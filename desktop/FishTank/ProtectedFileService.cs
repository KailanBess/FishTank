using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FishTank;

internal sealed class ProtectedSource : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StreamInfo { public long Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string Name; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindFirstStreamW(string path, int level, out StreamInfo info, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool FindNextStreamW(IntPtr handle, out StreamInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FindClose(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] private struct Info
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out Info info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetFileInformationByHandle(SafeFileHandle file, int kind, ref int data, uint size);
    private readonly List<SafeFileHandle> parents;
    internal readonly FileStream Stream;
    internal readonly string Path;
    private readonly bool canRemove;
    internal ProtectedSource(string path, bool move)
    {
        path = ScanPaths.Normalize(path); Path = path;
        if (move)
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!ScanPaths.Within(path, profile) || ScanPaths.Within(path, System.IO.Path.Combine(profile, "AppData"))
                || ScanPaths.Within(path, Environment.GetFolderPath(Environment.SpecialFolder.Windows))
                || ScanPaths.Within(path, Core.Data) || ScanPaths.Within(path, Core.Base)
                || System.IO.Path.GetFileName(path).StartsWith("NTUSER.", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Moving is limited to ordinary personal files inside your user profile, outside AppData and FishTank's application/data folders. System/account files are refused.");
        }
        parents = Vault.LockParents(path);
        SafeFileHandle handle = null;
        try
        {
            handle = CreateFile(path, 0x80000000u | (move ? 0x10000u : 0), 0, IntPtr.Zero, 3, 0x200000, IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out Info info) || (info.Attributes & 0x410) != 0 || info.Links != 1 || info.SizeHigh != 0 || info.SizeLow > FileSafe.MaximumContentBytes)
                throw new IOException("Close other programs using the file. Only local, single-link regular files up to 64 MiB can be protected.");
            if (move)
            {
                IntPtr search = FindFirstStreamW(path, 0, out StreamInfo stream, 0);
                if (search == new IntPtr(-1)) throw new IOException("Could not inspect alternate streams; move was refused. Use an encrypted copy instead.");
                try
                {
                    do { if (stream.Name != "::$DATA") throw new IOException("This file has alternate streams (possibly its internet-origin marker). Moving would lose metadata, so use an encrypted copy or Windows drive encryption instead."); }
                    while (FindNextStreamW(search, out stream));
                    if (Marshal.GetLastWin32Error() != 38) throw new IOException("Alternate stream inspection did not complete; originals were not moved.");
                }
                finally { FindClose(search); }
            }
            Stream = new FileStream(handle, FileAccess.Read); handle = null; canRemove = move;
        }
        catch { handle?.Dispose(); foreach (var parent in parents) parent.Dispose(); throw; }
    }
    internal void RemoveOriginal()
    {
        if (!canRemove) throw new InvalidOperationException("This source was opened for an encrypted copy, not a move.");
        int delete = 1;
        if (!SetFileInformationByHandle(Stream.SafeFileHandle, 4, ref delete, 4)) throw new IOException("Original removal failed; its plaintext remains outside the encrypted container.");
    }
    public void Dispose() { Stream.Dispose(); foreach (var parent in parents) parent.Dispose(); }
}

internal static class ProtectedFileService
{
    internal static string[] CollectFolder(string folder)
    {
        folder = ScanPaths.Normalize(folder); ScanPaths.RefuseReparseAncestors(folder);
        var pending = new Stack<string>(); pending.Push(folder); var files = new List<string>(); int entries = 0;
        while (pending.Count > 0)
            foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (++entries > 4096) throw new IOException("Folder scope exceeds 4,096 entries. Choose a smaller folder; no files were moved.");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Folder contains a link or cloud placeholder. Choose ordinary local files; no files were moved.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else { files.Add(path); if (files.Count > FileSafe.MaximumFiles) throw new IOException("Choose at most 256 files per container."); }
            }
        return files.ToArray();
    }
    internal static string Create(string[] paths, string folderRoot, string destination, string password, bool move)
    {
        if (paths == null || paths.Length < 1 || paths.Length > FileSafe.MaximumFiles) throw new IOException("Choose 1–256 files.");
        destination = ScanPaths.Normalize(destination); ScanPaths.RefuseReparseAncestors(System.IO.Path.GetDirectoryName(destination));
        string recovery = destination + ".recovery.ftsafe";
        if (File.Exists(destination) || File.Exists(recovery)) throw new IOException("Choose a new container name; existing files and backups are never overwritten.");
        var sources = new List<ProtectedSource>(); var failures = new List<string>(); int removed = 0;
        string fingerprint = null;
        try
        {
            var input = new List<FileSafeSource>();
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var source = new ProtectedSource(path, move); sources.Add(source);
                string name = folderRoot == null ? System.IO.Path.GetFileName(path) : path.Substring(folderRoot.TrimEnd('\\').Length).TrimStart('\\').Replace('\\', '/');
                input.Add(new FileSafeSource { Name = name, Content = source.Stream });
            }
            FileSafe.Create(destination, password, input);
            using (var verified = Open(destination, password)) { if (verified.Files.Count != input.Count) throw new IOException("Container verification failed; originals were not removed."); }
            File.Copy(destination, recovery, false);
            using (var durable = new FileStream(recovery, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) durable.Flush(true);
            using (var verified = Open(recovery, password)) { if (verified.Files.Count != input.Count) throw new IOException("Recovery copy verification failed; originals were not removed."); fingerprint = verified.ContainerSha256; }
            if (move)
                foreach (var source in sources)
                    try { source.RemoveOriginal(); removed++; } catch (IOException) { failures.Add(source.Path); }
        }
        finally { foreach (var source in sources) source.Dispose(); }
        string tracking = "";
        try { BackupHistory.Created(BackupHistory.ProtectedFiles, recovery, fingerprint, true); }
        catch { tracking = "Backup-age tracking could not be saved; the verified encrypted copies remain available. "; }
        return "Encrypted container and recovery copy verified. " + (move ? removed + " selected originals were moved. " : "Originals were NOT removed and are still readable outside FishTank. ")
            + (failures.Count > 0 ? failures.Count + " originals remain readable because removal failed:\n" + string.Join("\n", failures) + "\n" : "")
            + tracking + "Keep the recovery copy on separate protected storage. Deletion is not secure erasure; older copies, backups and disk remnants may still exist. There is no passphrase reset.";
    }
    internal static FileSafeSession Open(string path, string password)
    {
        path = ScanPaths.Normalize(path); ScanPaths.RefuseReparseAncestors(path);
        var parents = Vault.LockParents(path);
        try { return FileSafe.Open(path, password); }
        finally { foreach (var parent in parents) parent.Dispose(); }
    }
    internal static string Export(FileSafeSession session, string parent)
    {
        parent = ScanPaths.Normalize(parent); ScanPaths.RefuseReparseAncestors(parent);
        var held = Vault.LockParents(System.IO.Path.Combine(parent, "export-placeholder"));
        string destination = System.IO.Path.Combine(parent, "FishTank-restored-" + Guid.NewGuid().ToString("N"));
        try
        {
            string[] permittedNames = session.Files.Select(x => x.Name).ToArray();
            session.ExtractAll(destination, Core.RestrictDirectory);
            foreach (string name in permittedNames)
            {
                string file = System.IO.Path.Combine(destination, name.Replace('/', System.IO.Path.DirectorySeparatorChar));
                ScanPaths.RefuseReparseAncestors(file);
                NamedStreams.Write(file, "Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            }
            return "Permitted plaintext export saved to " + destination + ". Exports have an internet-origin marker so Windows can apply its normal warnings. These copies remain readable to your Windows account outside FishTank; locking the container does not revoke them.";
        }
        catch (Exception ex) { throw new IOException("Export did not complete. Partial permitted plaintext copies may remain at " + destination + ". Review them before relying on protection.", ex); }
        finally { foreach (var handle in held) handle.Dispose(); }
    }
}

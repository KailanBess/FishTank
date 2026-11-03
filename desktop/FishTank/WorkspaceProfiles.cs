using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
namespace FishTank;
public sealed class WorkspaceLayout { public string Name;public Appearance Appearance; }
public sealed class ShortcutBinding { public string Tool;public string Keys; }
internal static class ShortcutStore
{
    internal static List<ShortcutBinding> Read()=>ProtectedStore.Read("keyboard-shortcuts",()=>new List<ShortcutBinding>())??throw new InvalidDataException("Shortcuts unreadable.");
    internal static void Set(string letter,string tool)
    {
        letter=(letter??"").ToUpperInvariant();if(!Regex.IsMatch(letter,"\\A[A-Z]\\z")||!ToolCatalog.Ids.Contains(tool))throw new ArgumentException("Choose one letter A–Z and an available tool.");
        var rows=Read();rows.RemoveAll(x=>x.Keys==letter);rows.Add(new ShortcutBinding{Keys=letter,Tool=tool});ProtectedStore.Write("keyboard-shortcuts",rows);
    }
    internal static string Find(System.Windows.Forms.Keys keys)
    {
        if((keys & System.Windows.Forms.Keys.Modifiers)!=(System.Windows.Forms.Keys.Control|System.Windows.Forms.Keys.Alt))return null;
        string letter=(keys & System.Windows.Forms.Keys.KeyCode).ToString();return Read().FirstOrDefault(x=>x.Keys==letter)?.Tool;
    }
}
internal static class LayoutStore
{
    internal static List<WorkspaceLayout> Read()=>ProtectedStore.Read("saved-layouts",()=>new List<WorkspaceLayout>())??throw new InvalidDataException("Layouts unreadable.");
    internal static void Save(string name,Appearance appearance){if(string.IsNullOrWhiteSpace(name)||name.Length>64)throw new ArgumentException("Choose a layout name up to 64 characters.");appearance.Validate();var rows=Read();rows.RemoveAll(x=>x.Name==name);if(rows.Count>=20)throw new InvalidOperationException("At most 20 layouts are supported.");var copy=appearance.Copy();copy.WallpaperPath="";rows.Add(new WorkspaceLayout{Name=name,Appearance=copy});ProtectedStore.Write("saved-layouts",rows);}
    internal static Appearance Import(string text){if(text==null||text.Length>128*1024)throw new InvalidDataException("Appearance import is oversized.");var a=Core.Parse<Appearance>(text)??throw new InvalidDataException("Invalid appearance file.");a.WallpaperPath="";a.Validate();return a;}
}
internal static class ProfilePaths
{
    internal static string Resolve(string root,string name){if(!Regex.IsMatch(name??"","\\A[A-Za-z0-9_-]{1,32}\\z"))throw new ArgumentException("Profile names use 1–32 letters, numbers, underscores or hyphens.");return Path.Combine(Path.GetFullPath(root),"profiles",name);}
    internal static string[] Names(string root){string dir=Path.Combine(root,"profiles");return Directory.Exists(dir)?Directory.GetDirectories(dir).Select(Path.GetFileName).Where(x=>Regex.IsMatch(x,"\\A[A-Za-z0-9_-]{1,32}\\z")).ToArray():new string[0];}
    internal static void CopyEncryptedVault(string source,string destination){Directory.CreateDirectory(destination);byte[] bytes=LocalStoreFiles.ReadOptional(Path.Combine(source,"passwords.ftvault"),16*1024*1024)??throw new FileNotFoundException("No encrypted vault is available to migrate.");if(bytes.Length<96||System.Text.Encoding.ASCII.GetString(bytes,0,8)!="FTPV0001")throw new InvalidDataException("Source is not a recognized encrypted vault.");RestoreFileWriter.WriteNew(Path.Combine(destination,"passwords.ftvault"),bytes);}
}
internal static class HistoryPrivacy
{
    internal static FileInfo[] Candidates(int olderThanDays)
    {
        if(olderThanDays<1||olderThanDays>365)throw new ArgumentException("History age must be 1–365 days.");
        return new DirectoryInfo(Core.Data).GetFiles("events-*.jsonl").Where(x=>!x.Attributes.HasFlag(FileAttributes.ReparsePoint)&&Regex.IsMatch(x.Name,"\\Aevents-[0-9]{4}-[0-9]{2}-[0-9]{2}\\.jsonl\\z")&&DateTime.TryParseExact(x.Name.Substring(7,10),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal,out var date)&&date<DateTime.UtcNow.Date.AddDays(-olderThanDays)).ToArray();
    }
    internal static void DeleteSelected(string name)
    {
        if(!Regex.IsMatch(name??"","\\Aevents-[0-9]{4}-[0-9]{2}-[0-9]{2}\\.jsonl\\z")||name=="events-"+DateTime.UtcNow.ToString("yyyy-MM-dd")+".jsonl")throw new ArgumentException("Choose an older application history file.");
        string path=Path.Combine(Core.Data,name);ScanPaths.RefuseReparseAncestors(path);File.Delete(path);
    }
}
internal static class RemovableDriveOffers
{
    private static HashSet<string> seen=new HashSet<string>(DriveInfo.GetDrives().Where(x=>x.DriveType==DriveType.Removable&&x.IsReady).Select(x=>x.Name),StringComparer.OrdinalIgnoreCase);
    internal static string[] Poll(){var now=new HashSet<string>(DriveInfo.GetDrives().Where(x=>x.DriveType==DriveType.Removable&&x.IsReady).Select(x=>x.Name),StringComparer.OrdinalIgnoreCase);var added=now.Except(seen,StringComparer.OrdinalIgnoreCase).ToArray();seen=now;return added;}
}
internal static class OfflineHelp
{
    internal static readonly Dictionary<string,string> Articles=new Dictionary<string,string> {
        {"Protection and signatures","FishTank reports Windows evidence. Unknown or outdated readings are not safety verdicts. Refresh Dashboard protection checks. Antivirus → scanner policy shows Defender signature age and exclusions. Signature updates and Windows restarts remain under your control."},
        {"Scanning and queues","Use Antivirus for quick/full/custom scans. All tools → Scan queue runs pending items in order. Saved detailed profiles are available there. A cancelled command is not proof Defender stopped; review cancellation evidence. Custom scans do not use the quick/full cancel command."},
        {"Vault passwords and history","The master passphrase encrypts the vault; it cannot be reset. History, attachments and expiry live inside that same encrypted vault. Entry changes retain up to five previous passwords. Undo is limited to the last mutation in the unlocked session. Encrypted backups can retain older secrets."},
        {"Backups and recovery","Automatic backups copy already-encrypted vault bytes to your chosen destination. They do not store a master passphrase or certify a restore. Verify backups and run isolated restore drills from Recovery center. Keep the master passphrase and Windows recovery information separately."},
        {"Import and export","Preview an import before applying it. JSON retains email/service relationships; CSV requires Title and Password headers. Import is one atomic vault mutation. Plaintext exports contain credentials: choose their destination carefully and remove them with your own storage policy after use."},
        {"Browser pairing and server passkeys","The local browser dashboard and existing HTTPS verification server are separate services. Pairing is local and single-use. Server passkeys/security keys require your configured HTTPS origin and explicit enrollment. Private authenticator keys never enter the vault or device reports. Offline MFA remains available."},
        {"Troubleshooting a failed check","Refresh the relevant check, read its error and coverage text, and check Windows Security for native protection. Do not disable security software just to make a check pass. Use Preview support report to inspect and redact diagnostic summaries before sharing."},
        {"Profiles and migration","Profiles use separate local data folders and require their own app authentication. Profile migration copies the encrypted vault only; it does not silently transfer MFA secrets, tokens or Windows-bound data. Test the copy with its master passphrase before removing anything from the old profile."},
        {"Recovery USB folder","Copy verified FishTank installers and encrypted backups to separate storage, along with offline instructions. The recovery folder is not a bootable Windows USB. Keep the passphrase elsewhere. Windows installation/recovery media should be created with Microsoft's official recovery tools."},
        {"Privacy and diagnostics","Notification history is encrypted locally. Privacy controls can remove selected older application-history days. Backup retention is separate. Review and redact support reports before saving or sharing them. Remote status sharing starts off and excludes vault secrets, tokens, paths and raw command output."}
    };
}

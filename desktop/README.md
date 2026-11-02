# Recovered FishTank desktop and CLI source

Recovered with ILSpy command-line 9.1.0.7988 from the user-supplied FishTank.exe, assembly version 0.6.0.0. Both uploaded assemblies decompile to identical application C# files when using the official .NET Framework 4.8 reference assemblies. Their hashes also match the user's uploaded SHA256SUMS.txt:

```text
b60bd306b6a9366f6eee4777b8b7f093f7bd59c1087b0422fd1f74273cdc99e1  FishTank.exe
626dda273772c93836330de936514b5200c65d2c9a1fbb13f2aca67d156fd1ac  FishTank.Cli.exe
```

This is recovered source, not the lost original files. Original comments, source layout, tests, and some local names cannot be recovered. The collector PowerShell script, image, and icon were recovered as embedded resources. The generated project was adjusted to use portable framework references and target .NET Framework 4.8, matching the supplied build documentation. No application behavior was intentionally changed.

## Build

Install the .NET 8 SDK. From the repository root:

```sh
dotnet build desktop/FishTank.csproj -c Release
dotnet build desktop/FishTank.csproj -c Release -p:BuildCli=true
```

Both outputs are under `desktop/bin/Release/net48/`. Package restore uses Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3 from NuGet. The recovered C# uses C# 12 syntax, so use this SDK project rather than the old .NET Framework csc.exe build script.

Both builds passed in the Linux recovery workspace with zero warnings and zero errors. Compilation does not validate Windows runtime behavior, GUI layouts, Defender integration, DPAPI storage, service installation, or live MFA delivery. The original desktop tests were not supplied and have not been recreated. The original executables under `windows/` have not been replaced by the recovered builds. New builds remain unsigned and are not byte-identical to the originals.

Run and validate rebuilt applications on Windows 10/11 with .NET Framework 4.8 before replacing an installed copy. Preserve private data backups before testing password storage and authentication. Keep private runtime configuration and data out of Git.

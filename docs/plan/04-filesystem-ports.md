# 04 - Filesystem & Platform Ports

**Parent:** [00-high-level.md](00-high-level.md) §5, §6, §8
**Milestone:** M3 (ports), consumed by M2/M4
**Status:** Approved

## 1. Scope

Define the platform-touching ports and adapters in `LincleLINK.Core`: the IO facade (`IFileSystem`), cross-platform hard links (`IHardLinker`), file hashing (`IFileHasher`), free-space detection (`IDriveInfoProvider`), and path normalization (`PathNormalizer`). These are the seams that make V2's Windows-only code cross-platform and unit-testable.

## 2. Layout

| Concern | Port | Impl(s) |
|---|---|---|
| IO facade for services | `Abstractions/Filesystem/IFileSystem.cs` | `Infrastructure/Filesystem/FileSystem.cs` |
| Hard links | `Abstractions/Linking/IHardLinker.cs` | `Infrastructure/Linking/Win32HardLinker.cs` · `UnixHardLinker.cs` |
| Hashing | `Abstractions/Hashing/IFileHasher.cs` | `Infrastructure/Hashing/Md5FileHasher.cs` |
| Free space / totals | `Abstractions/Disk/IDriveInfoProvider.cs` | `Infrastructure/Disk/DriveInfoProvider.cs` (Windows) · `UnixStatFsDriveInfoProvider.cs` (Linux) |
| Path normalization | (pure, Domain) `Domain/PathNormalizer.cs` | - |

## 3. `IFileSystem` - thin IO facade (D1)

```csharp
public interface IFileSystem
{
    bool FileExists(string path);
    long GetFileLength(string path);
    Task CopyFileAsync(string source, string dest, bool overwrite, CancellationToken ct = default);
    Task MoveFileAsync(string source, string dest, bool overwrite, CancellationToken ct = default);
    bool DeleteFile(string path);

    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    IReadOnlyList<string> EnumerateFiles(string root, bool recursive);
    IReadOnlyList<string> EnumerateDirectories(string root, bool recursive);
}
```

- Used by **application services** (`InstanceService` enumeration + relative-path IO, `LinkingService` target dirs/dup checks, `CopyFiles`) so those services can be unit-tested with `NSubstitute` as well as real-IO `TempDir` tests.
- **Repositories/store (`03`) intentionally use real `System.IO`** against `TempDir` - no facade there (avoids double abstraction).
- `EnumerateFiles` returns full paths; relative-path computation is done by services via `PathNormalizer`.
- Implementation delegates to `System.IO` (file-scoped async via `File.CopyAsync`/`File.MoveAsync`, net10).

## 4. `IHardLinker` - cross-platform hard links (D2)

```csharp
public interface IHardLinker
{
    bool TryCreateLink(string sourcePath, string linkPath, out string? error);
}
```

- **`TryCreateLink` returns `false` + a user-presentable `error`** instead of throwing per file (V2 threw inside a big try/catch, aborting the whole op on the first failure; we want per-file failures logged and the rest to proceed). Services decide whether to abort.
- **Windows** (`Win32HardLinker`, `[SupportedOSPlatform("windows")]`): `CreateHardLinkW` (kernel32, P/Invoke, `CharSet.Unicode`); failure → `Marshal.GetLastWin32Error()` mapped to message.
- **Linux** (`UnixHardLinker`, `[SupportedOSPlatform("linux")]`): `link(oldpath, newpath)` from `libc`; return code 0 = success; nonzero `errno` mapped to message. Key mappings: `EXDEV` → "source and target are on different filesystems" (matches the V2 hard-link same-partition limitation), `EMLINK` → "maximum hard-link count reached", `ENOENT`/`EPERM` → generic "could not create link".
- **Selection** is centralized in `AddLincleLINKCore` via `OperatingSystem.IsWindows()`; tests can register either impl explicitly.

## 5. `IFileHasher` - MD5 (D5)

```csharp
public interface IFileHasher
{
    Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default);
}
```

- `Md5FileHasher` returns the **uppercase hex MD5 with no dashes** - byte-identical to V2 (`BitConverter.ToString(hash).Replace("-", "")`), so hashed names in existing `db/` match.
- Implementation: `MD5.HashDataAsync(File.OpenRead(path), ct)` (streaming, net10), uppercase hex via `Convert.ToHexString`.
- The store name is built by services as `hash + Path.GetExtension(fileName)` (V2 parity). Extension keeps original case; files with no extension → hash only (matches `FileStore` guard regex from `03`).
- Cancellation supported for the long add-instance hashing loop.

## 6. `IDriveInfoProvider` - free space (D3)

```csharp
public interface IDriveInfoProvider
{
    long GetAvailableFreeSpace(string path);
    long GetTotalSize(string path);
}
```

Used for the V2 low-disk-space warning (add instance) and the "Free drive space" status line.

- **Windows** (`DriveInfoProvider`): find `DriveInfo` whose `Name` is a prefix of `Path.GetPathRoot(path)` (V2 prefix logic), fall back to `Path.GetPathRoot`.
- **Linux** (`UnixStatFsDriveInfoProvider`): `statvfs(path)` P/Invoke (`f_bfree * f_frsize`, `f_blocks * f_frsize`) - more reliable than `DriveInfo` on unusual mounts.
- Selection via `OperatingSystem.IsWindows()` in composition (mirrors hard-linker selection).

## 7. `PathNormalizer` - stored vs platform paths (D4)

V2 persisted `RelativePath`/`DirectoryList` with backslashes and read torrent relative paths (`contents\data`) that way. On Linux those same JSON files still contain `\`. Pure static helpers:

```csharp
public static class PathNormalizer
{
    string ToPlatformSeparators(string storedPath);       // \ and / → Path.DirectorySeparatorChar
    string StripLeadingSeparators(string path);            // V2 import: remove leading '\' or '/'
    bool IsSafeRelativePath(string path);                  // no root, no '..', no empty segments that imply traversal
    string Canonicalize(string path);                      // separators → '/' + normalize (matching key)
}
```

- **Materializing** (link/copy targets): `ToPlatformSeparators` converts stored backslash paths to the host separator.
- **Matching** (torrent flow, legacy import): `Canonicalize` maps both `\` and `/` to `/` and normalizes, so an instance stored with `\` matches a BEP forward-slash torrent path on either OS.
- **Safety**: `IsSafeRelativePath` rejects rooted/`..`/empty-segment paths; used as a guard before writing any target path derived from stored or torrent data.
- On save, `InstanceService` stores the platform-native relative path as-is (no forced canonical form); normalization always happens at use time.

## 8. `AddLincleLINKCore` registration additions

```csharp
services.AddSingleton<IFileSystem, FileSystem>();
services.AddSingleton<IFileHasher, Md5FileHasher>();
services.AddSingleton<IDriveInfoProvider>(OperatingSystem.IsWindows()
    ? new DriveInfoProvider()
    : new UnixStatFsDriveInfoProvider());
services.AddSingleton<IHardLinker>(OperatingSystem.IsWindows()
    ? new Win32HardLinker()
    : new UnixHardLinker());
```

`PathNormalizer` is static (no registration).

## 9. Test plan (`tests/LincleLINK.Core.Tests/`)

**`Filesystem/FileSystemTests`** (real `TempDir`): recursive enumeration, copy/move overwrite semantics, delete, exists.

**`Linking/HardLinkerTests`** (platform-conditional):
- Windows/Linux: create link → both paths exist, content identical; delete source → target still intact.
- Linux-only: `link()` on missing source → `TryCreateLink` false + `ENOENT` message.
- Cross-device `EXDEV` is covered by unit-testing the errno→message mapper directly (no cross-volume fixture).

**`Hashing/Md5FileHasherTests`**: known bytes → known uppercase-hex MD5; matches V2 `GetMD5Checksum` output for a fixture file; large-file streaming; cancellation throws `OperationCanceledException`.

**`Disk/DriveInfoProviderTests`**: current dir free space `> 0`, total `> 0`; Windows impl on root; Linux statvfs impl returns sane values. (Real calls - no mocks needed.)

**`PathNormalizerTests`** (pure):
- `\`/`/` → host separator; mixed separators; `StripLeadingSeparators` on `\sound`, `/sound`;
- `IsSafeRelativePath` rejects rooted, `..`, empty segments; accepts normal relative paths;
- `Canonicalize` equality across `\`-vs-`/` inputs (the torrent-matching contract).

**Port mocks** used by application-service tests (`InstanceServiceTests`, `LinkingServiceTests`, `TorrentServiceTests`): `IFileSystem`, `IFileHasher`, `IDriveInfoProvider`, `IHardLinker`.

## 10. Decisions (locked)

- **D1** Minimal `IFileSystem` facade for application services; repos/store keep real IO.
- **D2** `IHardLinker.TryCreateLink` returns `bool` + error string (per-file failures don't abort the whole operation).
- **D3** `IDriveInfoProvider`: Windows `DriveInfo`, Linux `statvfs`.
- **D4** `PathNormalizer` canonicalizes to `/` for matching, converts to host separators for materialization, guards against traversal.
- **D5** `Md5FileHasher` returns uppercase hex, byte-compatible with V2.

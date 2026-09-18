using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Abstractions.Games;

public interface IGameVersionDetector
{
    Task<DetectionResult> DetectAsync(string rootPath, CancellationToken ct = default);

    /// <summary>
    /// The same detection over another file system, e.g. a view of an Instance
    /// that is not deployed anywhere (plan 16 D10).
    /// </summary>
    Task<DetectionResult> DetectAsync(IFileSystem fileSystem, string rootPath, CancellationToken ct = default);

    /// <summary>
    /// Whether a file of this name is one the detector identifies a game by: a
    /// config it reads or a game DLL it knows. Lets a caller tell whether a change
    /// to an Instance can change what is detected.
    /// </summary>
    bool IsIdentityFile(string fileName);
}

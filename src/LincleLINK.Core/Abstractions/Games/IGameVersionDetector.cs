using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Abstractions.Games;

public interface IGameVersionDetector
{
    Task<DetectionResult> DetectAsync(string rootPath, CancellationToken ct = default);
}

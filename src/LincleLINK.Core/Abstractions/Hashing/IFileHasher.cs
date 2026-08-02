namespace LincleLINK.Core.Abstractions.Hashing;

public interface IFileHasher
{
    /// <summary>Returns the uppercase hex hash (no dashes) of the file's contents.</summary>
    Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default);
}

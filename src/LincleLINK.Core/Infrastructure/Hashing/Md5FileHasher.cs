using System.Security.Cryptography;
using LincleLINK.Core.Abstractions.Hashing;

namespace LincleLINK.Core.Infrastructure.Hashing;

/// <summary>
/// MD5 hashing returning uppercase hex (no dashes) - byte-identical to v2's
/// <c>GetMD5Checksum</c>, so existing <c>db/</c> hashed names match.
/// </summary>
public sealed class Md5FileHasher : IFileHasher
{
    public async Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await MD5.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }
}

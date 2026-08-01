using FluentAssertions;
using LincleLINK.Core.Infrastructure.Hashing;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Hashing;

public sealed class Md5FileHasherTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Computes_uppercase_hex_md5()
    {
        var path = _temp.CreateFile("hello.txt", "hello world"u8.ToArray());

        var hash = await new Md5FileHasher().ComputeHashAsync(path);

        hash.Should().Be("5EB63BBBE01EEED093CB22BB8F5ACDC3");
    }

    [Fact]
    public async Task Hash_is_32_uppercase_hex_characters()
    {
        var path = _temp.CreateFile("data.bin", [0x00, 0x01, 0xFE, 0xFF]);

        var hash = await new Md5FileHasher().ComputeHashAsync(path);

        hash.Should().MatchRegex("^[0-9A-F]{32}$");
    }

    [Fact]
    public async Task Cancelled_token_aborts()
    {
        var path = _temp.CreateFile("data.bin", [1, 2, 3]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => new Md5FileHasher().ComputeHashAsync(path, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

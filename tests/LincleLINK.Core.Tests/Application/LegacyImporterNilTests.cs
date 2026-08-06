using System.Text;
using FluentAssertions;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Application;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// Nil-manifest and nil-location handling in <see cref="LegacyImporter"/>: an
/// <c>xsi:nil</c> root deserializes to a null <c>DBInfo</c>, and a nil
/// <c>Location</c> carries no directory info so its file is skipped.
/// </summary>
public sealed class LegacyImporterNilTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IAppPaths _paths;
    private readonly LegacyImporter _importer;

    public LegacyImporterNilTests()
    {
        _paths = new AppPaths(Path.Combine(_temp.Root, "data"));
        _importer = new LegacyImporter(new JsonInstanceRepository(_paths), NullLogger<LegacyImporter>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Nil_root_manifest_yields_empty_result()
    {
        var xmlPath = _temp.CreateFile("DBInfo.xml", Encoding.UTF8.GetBytes(
            """<?xml version="1.0"?><DBInfo xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:nil="true" />"""));

        var result = await _importer.ImportAsync(xmlPath, TestContext.Current.CancellationToken);

        result.Imported.Should().BeEmpty();
        result.SkippedExisting.Should().BeEmpty();
    }

    [Fact]
    public void TryBuild_rejects_nil_location()
    {
        var fileInfo = new LegacyImporter.InstanceFileInfo
        {
            OriginalFileName = "nil.bin",
            HashedFileName = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin",
            Location = null,
            SizeBytes = 10,
        };

        LegacyImporter.TryBuild(fileInfo, out var file).Should().BeFalse();
        file.Should().BeNull();
    }
}

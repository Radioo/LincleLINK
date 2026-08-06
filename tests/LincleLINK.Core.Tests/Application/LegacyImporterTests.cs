using System.Text;
using FluentAssertions;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Application;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

public sealed class LegacyImporterTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IAppPaths _paths;
    private readonly IInstanceRepository _repository;
    private readonly LegacyImporter _importer;

    public LegacyImporterTests()
    {
        _paths = new AppPaths(Path.Combine(_temp.Root, "data"));
        _repository = new JsonInstanceRepository(_paths);
        _importer = new LegacyImporter(_repository, NullLogger<LegacyImporter>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Import_creates_instances_with_v2_path_semantics()
    {
        var xmlPath = _temp.CreateFile("DBInfo.xml", Encoding.UTF8.GetBytes(TestData.V1DbInfoXml));

        var result = await _importer.ImportAsync(xmlPath, TestContext.Current.CancellationToken);

        result.Imported.Should().BeEquivalentTo("IIDX28", "Dupe");
        result.SkippedExisting.Should().BeEmpty();

        var imported = await _repository.GetAsync("IIDX28", TestContext.Current.CancellationToken);
        imported.Should().NotBeNull();
        imported!.InstanceName.Should().Be("IIDX28");
        imported.FileList.Should().ContainSingle();
        imported.FileList[0].FileName.Should().Be("25063_pre.2dx");
        imported.FileList[0].RelativePath.Should().Be(@"sound\25063"); // leading backslash stripped
        imported.FileList[0].HashedFileName.Should().Be("7AFE6AC1B80128D44BA5357D4349B21A.2dx");
        imported.FileList[0].FileSize.Should().Be(463806);
        imported.DirectoryList.Should().Equal(@"sound\25063");
        imported.TotalFileCount.Should().Be(1);
        imported.TotalFileSize.Should().Be(463806);
    }

    [Fact]
    public async Task Import_skips_existing_instances()
    {
        await _repository.SaveAsync(LincleLINK.Core.Domain.Instance.Create("IIDX28", [], []), TestContext.Current.CancellationToken);
        var xmlPath = _temp.CreateFile("DBInfo.xml", Encoding.UTF8.GetBytes(TestData.V1DbInfoXml));

        var result = await _importer.ImportAsync(xmlPath, TestContext.Current.CancellationToken);

        result.Imported.Should().Equal("Dupe");
        result.SkippedExisting.Should().Equal("IIDX28");
    }

    [Fact]
    public async Task Import_skips_files_with_unsafe_paths()
    {        const string unsafeXml = """
        <?xml version="1.0"?>
        <DBInfo>
          <InstanceList>
            <DataInstance>
              <InstanceName>Safe</InstanceName>
              <InstanceFiles>
                <InstanceFileInfo>
                  <OriginalFileName>ok.bin</OriginalFileName>
                  <HashedFileName>AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin</HashedFileName>
                  <Location>sound</Location>
                  <SizeBytes>10</SizeBytes>
                </InstanceFileInfo>
                <InstanceFileInfo>
                  <OriginalFileName>evil.bin</OriginalFileName>
                  <HashedFileName>BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin</HashedFileName>
                  <Location>..\..\escape</Location>
                  <SizeBytes>10</SizeBytes>
                </InstanceFileInfo>
                <InstanceFileInfo>
                  <OriginalFileName>..\rooted.bin</OriginalFileName>
                  <HashedFileName>CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC.bin</HashedFileName>
                  <Location>sound</Location>
                  <SizeBytes>10</SizeBytes>
                </InstanceFileInfo>
              </InstanceFiles>
            </DataInstance>
          </InstanceList>
        </DBInfo>
        """;
        var xmlPath = _temp.CreateFile("DBInfo.xml", Encoding.UTF8.GetBytes(unsafeXml));

        var result = await _importer.ImportAsync(xmlPath, TestContext.Current.CancellationToken);

        result.Imported.Should().Equal("Safe");

        var imported = await _repository.GetAsync("Safe", TestContext.Current.CancellationToken);
        imported.Should().NotBeNull();
        imported!.FileList.Should().ContainSingle();
        imported.FileList[0].FileName.Should().Be("ok.bin");
    }

    [Fact]
    public async Task Import_with_nil_location_does_not_crash()
    {
        const string nilXml = """
        <?xml version="1.0"?>
        <DBInfo>
          <InstanceList>
            <DataInstance>
              <InstanceName>Nil</InstanceName>
              <InstanceFiles>
                <InstanceFileInfo>
                  <OriginalFileName>nil.bin</OriginalFileName>
                  <HashedFileName>BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin</HashedFileName>
                  <Location xsi:nil="true" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" />
                  <SizeBytes>10</SizeBytes>
                </InstanceFileInfo>
              </InstanceFiles>
            </DataInstance>
          </InstanceList>
        </DBInfo>
        """;
        var xmlPath = _temp.CreateFile("DBInfo.xml", Encoding.UTF8.GetBytes(nilXml));

        var result = await _importer.ImportAsync(xmlPath, TestContext.Current.CancellationToken);

        result.Imported.Should().Equal("Nil");
        var imported = await _repository.GetAsync("Nil", TestContext.Current.CancellationToken);
        imported.Should().NotBeNull();
        // A nil Location is treated as a root-level entry; the file is still imported.
        imported!.FileList.Should().ContainSingle(f => f.FileName == "nil.bin");
    }

    [Fact]
    public async Task Import_corrupt_xml_throws_typed_exception_with_path()
    {
        var xmlPath = _temp.CreateFile("DBInfo.xml", Encoding.UTF8.GetBytes("this is not xml"));

        var act = () => _importer.ImportAsync(xmlPath);

        var ex = await act.Should().ThrowAsync<LegacyImportException>();
        ex.WithMessage("*not a valid v1 DBInfo.xml*");
    }
}

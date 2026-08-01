namespace LincleLINK.Core.Tests.TestHelpers;

public static class TestData
{
    /// <summary>
    /// An instance manifest in the exact v2 on-disk schema (System.Text.Json default
    /// PascalCase naming, indented). Captured from real v2 output.
    /// </summary>
    public const string V2InstanceJson = """
        {
          "Name": "IIDX28",
          "TotalFileSize": 463806,
          "TotalFileCount": 1,
          "TotalFileSizeString": "452.94 KB",
          "FileList": [
            {
              "FileName": "25063_pre.2dx",
              "RelativePath": "sound\\25063",
              "FileSize": 463806,
              "HashedFileName": "7AFE6AC1B80128D44BA5357D4349B21A.2dx"
            }
          ],
          "DirectoryList": [
            "sound\\25063"
          ]
        }
        """;

    /// <summary>A v1 DBInfo.xml manifest in the format produced by the legacy app.</summary>
    public const string V1DbInfoXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <DBInfo>
          <InstanceList>
            <DataInstance>
              <InstanceName>IIDX28</InstanceName>
              <InstanceFiles>
                <InstanceFileInfo>
                  <OriginalFileName>25063_pre.2dx</OriginalFileName>
                  <HashedFileName>7AFE6AC1B80128D44BA5357D4349B21A.2dx</HashedFileName>
                  <Location>\sound\25063</Location>
                  <SizeBytes>463806</SizeBytes>
                </InstanceFileInfo>
              </InstanceFiles>
              <Entries>1</Entries>
              <Size>463806</Size>
            </DataInstance>
            <DataInstance>
              <InstanceName>Dupe</InstanceName>
              <InstanceFiles>
                <InstanceFileInfo>
                  <OriginalFileName>other.bin</OriginalFileName>
                  <HashedFileName>00000000000000000000000000000000.bin</HashedFileName>
                  <Location>sub</Location>
                  <SizeBytes>100</SizeBytes>
                </InstanceFileInfo>
              </InstanceFiles>
              <Entries>1</Entries>
              <Size>100</Size>
            </DataInstance>
          </InstanceList>
        </DBInfo>
        """;
}

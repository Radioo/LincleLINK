namespace LincleLINK.Core.Abstractions.Disk;

public interface IDriveInfoProvider
{
    long GetAvailableFreeSpace(string path);
}

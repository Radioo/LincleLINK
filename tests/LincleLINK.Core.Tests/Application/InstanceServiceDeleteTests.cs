using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Games;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// <see cref="InstanceService.DeleteInstanceAsync"/>: the confirm dialog gates the
/// repository delete, and both confirmations are reported back.
/// </summary>
public sealed class InstanceServiceDeleteTests
{
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IHardLinker _hardLinker = Substitute.For<IHardLinker>();
    private readonly IHardLinkPreflight _preflight = Substitute.For<IHardLinkPreflight>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IGameVersionDetector _detector = Substitute.For<IGameVersionDetector>();

    private InstanceService CreateService() =>
        new(_fs, _hasher, _store, _hardLinker, _preflight, _repository, _driveInfo, _dialogs, _detector, NullLogger<InstanceService>.Instance);

    [Fact]
    public async Task Confirmed_delete_deletes_and_reports_success()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _repository.DeleteAsync("IIDX28", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().DeleteInstanceAsync("IIDX28", TestContext.Current.CancellationToken);

        result.Deleted.Should().BeTrue();
        result.Cancelled.Should().BeFalse();
        await _dialogs.Received(1).ConfirmAsync(Arg.Any<string>(), "Remove from library");
        await _repository.Received(1).DeleteAsync("IIDX28", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Declined_delete_does_not_delete()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var result = await CreateService().DeleteInstanceAsync("IIDX28", TestContext.Current.CancellationToken);

        result.Deleted.Should().BeFalse();
        result.Cancelled.Should().BeTrue();
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_missing_instance_reports_not_deleted()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _repository.DeleteAsync("nope", Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateService().DeleteInstanceAsync("nope", TestContext.Current.CancellationToken);

        result.Deleted.Should().BeFalse();
        result.Cancelled.Should().BeFalse();
    }
}

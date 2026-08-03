using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

public sealed class UnusedFilesServiceTests
{
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private UnusedFilesService CreateService() => new(_store, _repository, _dialogs);

    [Fact]
    public async Task No_unused_files_shows_info_and_deletes_nothing()
    {
        _store.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>())
            .Returns(["AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin"]);
        _repository.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>())
            .Returns(["AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin"]);

        var result = await CreateService().CheckAndDeleteAsync(threadCount: 4);

        await _dialogs.Received(1).InfoAsync(Arg.Any<string>(), Arg.Any<string>());
        result.Found.Should().Be(0);
        await _store.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unused_files_deleted_after_confirmation()
    {
        _store.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>())
            .Returns(["AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin"]);
        _repository.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>())
            .Returns(["AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin"]);
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var result = await CreateService().CheckAndDeleteAsync(threadCount: 4);

        result.Found.Should().Be(1);
        result.Deleted.Should().Be(1);
        await _store.Received(1).DeleteAsync("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unused_files_confirmation_declined_deletes_nothing()
    {
        _store.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>())
            .Returns(["BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin"]);
        _repository.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var result = await CreateService().CheckAndDeleteAsync(threadCount: 4);

        result.Cancelled.Should().BeTrue();
        result.Found.Should().Be(1);
        result.Deleted.Should().Be(0);
        await _store.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

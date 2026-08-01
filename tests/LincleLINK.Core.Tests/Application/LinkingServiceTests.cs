using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

public sealed class LinkingServiceTests
{
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IHardLinker _hardLinker = Substitute.For<IHardLinker>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private LinkingService CreateService() => new(_fs, _store, _hardLinker, _repository, _dialogs);

    private static Instance SampleInstance() => Instance.Create(
        "inst",
        [
            new InstanceFile("a.bin", "", 10, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin"),
            new InstanceFile("b.bin", "sub", 20, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin"),
        ],
        ["sub"]);

    [Fact]
    public async Task Folder_pick_cancelled_returns_cancelled_without_work()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns((string?)null);

        var result = await CreateService().LinkInstanceAsync("inst");

        result.Cancelled.Should().BeTrue();
        await _repository.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_instance_returns_error()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("nope", Arg.Any<CancellationToken>()).Returns((Instance?)null);

        var result = await CreateService().LinkInstanceAsync("nope");

        result.Cancelled.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Creates_directories_before_linking()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());
        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });

        var result = await CreateService().LinkInstanceAsync("inst");

        var sep = Path.DirectorySeparatorChar;
        _fs.Received(1).CreateDirectory($"C:\\target{sep}sub");
        result.Cancelled.Should().BeFalse();
        result.Linked.Should().Be(2);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task Duplicates_without_confirmation_cancel_operation()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());
        _fs.FileExists(Arg.Any<string>()).Returns(true); // dupes exist
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var result = await CreateService().LinkInstanceAsync("inst");

        result.Cancelled.Should().BeTrue();
        _hardLinker.DidNotReceiveWithAnyArgs().TryCreateLink(default!, default!, out _);
    }

    [Fact]
    public async Task Duplicates_with_confirmation_delete_then_link()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());
        _fs.FileExists(Arg.Any<string>()).Returns(true);
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });

        var result = await CreateService().LinkInstanceAsync("inst");

        result.Cancelled.Should().BeFalse();
        _fs.Received().DeleteFile(Arg.Any<string>());
        result.Linked.Should().Be(2);
    }

    [Fact]
    public async Task Per_file_link_failures_are_logged_and_loop_continues()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());
        _fs.FileExists(Arg.Any<string>()).Returns(false);

        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _)
            .Returns(x =>
            {
                x[2] = "boom";
                return false;
            });

        var result = await CreateService().LinkInstanceAsync("inst");

        // Every file was attempted and failed; the loop did not abort.
        result.Cancelled.Should().BeFalse();
        result.Linked.Should().Be(0);
        result.Failed.Should().Be(2);
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().OnlyContain(e => e.Contains("boom"));
    }

    [Fact]
    public async Task Unsafe_directory_is_skipped()
    {
        var instance = Instance.Create("inst", [], ["..\\evil"]);
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(instance);

        var result = await CreateService().LinkInstanceAsync("inst");

        _fs.DidNotReceiveWithAnyArgs().CreateDirectory(default!);
        result.Errors.Should().Contain(e => e.Contains("unsafe"));
    }

    [Fact]
    public async Task Copy_hashed_skips_existing_and_copies_new()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\dest");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());
        _fs.FileExists("C:\\dest\\AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns(true);
        _fs.FileExists("C:\\dest\\BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin").Returns(false);

        var result = await CreateService().CopyHashedFilesAsync("inst");

        result.Cancelled.Should().BeFalse();
        result.AlreadyExisted.Should().Be(1);
        result.Copied.Should().Be(1);
        await _store.Received(1).CopyOutAsync("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin", "C:\\dest\\BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Copy_hashed_pick_cancelled_returns_cancelled()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns((string?)null);

        var result = await CreateService().CopyHashedFilesAsync("inst");

        result.Cancelled.Should().BeTrue();
    }
}

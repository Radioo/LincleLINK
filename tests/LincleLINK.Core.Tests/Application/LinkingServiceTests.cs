using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

public sealed class LinkingServiceTests
{
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IHardLinker _hardLinker = Substitute.For<IHardLinker>();
    private readonly IHardLinkPreflight _preflight = Substitute.For<IHardLinkPreflight>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    // The preflight substitute returns null (= linkable) by default; the
    // cross-volume test overrides it explicitly.
    private LinkingService CreateService() => new(_fs, _store, _hardLinker, _preflight, _repository, _dialogs, NullLogger<LinkingService>.Instance);

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

        var expectedDir = PathNormalizer.ToPlatformSeparators(Path.Combine("C:\\target", "sub"));
        _fs.Received(1).CreateDirectory(expectedDir);
        result.Cancelled.Should().BeFalse();
        result.Linked.Should().Be(2);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task Cross_volume_target_fails_preflight_with_one_error()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("D:\\target");
        _preflight.CheckLinkTo("D:\\target").Returns("The folder is on a different drive than storage.");

        var result = await CreateService().LinkInstanceAsync("inst");

        result.Cancelled.Should().BeTrue();
        await _dialogs.Received(1).ErrorAsync(
            Arg.Is<string>(m => m != null && m.Contains("different drive")), Arg.Any<string>());
        await _repository.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _hardLinker.DidNotReceiveWithAnyArgs().TryCreateLink(default!, default!, out _);
    }

    [Fact]
    public async Task Conflicting_files_cancel_choice_cancels_operation()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());
        _fs.FileExists(Arg.Any<string>()).Returns(true); // dupes exist
        _dialogs.AskConflictAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ConflictChoice.Cancel);

        var result = await CreateService().LinkInstanceAsync("inst");

        result.Cancelled.Should().BeTrue();
        _hardLinker.DidNotReceiveWithAnyArgs().TryCreateLink(default!, default!, out _);
    }

    [Fact]
    public async Task Conflicting_files_replace_choice_deletes_then_links()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());
        _fs.FileExists(Arg.Any<string>()).Returns(true);
        _dialogs.AskConflictAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ConflictChoice.Replace);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });

        var result = await CreateService().LinkInstanceAsync("inst");

        result.Cancelled.Should().BeFalse();
        _fs.Received().DeleteFile(Arg.Any<string>());
        result.Linked.Should().Be(2);
        result.SkippedExisting.Should().Be(0);
    }

    [Fact]
    public async Task Conflicting_files_skip_choice_links_only_missing()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());
        // a.bin already exists at the target; b.bin does not.
        _fs.FileExists(Arg.Is<string>(p => p != null && p.EndsWith("a.bin", StringComparison.Ordinal))).Returns(true);
        _dialogs.AskConflictAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(ConflictChoice.Skip);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });

        var result = await CreateService().LinkInstanceAsync("inst");

        result.Cancelled.Should().BeFalse();
        result.Linked.Should().Be(1);
        result.SkippedExisting.Should().Be(1);
        result.Failed.Should().Be(0);
        // Skip must never delete what is already there.
        _fs.DidNotReceiveWithAnyArgs().DeleteFile(default!);
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
    public async Task Unsafe_file_paths_cannot_escape_target_directory()
    {
        var instance = Instance.Create(
            "inst",
            [
                new InstanceFile("evil.bin", @"..\..\escape", 10, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin"),
                new InstanceFile("rooted.bin", "C:\\absolute", 10, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin"),
                new InstanceFile("badname.bin", "sub", 10, "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC.bin"),
            ],
            ["sub"]);
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(instance);
        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });

        var result = await CreateService().LinkInstanceAsync("inst");

        // The '..' and rooted relative paths are rejected; only the safe one links.
        var receivedTargets = _hardLinker.ReceivedCalls()
            .Select(c => c.GetArguments()[1] as string)
            .Where(s => s is not null)
            .Cast<string>()
            .ToList();

        receivedTargets.Should().HaveCount(1);
        receivedTargets[0].Should().NotContain(@"..\..");
        receivedTargets[0].Should().NotContain(@"C:\absolute");
        result.Linked.Should().Be(1);
        result.Failed.Should().Be(2);
        result.Errors.Should().Contain(e => e.Contains("unsafe path skipped"));
    }

    [Fact]
    public async Task Copy_hashed_skips_existing_and_copies_new()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\dest");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());
        _fs.FileExists(Path.Combine("C:\\dest", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin")).Returns(true);
        _fs.FileExists(Path.Combine("C:\\dest", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin")).Returns(false);

        var result = await CreateService().CopyHashedFilesAsync("inst");

        result.Cancelled.Should().BeFalse();
        result.AlreadyExisted.Should().Be(1);
        result.Copied.Should().Be(1);
        await _store.Received(1).CopyFromStoreAsync(
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin",
            Path.Combine("C:\\dest", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Copy_hashed_pick_cancelled_returns_cancelled()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns((string?)null);

        var result = await CreateService().CopyHashedFilesAsync("inst");

        result.Cancelled.Should().BeTrue();
    }
}

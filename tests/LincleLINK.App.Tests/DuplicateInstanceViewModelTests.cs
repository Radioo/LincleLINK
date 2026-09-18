using FluentAssertions;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Games;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

public sealed class DuplicateInstanceViewModelTests
{
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();

    private DuplicateInstanceViewModel CreateViewModel(string sourceName = "IIDX 32")
    {
        var service = new InstanceService(
            Substitute.For<IFileSystem>(), Substitute.For<IFileHasher>(), Substitute.For<IFileStore>(),
            Substitute.For<IHardLinker>(), Substitute.For<IHardLinkPreflight>(), _repository,
            Substitute.For<IDriveInfoProvider>(), Substitute.For<IDialogService>(),
            Substitute.For<IGameVersionDetector>(), NullLogger<InstanceService>.Instance);
        var vm = new DuplicateInstanceViewModel(service, NullLogger<DuplicateInstanceViewModel>.Instance);
        vm.Start(sourceName);
        return vm;
    }

    [Fact]
    public void Start_prefills_the_name_from_the_source()
    {
        var vm = CreateViewModel();

        vm.NewName.Should().Be("IIDX 32 - copy");
        vm.Error.Should().BeEmpty();
        vm.DuplicateCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void An_invalid_name_shows_why_and_blocks_the_command()
    {
        var vm = CreateViewModel();

        vm.NewName = "bad:name";

        vm.Error.Should().NotBeEmpty();
        vm.DuplicateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_saves_the_copy_reports_its_name_and_closes()
    {
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>())
            .Returns(Instance.Create("IIDX 32", [new InstanceFile("a.bin", "", 1, "AA.bin")], []));
        var vm = CreateViewModel();
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        vm.NewName = "IIDX 33";

        await vm.DuplicateCommand.ExecuteAsync(null);

        await _repository.Received(1).SaveAsync(
            Arg.Is<Instance>(i => i != null && i.InstanceName == "IIDX 33"), Arg.Any<CancellationToken>());
        vm.CreatedName.Should().Be("IIDX 33");
        closed.Should().BeTrue();
    }

    [Fact]
    public async Task A_taken_name_keeps_the_dialog_open_with_the_error()
    {
        _repository.ExistsAsync("IIDX 33", Arg.Any<CancellationToken>()).Returns(true);
        var vm = CreateViewModel();
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        vm.NewName = "IIDX 33";

        await vm.DuplicateCommand.ExecuteAsync(null);

        vm.Error.Should().Be("A library entry with this name already exists.");
        vm.CreatedName.Should().BeNull();
        closed.Should().BeFalse();
    }

    /// <summary>Stands in for the UI thread: work that runs "on the caller" runs with this context current.</summary>
    private sealed class CallerContext : SynchronizationContext;

    [Fact]
    public async Task While_the_copy_is_saved_the_dialog_is_busy_says_what_it_does_and_does_not_block_the_caller()
    {
        var saving = new TaskCompletionSource();
        var ranOnCaller = new List<bool>();
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            ranOnCaller.Add(SynchronizationContext.Current is CallerContext);
            return Instance.Create("IIDX 32", [new InstanceFile("a.bin", "", 1, "AA.bin")], []);
        });
        _repository.SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            ranOnCaller.Add(SynchronizationContext.Current is CallerContext);
            return saving.Task;
        });
        var vm = CreateViewModel();

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new CallerContext());
        Task duplicating;
        try
        {
            duplicating = vm.DuplicateCommand.ExecuteAsync(null);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        // The command returned to the "UI thread" with the save still pending.
        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => ranOnCaller.Count == 2);
        vm.IsBusy.Should().BeTrue();
        vm.StatusLine.Should().NotBeEmpty();
        vm.DuplicateCommand.CanExecute(null).Should().BeFalse();
        vm.CloseCommand.CanExecute(null).Should().BeFalse();

        saving.SetResult();
        await duplicating;

        ranOnCaller.Should().OnlyContain(onCaller => !onCaller);
        vm.IsBusy.Should().BeFalse();
        vm.StatusLine.Should().BeEmpty();
    }

    [Fact]
    public async Task A_storage_failure_keeps_the_dialog_open_with_the_message()
    {
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>())
            .Returns(Instance.Create("IIDX 32", [new InstanceFile("a.bin", "", 1, "AA.bin")], []));
        _repository.SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("database is locked")));
        var vm = CreateViewModel();
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.DuplicateCommand.ExecuteAsync(null);

        vm.Error.Should().Be("database is locked");
        vm.CreatedName.Should().BeNull();
        vm.IsBusy.Should().BeFalse();
        closed.Should().BeFalse();
    }
}

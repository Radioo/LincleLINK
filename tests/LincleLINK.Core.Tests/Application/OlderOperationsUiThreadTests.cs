using System.Text;
using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Games;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// The UI never freezes (CLAUDE.md), checked for the operations that predate the
/// rule: legacy import, export, the status summary and Add's checks. After an
/// <c>await</c> a method started from a view model is back on the UI thread, so a
/// loop or a disk check that follows runs there unless the service moves it.
/// </summary>
public sealed class OlderOperationsUiThreadTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly UiThreadStandIn _ui = new();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();

    public void Dispose() => _temp.Dispose();

    // ── legacy import ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_legacy_import_parses_and_saves_off_the_ui_thread_and_reports_what_it_does()
    {
        var xmlPath = _temp.CreateFile("DBInfo.xml", Encoding.UTF8.GetBytes(TestData.V1DbInfoXml));
        _repository.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _ui.Note("exists");
            return false;
        });
        _repository.SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _ui.Note("save");
            return Task.CompletedTask;
        });
        var statuses = new List<string>();
        var percents = new List<double>();
        var importer = new LegacyImporter(_repository, NullLogger<LegacyImporter>.Instance);

        var result = await _ui.RunAsync(() => importer.ImportAsync(
            xmlPath, new InlineProgress<string>(statuses.Add), new InlineProgress<double>(percents.Add),
            TestContext.Current.CancellationToken));

        result.Imported.Should().NotBeEmpty();
        _ui.RanOnUiThread.Should().BeEmpty();

        // Parsing has no steps to count, so it is named; the entries then count up.
        statuses.First().Should().StartWith("Reading ");
        statuses.Should().Contain(s => s.StartsWith("Importing "));
        percents.Should().NotBeEmpty().And.BeInAscendingOrder();
        percents.Last().Should().Be(100);
    }

    // ── export ────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_export_checks_and_copies_off_the_ui_thread_and_picks_its_folder_on_it()
    {
        var pickedOnUi = false;
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns(_ =>
        {
            pickedOnUi = UiThreadStandIn.IsCurrent;
            return "/export";
        });
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(Instance.Create(
            "inst",
            [
                new InstanceFile("a.bin", "", 1, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin"),
                new InstanceFile("b.bin", "", 1, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin"),
            ],
            []));
        _fs.FileExists(Arg.Any<string>()).Returns(call =>
        {
            _ui.Note("file exists");
            return call.Arg<string>()!.Contains("AAAA", StringComparison.Ordinal);
        });
        _store.CopyFromStoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _ui.Note("copy");
            return Task.CompletedTask;
        });
        var service = new LinkingService(
            _fs, _store, Substitute.For<IHardLinker>(), Substitute.For<IHardLinkPreflight>(), _repository, _dialogs,
            NullLogger<LinkingService>.Instance);

        var result = await _ui.RunAsync(() => service.CopyHashedFilesAsync("inst", ct: TestContext.Current.CancellationToken));

        result.Copied.Should().Be(1);
        result.AlreadyExisted.Should().Be(1);
        pickedOnUi.Should().BeTrue("a picker opens a window, which only the UI thread may do");
        _ui.RanOnUiThread.Should().BeEmpty();
    }

    // ── status summary ────────────────────────────────────────────────────

    [Fact]
    public async Task The_status_summary_asks_the_drive_off_the_ui_thread()
    {
        var paths = Substitute.For<IAppPaths>();
        paths.DataDirectory.Returns("/data");
        _store.GetTotalSizeAsync(Arg.Any<CancellationToken>()).Returns(10L);
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([new InstanceListEntry("A", 1, 30, "30 B")]);
        _driveInfo.GetAvailableFreeSpace("/data").Returns(_ =>
        {
            _ui.Note("free space");
            return 500L;
        });
        var service = new StatusService(_store, _repository, _driveInfo, paths, NullLogger<StatusService>.Instance);

        var summary = await _ui.RunAsync(() => service.GetSummaryAsync(TestContext.Current.CancellationToken));

        summary.FreeSpace.Should().Be(500);
        summary.Savings.Should().Be(20);
        _ui.RanOnUiThread.Should().BeEmpty();
    }

    // ── add ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Adding_a_folder_checks_the_folder_and_the_drive_off_the_ui_thread()
    {
        // A folder on a network share can take seconds to answer "do you exist".
        _fs.DirectoryExists("/games/new").Returns(_ =>
        {
            _ui.Note("directory exists");
            return true;
        });
        _fs.EnumerateFiles("/games/new", true).Returns(_ =>
        {
            _ui.Note("enumerate");
            return ["/games/new/a.bin"];
        });
        _fs.GetFileLength("/games/new/a.bin").Returns(10);
        _driveInfo.GetAvailableFreeSpace("/games/new").Returns(_ =>
        {
            _ui.Note("free space");
            return 1L; // low, so the question below is asked
        });
        var askedOnUi = false;
        _dialogs.ConfirmAsync(Arg.Any<string>(), "Low disk space").Returns(_ =>
        {
            askedOnUi = UiThreadStandIn.IsCurrent;
            return false;
        });
        var service = new InstanceService(
            _fs, Substitute.For<IFileHasher>(), _store, Substitute.For<IHardLinker>(), Substitute.For<IHardLinkPreflight>(),
            _repository, _driveInfo, _dialogs, Substitute.For<IGameVersionDetector>(), NullLogger<InstanceService>.Instance);

        var result = await _ui.RunAsync(() => service.CreateInstanceAsync(
            new AddInstanceRequest("New", "/games/new", CopyMoveMode.Copy), ct: TestContext.Current.CancellationToken));

        result.Success.Should().BeFalse("the user declined the low-disk question");
        askedOnUi.Should().BeTrue("a dialog opens a window, which only the UI thread may do");
        _ui.RanOnUiThread.Should().BeEmpty();
    }

    /// <summary>Reports inline; <see cref="Progress{T}"/> would post to a context and arrive late.</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            lock (this)
            {
                report(value);
            }
        }
    }
}

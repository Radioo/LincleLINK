using System.Text.RegularExpressions;
using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Logos;
using LincleLINK.App.Services;
using LincleLINK.App.Tests.TestHelpers;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Games;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Settings;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using AvaloniaLevel = Avalonia.Logging.LogEventLevel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace LincleLINK.App.Tests;

/// <summary>
/// Diagnostic-logging coverage (issue #17 D4-D6). Everything that touches the
/// process-global Serilog <c>Log</c>, <see cref="FileLoggingSwitch"/> or the static
/// Avalonia sink lives in this one class so xUnit's per-class serialization keeps
/// the global state safe.
/// </summary>
public sealed class DiagnosticLoggingTests
{
    private readonly string _logDir =
        Path.Combine(Path.GetTempPath(), "linclelink-diag-" + Guid.NewGuid().ToString("N"));

    private DiagnosticLogOptions Options => new(_logDir);

    // VM test plumbing (mirrors MainViewModelTests).
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IAppPaths _paths = Substitute.For<IAppPaths>();
    private readonly LogoCatalog _logoCatalog = new();

    private MainViewModel CreateViewModel(RecordingLoggerProvider provider, ISettingsStore settingsStore)
    {
        var dialogs = Substitute.For<IDialogService>();
        return new MainViewModel(
            new InstanceService(_fs, _hasher, _store, Substitute.For<IHardLinker>(), Substitute.For<IHardLinkPreflight>(), _repository, _driveInfo, dialogs, Substitute.For<IGameVersionDetector>(), NullLogger<InstanceService>.Instance),
            new LinkingService(_fs, _store, Substitute.For<IHardLinker>(), Substitute.For<IHardLinkPreflight>(), _repository, dialogs, NullLogger<LinkingService>.Instance),
            new UnusedFilesService(_store, _repository, dialogs, NullLogger<UnusedFilesService>.Instance),
            new LegacyImporter(_repository, NullLogger<LegacyImporter>.Instance),
            new TorrentService(Substitute.For<ITorrentSource>(), _repository, _store, Substitute.For<IHardLinker>(), _fs, NullLogger<TorrentService>.Instance),
            _repository,
            new StatusService(_store, _repository, _driveInfo, _paths, NullLogger<StatusService>.Instance),
            dialogs,
            Substitute.For<IThemeManager>(),
            settingsStore,
            Substitute.For<ITaskbarProgress>(),
            Substitute.For<IHardLinkPreflight>(),
            () => throw new InvalidOperationException("add-instance factory not exercised here"),
            LoggerFactory.Create(builder => builder.AddProvider(provider).SetMinimumLevel(LogLevel.Debug)).CreateLogger<MainViewModel>(),
            Options,
            _logoCatalog,
            _paths);
    }

    private void StubStatus()
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _store.GetTotalSizeAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(1L);
        _paths.DataDirectory.Returns("C:\\data");
    }

    // ── RunOperationAsync structured events (D4) ─────────────────────────────

    [Fact]
    public async Task RunOperationAsync_logs_start_completion_and_scope()
    {
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(provider, Substitute.For<ISettingsStore>());

        await vm.RunOperationAsync("Link files", _ => Task.CompletedTask);

        var start = provider.Logs.Single(l => l.Message.Contains("Starting operation"));
        start.Level.Should().Be(LogLevel.Information);
        start.Properties["Operation"].Should().Be("Link files");
        start.Scope.Should().Contain("Operation Link files");

        provider.Logs.Should().Contain(l =>
            l.Level == LogLevel.Information && l.Message.Contains("completed"));
    }

    [Fact]
    public async Task RunOperationAsync_logs_failure_with_full_exception()
    {
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(provider, Substitute.For<ISettingsStore>());
        var ex = new IOException("boom");

        await vm.RunOperationAsync("Check unused", _ => throw ex);

        var failure = provider.Logs.Single(l => l.Level == LogLevel.Error);
        failure.Exception.Should().BeSameAs(ex);
        failure.Scope.Should().Contain("Check unused");
        vm.LogLines.Should().Contain(l => l.Contains("boom"));
    }

    [Fact]
    public async Task RunOperationAsync_logs_cancellation_as_information()
    {
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(provider, Substitute.For<ISettingsStore>());

        await vm.RunOperationAsync("Cancel me", _ => throw new OperationCanceledException());

        provider.Logs.Should().Contain(l =>
            l.Level == LogLevel.Information && l.Message.Contains("cancelled"));
    }

    // ── AddLogLine / activity mirror (D4/D5) ─────────────────────────────────

    [Fact]
    public void AddLogLine_timestamps_line_and_mirrors_to_diagnostic_log()
    {
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(provider, Substitute.For<ISettingsStore>());

        vm.AddLogLine("hello");

        vm.LogLines.Should().ContainSingle(l => Regex.IsMatch(l, @"^\d{2}:\d{2}:\d{2} hello$"));
        provider.Logs.Should().Contain(l => l.Level == LogLevel.Debug && l.Message == "Activity: hello");
    }

    // ── Settings toggle (D2) ─────────────────────────────────────────────────

    [Fact]
    public void SaveLogToFile_toggle_persists_flips_switch_and_reports()
    {
        FileLoggingSwitch.Enabled = false;
        try
        {
            var settingsStore = Substitute.For<ISettingsStore>();
            settingsStore.Load().Returns(new AppSettings(AppTheme.System, null, 2));
            AppSettings? saved = null;
            settingsStore.When(s => s.Save(Arg.Any<AppSettings>()))
                .Do(ci => saved = ci.Arg<AppSettings>());
            using var provider = new RecordingLoggerProvider();
            var vm = CreateViewModel(provider, settingsStore);

            vm.SaveLogToFile = true;

            saved.Should().NotBeNull();
            saved!.SaveLogToFile.Should().BeTrue();
            FileLoggingSwitch.Enabled.Should().BeTrue();
            vm.LogLines.Should().Contain(l => l.Contains(LogMessages.DiagnosticLogEnabledPrefix));

            vm.SaveLogToFile = false;

            saved.SaveLogToFile.Should().BeFalse();
            FileLoggingSwitch.Enabled.Should().BeFalse();
            vm.LogLines.Should().Contain(l => l.Contains(LogMessages.DiagnosticLogDisabled));
        }
        finally
        {
            FileLoggingSwitch.Enabled = false;
        }
    }

    [Fact]
    public async Task InitializeAsync_seeds_toggle_from_settings_without_user_side_effects()
    {
        FileLoggingSwitch.Enabled = false;
        try
        {
            var settingsStore = Substitute.For<ISettingsStore>();
            settingsStore.Load().Returns(new AppSettings(AppTheme.System, null, 2, SaveLogToFile: true));
            using var provider = new RecordingLoggerProvider();
            var vm = CreateViewModel(provider, settingsStore);
            StubStatus();

            await vm.InitializeAsync();

            vm.SaveLogToFile.Should().BeTrue();
            // Seeding reflects the persisted value but must not re-touch the live
            // switch (Program.Main owns it) or add the user-flip activity line.
            FileLoggingSwitch.Enabled.Should().BeFalse();
            vm.LogLines.Should().NotContain(l => l.Contains(LogMessages.DiagnosticLogEnabledPrefix));
        }
        finally
        {
            FileLoggingSwitch.Enabled = false;
        }
    }

    // ── File logging switch / conditional file sink (D1) ────────────────────

    [Fact]
    public void File_sink_writes_when_enabled_and_is_silent_when_disabled()
    {
        FileLoggingSwitch.Enabled = false;
        try
        {
            FileLoggingSwitch.Enabled = true;
            using (var log = SerilogPipeline.BuildConfiguration(_logDir).CreateLogger())
            {
                log.Information("enabled-marker");
            }

            var file = Directory.GetFiles(_logDir, "linclelink-*.log").Should().ContainSingle().Subject;
            File.ReadAllText(file).Should().Contain("enabled-marker");

            FileLoggingSwitch.Enabled = false;
            var dir2 = Path.Combine(Path.GetTempPath(), "linclelink-diag-" + Guid.NewGuid().ToString("N"));
            using (var silent = SerilogPipeline.BuildConfiguration(dir2).CreateLogger())
            {
                silent.Information("disabled-marker");
            }

            Directory.Exists(dir2).Should().BeFalse();
        }
        finally
        {
            FileLoggingSwitch.Enabled = false;
        }
    }

    [Fact]
    public void File_sink_reacts_to_switch_flip_mid_run_without_pipeline_rebuild()
    {
        FileLoggingSwitch.Enabled = false;
        try
        {
            FileLoggingSwitch.Enabled = true;
            using var log = SerilogPipeline.BuildConfiguration(_logDir).CreateLogger();

            log.Information("first");
            FileLoggingSwitch.Enabled = false;
            log.Information("second");
            FileLoggingSwitch.Enabled = true;
            log.Information("third");
            log.Dispose();

            var content = File.ReadAllText(Directory.GetFiles(_logDir, "linclelink-*.log").Single());
            content.Should().Contain("first").And.Contain("third").And.NotContain("second");
        }
        finally
        {
            FileLoggingSwitch.Enabled = false;
        }
    }

    [Fact]
    public void WriteHeader_logs_version_os_and_runtime()
    {
        var sink = new CollectingSink();
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        try
        {
            SerilogPipeline.WriteHeader();

            var evt = sink.Events.Should().ContainSingle().Subject;
            evt.MessageTemplate.Text.Should().Contain("LincleLINK {Version} starting on {Os}");
            evt.Properties.Should().ContainKey("Version");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // ── Avalonia sink bridge (D4) ────────────────────────────────────────────

    [Fact]
    public void AvaloniaLogSink_forwards_only_warning_and_above()
    {
        var sink = new CollectingSink();
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        try
        {
            AvaloniaLogSink.Install();
            var avaloniaSink = Avalonia.Logging.Logger.Sink!;

            avaloniaSink.IsEnabled(AvaloniaLevel.Debug, "Area").Should().BeFalse();
            avaloniaSink.IsEnabled(AvaloniaLevel.Information, "Area").Should().BeFalse();
            avaloniaSink.IsEnabled(AvaloniaLevel.Warning, "Area").Should().BeTrue();

            avaloniaSink.Log(AvaloniaLevel.Information, "Area", null, "ignored");
            avaloniaSink.Log(AvaloniaLevel.Error, "Area", null, "boom {Code}", new object[] { 42 });

            var evt = sink.Events.Should().ContainSingle().Subject;
            evt.Level.Should().Be(LogEventLevel.Error);
            evt.Properties.Should().ContainKey("Code");
            evt.Properties.Should().ContainKey("AvaloniaArea");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // ── Folder opener (D2) ───────────────────────────────────────────────────

    [Theory]
    [InlineData(true, false, false, "explorer.exe")]
    [InlineData(false, true, false, "xdg-open")]
    [InlineData(false, false, true, "open")]
    [InlineData(false, false, false, "xdg-open")]
    public void FolderOpener_selects_platform_command(bool isWindows, bool isLinux, bool isMacOS, string expected)
    {
        const string path = "/some/logs";

        var info = FolderOpener.CreateStartInfo(path, isWindows, isLinux, isMacOS);

        info.FileName.Should().Be(expected);
        info.UseShellExecute.Should().BeTrue();
        info.ArgumentList.Should().Contain(path);
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}

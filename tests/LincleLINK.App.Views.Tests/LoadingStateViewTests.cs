using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using LincleLINK.App.ViewModels;
using LincleLINK.App.Views;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Domain;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Views.Tests;

/// <summary>
/// What is on screen, through the real bindings, in each state of a view that shows
/// loaded data. Every view begins in its loading state (CLAUDE.md): the empty state
/// is a result and may only follow a load that finished and found nothing. The
/// library page once bound its empty state to "no entries", which is also true
/// before the first load, and flashed "Your library is empty" at startup.
/// </summary>
public sealed class LoadingStateViewTests
{
    private const string EmptyHeadline = "Your library is empty";
    private const string LoadingText = "Loading your library...";

    private static void PumpUntil(Func<bool> done)
    {
        var waited = Stopwatch.StartNew();
        while (!done() && waited.Elapsed < TimeSpan.FromSeconds(20))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }

        done().Should().BeTrue("the view model should get there while the UI thread keeps pumping");
    }

    /// <summary>Every piece of text a user could read in <paramref name="view"/> right now.</summary>
    private static List<string> VisibleTexts(Control view, Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => t.Text!)
            .ToList();
    }

    private static bool AddButtonShows(Control view)
        => view.GetVisualDescendants().OfType<Button>()
            .Any(b => b.IsEffectivelyVisible && b.Content is string text && text.Contains("Add your first folder"));

    [Fact]
    public void The_library_page_begins_loading_and_shows_empty_only_after_a_load_found_nothing()
    {
        var reading = new TaskCompletionSource<IReadOnlyList<InstanceListEntry>>();
        var repository = Substitute.For<IInstanceRepository>();
        repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns(reading.Task);

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var vm = InteractionTests.BuildMainViewModel(repository);
            var page = new LibraryPage { DataContext = vm };
            var window = new Window { Content = page, Width = 900, Height = 600 };
            window.Show();
            try
            {
                // Before anything was asked of the database: the state a user sees
                // for the first frames after the window comes up.
                var atStart = VisibleTexts(page, window);
                atStart.Should().Contain(LoadingText);
                atStart.Should().NotContain(EmptyHeadline);
                atStart.Should().Contain("Loading...").And.NotContain("0 entries");
                AddButtonShows(page).Should().BeFalse();

                // The query is under way.
                var initializing = vm.InitializeAsync();
                var whileLoading = VisibleTexts(page, window);
                whileLoading.Should().Contain(LoadingText);
                whileLoading.Should().NotContain(EmptyHeadline);
                AddButtonShows(page).Should().BeFalse();

                // It finished and found nothing: now, and only now, the empty state.
                reading.SetResult([]);
                PumpUntil(() => initializing.IsCompleted);
                var afterLoad = VisibleTexts(page, window);
                afterLoad.Should().Contain(EmptyHeadline);
                afterLoad.Should().NotContain(LoadingText);
                afterLoad.Should().Contain("0 entries");
                AddButtonShows(page).Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_library_with_entries_goes_from_loading_to_the_list_without_an_empty_state()
    {
        var repository = Substitute.For<IInstanceRepository>();
        repository.GetSummariesAsync(Arg.Any<CancellationToken>())
            .Returns([new InstanceListEntry("IIDX 32", 1, 10, "10 B")]);

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var vm = InteractionTests.BuildMainViewModel(repository);
            var page = new LibraryPage { DataContext = vm };
            var window = new Window { Content = page, Width = 900, Height = 600 };
            window.Show();
            try
            {
                var initializing = vm.InitializeAsync();
                PumpUntil(() => initializing.IsCompleted);

                var texts = VisibleTexts(page, window);
                texts.Should().Contain("1 entry");
                texts.Should().NotContain(EmptyHeadline).And.NotContain(LoadingText);

                vm.FilterText = "sdvx";
                VisibleTexts(page, window).Should().Contain("No entries match the filter.")
                    .And.NotContain(EmptyHeadline);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_failed_first_load_shows_the_reason_and_a_way_to_retry()
    {
        var repository = Substitute.For<IInstanceRepository>();
        repository.GetSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<InstanceListEntry>>(new IOException("database is locked")));

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var vm = InteractionTests.BuildMainViewModel(repository);
            var page = new LibraryPage { DataContext = vm };
            var window = new Window { Content = page, Width = 900, Height = 600 };
            window.Show();
            try
            {
                var initializing = vm.InitializeAsync();
                PumpUntil(() => initializing.IsCompleted);

                var texts = VisibleTexts(page, window);
                texts.Should().Contain("The library could not be loaded").And.Contain("database is locked");
                texts.Should().NotContain(EmptyHeadline).And.NotContain(LoadingText);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_sidebar_and_the_torrent_picker_begin_loading_too()
    {
        var reading = new TaskCompletionSource<IReadOnlyList<InstanceListEntry>>();
        var repository = Substitute.For<IInstanceRepository>();
        repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns(reading.Task);

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var vm = InteractionTests.BuildMainViewModel(repository);
            var sidebar = new Sidebar { DataContext = vm };
            var torrent = new TorrentPage { DataContext = vm };
            var window = new Window
            {
                Content = new StackPanel { Children = { sidebar, torrent } },
                Width = 1000,
                Height = 900,
            };
            window.Show();
            try
            {
                var sidebarTexts = VisibleTexts(sidebar, window);
                sidebarTexts.Should().Contain("Measuring storage...");
                sidebarTexts.Should().NotContain(t => t.StartsWith("Saving"), "a card that starts at 'Saving' reads as a result of zero");

                var picker = torrent.GetVisualDescendants().OfType<ComboBox>().First();
                picker.PlaceholderText.Should().Be("Loading entries...");
                picker.IsEffectivelyEnabled.Should().BeFalse();
            }
            finally
            {
                reading.SetResult([]);
                window.Close();
            }
        });
    }

    [Fact]
    public void The_main_window_begins_with_a_boot_state_until_its_view_model_exists()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var window = new MainWindow();
            window.Show();
            try
            {
                var booting = VisibleTexts(window, window);
                booting.Should().Contain("Starting...");
                booting.Should().NotContain(EmptyHeadline).And.NotContain("Library");

                window.DataContext = InteractionTests.BuildMainViewModel();

                var running = VisibleTexts(window, window);
                running.Should().NotContain("Starting...");
                running.Should().Contain("Library");
            }
            finally
            {
                window.Close();
            }
        });
    }
}

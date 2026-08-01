using FluentAssertions;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Dialogs;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

public sealed class FirstRunViewModelTests
{
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private FirstRunViewModel Create(string defaultDirectory = @"C:\data", bool hasLegacyV2Data = false)
        => new(_dialogs, defaultDirectory, hasLegacyV2Data);

    [Fact]
    public void Constructor_sets_directory_and_status()
    {
        var vm = Create(hasLegacyV2Data: true);

        vm.DataDirectory.Should().Be(@"C:\data");
        vm.Status.Should().Contain("v2 data detected");
    }

    [Fact]
    public async Task Browse_picks_folder_and_sets_directory()
    {
        _dialogs.PickFolderAsync("Select data directory").Returns(@"C:\chosen");
        var vm = Create();

        await vm.BrowseCommand.ExecuteAsync(null);

        vm.DataDirectory.Should().Be(@"C:\chosen");
    }

    [Fact]
    public async Task Browse_cancelled_keeps_directory()
    {
        _dialogs.PickFolderAsync("Select data directory").Returns((string?)null);
        var vm = Create(@"C:\data");

        await vm.BrowseCommand.ExecuteAsync(null);

        vm.DataDirectory.Should().Be(@"C:\data");
    }

    [Fact]
    public void Confirm_raises_confirmed_with_directory_and_requests_close()
    {
        var vm = Create();
        vm.DataDirectory = @"C:\data";
        string? chosen = null;
        vm.Confirmed += (_, dir) => chosen = dir;
        var closeRequested = false;
        vm.CloseRequested += (_, _) => closeRequested = true;

        vm.ConfirmCommand.Execute(null);

        chosen.Should().Be(@"C:\data");
        closeRequested.Should().BeTrue();
    }

    [Fact]
    public void Confirm_with_blank_directory_shows_status_and_does_not_raise()
    {
        var vm = Create();
        vm.DataDirectory = "   ";
        var raised = false;
        var closeRequested = false;
        vm.Confirmed += (_, _) => raised = true;
        vm.CloseRequested += (_, _) => closeRequested = true;

        vm.ConfirmCommand.Execute(null);

        raised.Should().BeFalse();
        closeRequested.Should().BeFalse();
        vm.Status.Should().NotBeEmpty();
    }
}

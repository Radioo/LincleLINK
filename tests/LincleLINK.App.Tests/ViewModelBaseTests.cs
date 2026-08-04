using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.ViewModels;
using LincleLINK.App.ViewModels.Base;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Settings;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

/// <summary>
/// <see cref="ViewModelBase"/> surface: dialog sizing, theme radio bookkeeping and
/// the <see cref="OnThemeChanged"/> hook.
/// </summary>
public sealed class ViewModelBaseTests
{
    private sealed class TestVm : ViewModelBase
    {
        public AppTheme? LastThemeApplied;

        protected override void OnThemeChanged(AppTheme theme) => LastThemeApplied = theme;

        public void RaiseClose() => RequestClose();
    }

    [Fact]
    public void Defaults_provide_title_and_dialog_sizes()
    {
        var vm = new TestVm();

        vm.Title.Should().Be("LincleLINK");
        vm.DialogSize.Width.Should().Be(520);
        vm.DialogMinSize.Width.Should().Be(400);
    }

    [Fact]
    public void SetTheme_checks_the_matching_radio()
    {
        var vm = new TestVm();

        vm.SetTheme(AppTheme.Dark);
        vm.IsDarkTheme.Should().BeTrue();
        vm.Theme.Should().Be(AppTheme.Dark);
        vm.LastThemeApplied.Should().Be(AppTheme.Dark);

        vm.SetTheme(AppTheme.System);
        vm.IsSystemTheme.Should().BeTrue();
        vm.IsDarkTheme.Should().BeFalse();
        vm.Theme.Should().Be(AppTheme.System);

        vm.SetTheme(AppTheme.Light);
        vm.IsLightTheme.Should().BeTrue();
        vm.Theme.Should().Be(AppTheme.Light);
    }

    [Fact]
    public void Deselecting_a_radio_does_not_reapply_theme()
    {
        var vm = new TestVm();
        vm.SetTheme(AppTheme.Dark);

        vm.IsDarkTheme = false;

        vm.LastThemeApplied.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public void RequestClose_raises_the_close_event()
    {
        var vm = new TestVm();
        var raised = false;
        vm.CloseRequested += (_, _) => raised = true;

        vm.RaiseClose();

        raised.Should().BeTrue();
    }
}

public sealed class DialogViewModelSurfaceTests
{
    [Fact]
    public void FirstRunViewModel_exposes_title_and_sizes()
    {
        var vm = new FirstRunViewModel(
            Substitute.For<IDialogService>(), Substitute.For<IThemeManager>(), @"C:\data", false, AppTheme.Light);

        vm.Title.Should().Be("First launch");
        vm.DialogSize.Width.Should().Be(720);
        vm.DialogMinSize.Width.Should().Be(640);
    }

    [Fact]
    public void StorageMigrationViewModel_exposes_title_and_size()
    {
        var vm = new StorageMigrationViewModel(Substitute.For<LincleLINK.Core.Application.StorageMigrationService>(
            Substitute.For<LincleLINK.Core.Abstractions.Paths.IAppPaths>(),
            Substitute.For<LincleLINK.Core.Abstractions.Instances.IInstanceRepository>(),
            Substitute.For<Microsoft.EntityFrameworkCore.IDbContextFactory<LincleLINK.Core.Infrastructure.Persistence.LincleLinkDbContext>>()));

        vm.Title.Should().Be("Upgrading database");
        vm.DialogSize.Width.Should().Be(560);
    }
}

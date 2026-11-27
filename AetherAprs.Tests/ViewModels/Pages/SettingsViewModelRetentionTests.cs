// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

/// <summary>
/// Packet retention is a stored setting that the operator can now change; these
/// tests cover the round trip and the validation that guards it.
/// </summary>
public sealed class SettingsViewModelRetentionTests
{
    [Fact]
    public void RetentionDefaultsToTheThirtyDayTarget()
    {
        using SettingsViewModel viewModel = CreateViewModel(new TestConfigurationService());

        Assert.Equal(AprsSettings.DefaultPacketRetentionDays, viewModel.PacketRetentionDays);
        Assert.Equal(30, viewModel.PacketRetentionDays);
        Assert.Equal(AprsSettings.DefaultPacketRetentionMaxRows, viewModel.PacketRetentionMaxRows);
    }

    [Fact]
    public async Task ChangingRetentionDaysPersistsTheWindow()
    {
        TestConfigurationService configuration = new();
        using SettingsViewModel viewModel = CreateViewModel(configuration);

        viewModel.PacketRetentionDays = 7;
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Empty(viewModel.GetErrors(nameof(SettingsViewModel.PacketRetentionDays)));
        Assert.Equal(7, configuration.Current.Aprs.PacketRetentionDays);
    }

    [Fact]
    public async Task ChangingRetentionRowsPersistsTheBound()
    {
        TestConfigurationService configuration = new();
        using SettingsViewModel viewModel = CreateViewModel(configuration);

        viewModel.PacketRetentionMaxRows = 25_000;
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Empty(viewModel.GetErrors(nameof(SettingsViewModel.PacketRetentionMaxRows)));
        Assert.Equal(25_000, configuration.Current.Aprs.PacketRetentionMaxRows);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SettingsValidation.MaxPacketRetentionDays + 1)]
    public async Task OutOfRangeRetentionDaysIsRejectedAndLeavesTheSavedValue(int days)
    {
        TestConfigurationService configuration = new();
        using SettingsViewModel viewModel = CreateViewModel(configuration);

        int saved = configuration.Current.Aprs.PacketRetentionDays;
        int updatesBefore = configuration.UpdateCount;

        viewModel.PacketRetentionDays = days;
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.NotEmpty(viewModel.GetErrors(nameof(SettingsViewModel.PacketRetentionDays)));
        Assert.Equal(updatesBefore, configuration.UpdateCount);
        Assert.Equal(saved, configuration.Current.Aprs.PacketRetentionDays);
    }

    [Theory]
    [InlineData(SettingsValidation.MinPacketRetentionMaxRows - 1)]
    [InlineData(SettingsValidation.MaxPacketRetentionMaxRows + 1)]
    public async Task OutOfRangeRetentionRowsIsRejectedAndLeavesTheSavedValue(int rows)
    {
        TestConfigurationService configuration = new();
        using SettingsViewModel viewModel = CreateViewModel(configuration);

        int saved = configuration.Current.Aprs.PacketRetentionMaxRows;
        int updatesBefore = configuration.UpdateCount;

        viewModel.PacketRetentionMaxRows = rows;
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.NotEmpty(viewModel.GetErrors(nameof(SettingsViewModel.PacketRetentionMaxRows)));
        Assert.Equal(updatesBefore, configuration.UpdateCount);
        Assert.Equal(saved, configuration.Current.Aprs.PacketRetentionMaxRows);
    }

    [Fact]
    public void SavedRetentionIsLoadedIntoThePage()
    {
        TestConfigurationService configuration = new()
        {
            Current = new AppSettings
            {
                Aprs = new AprsSettings { PacketRetentionDays = 45, PacketRetentionMaxRows = 12_000 },
            },
        };

        using SettingsViewModel viewModel = CreateViewModel(configuration);

        Assert.Equal(45, viewModel.PacketRetentionDays);
        Assert.Equal(12_000, viewModel.PacketRetentionMaxRows);
    }

    [Fact]
    public void TheSnapshotRejectsAnOutOfRangeRetentionWindow()
    {
        // The snapshot is a second entry point: a caller that builds one directly
        // must not be able to store a window the cleanup path cannot honour.
        Assert.Throws<ArgumentOutOfRangeException>(() => new AprsSettings { PacketRetentionDays = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AprsSettings { PacketRetentionMaxRows = 0 });
    }

    private static SettingsViewModel CreateViewModel(IConfigurationService configuration)
    {
        TestNavigationService navigation = new();
        return new SettingsViewModel(
            configuration,
            navigation,
            new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider()),
            new MessageTemplatesViewModel(
                Substitute.For<IMessageTemplateService>(),
                Substitute.For<IConfirmationDialogService>(),
                navigation,
                Substitute.For<IMessageTemplateEditViewModelFactory>()));
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(Symbol symbol) => new(64, 64);
        public SKBitmap GetOverlayBitmap(SymbolCode overlayChar) => new(64, 64);
        public void Dispose() { }
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
#pragma warning disable CS0067 // Event is never used in this test double
        public event EventHandler? SettingsChanged;
#pragma warning restore CS0067
        private int _updateCount;
        public AppSettings Current { get; set; } = new();
        public int UpdateCount => _updateCount;

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _updateCount);
            Current = update(Current);
            return Task.FromResult(Current);
        }
    }

    private sealed class TestNavigationService : INavigationService
    {
        public ViewModelBase? CurrentViewModel => null;
        public event EventHandler<ViewModelBase?>? CurrentViewModelChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? RequestAppExit
        {
            add { }
            remove { }
        }

        public bool CanGoBack => false;
        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase { }
        public void NavigateTo(ViewModelBase viewModel) { }
        public void GoBack() { }
    }
}

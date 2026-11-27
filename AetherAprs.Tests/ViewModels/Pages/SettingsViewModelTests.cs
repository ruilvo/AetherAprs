// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Objects;
using AetherAprs.Services.Packets;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task ChangingCallsignAutoSavesSettings()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        AprsSymbolPickerViewModel symbolPicker = new(new TestSymbolBitmapProvider());
        SettingsViewModel viewModel = new(
            configuration, navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation))
        {
            Callsign = "CT7ALW"
        };

        // Wait for async save to complete deterministically
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal("CT7ALW", configuration.Current.Aprs.Callsign);
        Assert.True(configuration.UpdateCount > 0);
        Assert.Equal(1, configuration.UpdateCount);
    }

    [Fact]
    public async Task ChangingMultiplePropertiesAutoSavesEachTime()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        AprsSymbolPickerViewModel symbolPicker = new(new TestSymbolBitmapProvider());
        SettingsViewModel viewModel = new(
            configuration, navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation))
        {
            Callsign = "CT7ALW"
        };
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        viewModel.Ssid = AprsSsid.HumanPortable;
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        viewModel.SymbolTableCharacter = "\\";
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        viewModel.SymbolCodeCharacter = ">";
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal("CT7ALW", configuration.Current.Aprs.Callsign);
        Assert.Equal(
            AetherAprs.Models.Aprs.AprsSsid.HumanPortable,
            configuration.Current.Aprs.Ssid);
        Assert.Equal(
            AetherAprs.Models.Aprs.SymbolTable.Alternate,
            configuration.Current.Aprs.SymbolTable);
        Assert.Equal(SymbolCode.GreaterThanSign, configuration.Current.Aprs.SymbolCode);
        Assert.Equal(4, configuration.UpdateCount);
    }

    [Fact]
    public async Task ChangingTileSourceAutoSavesMapSettings()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        AprsSymbolPickerViewModel symbolPicker = new(new TestSymbolBitmapProvider());
        SettingsViewModel viewModel = new(
            configuration, navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation))
        {
            TileSource = TileSourceType.MbTilesOffline
        };
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        viewModel.MbTilesFilePath = "/data/map.mbtiles";
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(TileSourceType.MbTilesOffline, configuration.Current.Map.TileSource);
        Assert.Equal("/data/map.mbtiles", configuration.Current.Map.MbTilesFilePath);
        Assert.True(configuration.UpdateCount > 0);
    }

    [Fact]
    public async Task EditingCallsignPreservesOfflineMapSelectedAfterPageOpened()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        using SettingsViewModel viewModel = new(
            configuration, navigation, new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider()),
            CreateMessageTemplatesViewModel(navigation));

        await configuration.UpdateAsync(settings => settings with
        {
            Map = settings.Map with
            {
                TileSource = TileSourceType.MbTilesOffline,
                MbTilesFilePath = "/data/selected.mbtiles",
            },
        }, TestContext.Current.CancellationToken);

        viewModel.Callsign = "CT7ALW";
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal("CT7ALW", configuration.Current.Aprs.Callsign);
        Assert.Equal(TileSourceType.MbTilesOffline, configuration.Current.Map.TileSource);
        Assert.Equal("/data/selected.mbtiles", configuration.Current.Map.MbTilesFilePath);
    }

    [Fact]
    public async Task ChangingPositionFormatAutoSavesSettings()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        AprsSymbolPickerViewModel symbolPicker = new(new TestSymbolBitmapProvider());
        SettingsViewModel viewModel = new(
            configuration, navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation));

        // Defaults to the configured value
        Assert.Equal(PositionFormat.Compressed, viewModel.PositionFormat);

        viewModel.PositionFormat = PositionFormat.MicE;
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(PositionFormat.MicE, configuration.Current.Aprs.PositionFormat);
    }

    [Fact]
    public void AvailablePositionFormatsExposesAllEncodings()
    {
        TestNavigationService navigation = new();
        AprsSymbolPickerViewModel symbolPicker = new(new TestSymbolBitmapProvider());
        SettingsViewModel viewModel = new(
            new TestConfigurationService(), navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation));

        Assert.Equal(
            [PositionFormat.Uncompressed, PositionFormat.Compressed, PositionFormat.MicE],
            viewModel.AvailablePositionFormats);
    }

    [Fact]
    public void OpenBeaconingSettingsNavigatesToDynamicBeaconingViewModel()
    {
        TestNavigationService navigation = new();
        AprsSymbolPickerViewModel symbolPicker = new(new TestSymbolBitmapProvider());
        SettingsViewModel viewModel = new(
            new TestConfigurationService(), navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation));

        viewModel.OpenBeaconingSettingsCommand.Execute(null);

        Assert.Equal(typeof(DynamicBeaconingViewModel), navigation.LastNavigatedType);
    }

    [Fact]
    public void OpenMessageTemplatesNavigatesToMessageTemplatesViewModel()
    {
        TestNavigationService navigation = new();
        AprsSymbolPickerViewModel symbolPicker = new(new TestSymbolBitmapProvider());
        MessageTemplatesViewModel messageTemplates = CreateMessageTemplatesViewModel(navigation);
        SettingsViewModel viewModel = new(
            new TestConfigurationService(), navigation, symbolPicker, messageTemplates);

        viewModel.OpenMessageTemplatesCommand.Execute(null);

        Assert.Equal(typeof(MessageTemplatesViewModel), navigation.LastNavigatedType);
    }

    [Fact]
    public async Task ChangingTheme_AppliesThemeAndPersistsTheChoice()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        RecordingThemeService themeService = new();
        using SettingsViewModel viewModel = new(
            configuration, navigation, new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider()),
            CreateMessageTemplatesViewModel(navigation), themeService);

        viewModel.Theme = ThemeMode.Dark;
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(ThemeMode.Dark, Assert.Single(themeService.Applied));
        Assert.Equal(ThemeMode.Dark, configuration.Current.Theme);
    }

    [Fact]
    public async Task ValidDigipeaterPathIsSaved()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        AprsSymbolPickerViewModel symbolPicker = new(new TestSymbolBitmapProvider());
        using SettingsViewModel viewModel = new(
            configuration, navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation));

        viewModel.DigipeaterPath = "WIDE1-1,WIDE2-1,WIDE2-2";
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Empty(viewModel.GetErrors(nameof(SettingsViewModel.DigipeaterPath)));
        Assert.Equal("WIDE1-1,WIDE2-1,WIDE2-2", configuration.Current.Aprs.DigipeaterPath);
    }

    [Fact]
    public async Task InvalidDigipeaterPathIsRejectedAndLeavesTheSavedPath()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        AprsSymbolPickerViewModel symbolPicker = new(new TestSymbolBitmapProvider());
        using SettingsViewModel viewModel = new(
            configuration, navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation));

        string? saved = configuration.Current.Aprs.DigipeaterPath;
        int updatesBefore = configuration.UpdateCount;

        // The AX.25 field cannot carry this hop, so the beacon would throw when it
        // turned the path into a digipeater list. The invalid edit must not reach the
        // saved settings while it is on screen.
        viewModel.DigipeaterPath = "WIDE1-1,NOT A HOP";
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.NotEmpty(viewModel.GetErrors(nameof(SettingsViewModel.DigipeaterPath)));
        Assert.Equal(updatesBefore, configuration.UpdateCount);
        Assert.Equal(saved, configuration.Current.Aprs.DigipeaterPath);
    }

    [Fact]
    public async Task DigipeaterPathAcceptsRepeatedBitButRejectsTooManyHops()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        AprsSymbolPickerViewModel symbolPicker = new(new TestSymbolBitmapProvider());
        using SettingsViewModel viewModel = new(
            configuration, navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation));

        viewModel.DigipeaterPath = "WIDE1-1*,WIDE2-1";
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Empty(viewModel.GetErrors(nameof(SettingsViewModel.DigipeaterPath)));
        Assert.Equal("WIDE1-1*,WIDE2-1", configuration.Current.Aprs.DigipeaterPath);

        viewModel.DigipeaterPath = "WIDE1-1,WIDE2-1,WIDE2-1,WIDE2-1,WIDE2-1,WIDE2-1,WIDE2-1,WIDE2-1,WIDE2-1";
        await viewModel.CurrentSaveTask.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.NotEmpty(viewModel.GetErrors(nameof(SettingsViewModel.DigipeaterPath)));
        Assert.Equal("WIDE1-1*,WIDE2-1", configuration.Current.Aprs.DigipeaterPath);
    }

    private static MessageTemplatesViewModel CreateMessageTemplatesViewModel(INavigationService navigation) => new(
            Substitute.For<IMessageTemplateService>(),
            Substitute.For<IConfirmationDialogService>(),
            navigation,
            Substitute.For<IMessageTemplateEditViewModelFactory>());

    private sealed class RecordingThemeService : IThemeService
    {
        public List<ThemeMode> Applied { get; } = [];

        public void Apply(ThemeMode mode) => Applied.Add(mode);
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(AetherAprs.Models.Aprs.Symbol symbol) => new(64, 64);
        public SKBitmap GetOverlayBitmap(AetherAprs.Models.Aprs.SymbolCode overlayChar) => new(64, 64);
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
            // Auto-saves now run on the thread pool; count them atomically.
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
        public Type? LastNavigatedType { get; private set; }
        public ViewModelBase? LastNavigatedViewModel { get; private set; }
        public bool CanGoBack => false;
        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase => LastNavigatedType = typeof(TViewModel);
        public void NavigateTo(ViewModelBase viewModel)
        {
            LastNavigatedType = viewModel.GetType();
            LastNavigatedViewModel = viewModel;
        }
        public void GoBack() { }
    }
}

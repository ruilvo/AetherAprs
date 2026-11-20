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
        var configuration = new TestConfigurationService();
        var navigation = new TestNavigationService();
        var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());
        var viewModel = new SettingsViewModel(
            configuration, navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation),
            CreateObjectsViewModel(navigation), CreateBulletinsViewModel(navigation),
            CreateSearchViewModel(navigation));

        viewModel.Callsign = "CT7ALW";
        
        // Wait for async save to complete deterministically
        await viewModel.CurrentSaveTask.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal("CT7ALW", configuration.Settings.Aprs.Callsign);
        Assert.True(configuration.SaveCount > 0);
    }

    [Fact]
    public async Task ChangingMultiplePropertiesAutoSavesEachTime()
    {
        var configuration = new TestConfigurationService();
        var navigation = new TestNavigationService();
        var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());
        var viewModel = new SettingsViewModel(
            configuration, navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation),
            CreateObjectsViewModel(navigation), CreateBulletinsViewModel(navigation),
            CreateSearchViewModel(navigation));

        viewModel.Callsign = "CT7ALW";
        await viewModel.CurrentSaveTask.WaitAsync(TestContext.Current.CancellationToken);
        
        viewModel.Ssid = AprsSsid.HumanPortable;
        await viewModel.CurrentSaveTask.WaitAsync(TestContext.Current.CancellationToken);
        
        viewModel.SymbolTableCharacter = "\\";
        await viewModel.CurrentSaveTask.WaitAsync(TestContext.Current.CancellationToken);
        
        viewModel.SymbolCodeCharacter = ">";
        await viewModel.CurrentSaveTask.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal("CT7ALW", configuration.Settings.Aprs.Callsign);
        Assert.Equal(
            AetherAprs.Models.Aprs.AprsSsid.HumanPortable,
            configuration.Settings.Aprs.Ssid);
        Assert.Equal(
            AetherAprs.Models.Aprs.SymbolTable.Alternate,
            configuration.Settings.Aprs.SymbolTable);
        Assert.Equal(SymbolCode.GreaterThanSign, configuration.Settings.Aprs.SymbolCode);
        Assert.True(configuration.SaveCount >= 4);
    }

    [Fact]
    public async Task ChangingTileSourceAutoSavesMapSettings()
    {
        var configuration = new TestConfigurationService();
        var navigation = new TestNavigationService();
        var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());
        var viewModel = new SettingsViewModel(
            configuration, navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation),
            CreateObjectsViewModel(navigation), CreateBulletinsViewModel(navigation),
            CreateSearchViewModel(navigation));

        viewModel.TileSource = TileSourceType.MbTilesOffline;
        await viewModel.CurrentSaveTask.WaitAsync(TestContext.Current.CancellationToken);

        viewModel.MbTilesFilePath = "/data/map.mbtiles";
        await viewModel.CurrentSaveTask.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TileSourceType.MbTilesOffline, configuration.Settings.Map.TileSource);
        Assert.Equal("/data/map.mbtiles", configuration.Settings.Map.MbTilesFilePath);
        Assert.True(configuration.SaveCount > 0);
    }

    [Fact]
    public void OpenBeaconingSettingsNavigatesToDynamicBeaconingViewModel()
    {
        var navigation = new TestNavigationService();
        var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());
        var viewModel = new SettingsViewModel(
            new TestConfigurationService(), navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation),
            CreateObjectsViewModel(navigation), CreateBulletinsViewModel(navigation),
            CreateSearchViewModel(navigation));

        viewModel.OpenBeaconingSettingsCommand.Execute(null);

        Assert.Equal(typeof(DynamicBeaconingViewModel), navigation.LastNavigatedType);
    }

    [Fact]
    public void OpenMessageTemplatesNavigatesToMessageTemplatesViewModel()
    {
        var navigation = new TestNavigationService();
        var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());
        var messageTemplates = CreateMessageTemplatesViewModel(navigation);
        var viewModel = new SettingsViewModel(
            new TestConfigurationService(), navigation, symbolPicker, messageTemplates,
            CreateObjectsViewModel(navigation), CreateBulletinsViewModel(navigation),
            CreateSearchViewModel(navigation));

        viewModel.OpenMessageTemplatesCommand.Execute(null);

        Assert.Equal(typeof(MessageTemplatesViewModel), navigation.LastNavigatedType);
    }

    [Fact]
    public void OpenObjectsNavigatesToObjectsViewModel()
    {
        var navigation = new TestNavigationService();
        var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());
        var objects = CreateObjectsViewModel(navigation);
        var viewModel = new SettingsViewModel(
            new TestConfigurationService(), navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation),
            objects, CreateBulletinsViewModel(navigation), CreateSearchViewModel(navigation));

        viewModel.OpenObjectsCommand.Execute(null);

        Assert.Same(objects, navigation.LastNavigatedViewModel);
    }

    [Fact]
    public void OpenBulletinsNavigatesToBulletinsViewModel()
    {
        var navigation = new TestNavigationService();
        var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());
        var bulletins = CreateBulletinsViewModel(navigation);
        var viewModel = new SettingsViewModel(
            new TestConfigurationService(), navigation, symbolPicker, CreateMessageTemplatesViewModel(navigation),
            CreateObjectsViewModel(navigation), bulletins, CreateSearchViewModel(navigation));

        viewModel.OpenBulletinsCommand.Execute(null);

        Assert.Same(bulletins, navigation.LastNavigatedViewModel);
    }

    private static MessageTemplatesViewModel CreateMessageTemplatesViewModel(INavigationService navigation)
    {
        return new MessageTemplatesViewModel(
            Substitute.For<IMessageTemplateService>(),
            Substitute.For<IConfirmationDialogService>(),
            navigation,
            Substitute.For<IMessageTemplateEditViewModelFactory>());
    }

    private static ObjectsViewModel CreateObjectsViewModel(INavigationService navigation)
    {
        return new ObjectsViewModel(
            Substitute.For<IObjectService>(),
            navigation,
            Substitute.For<IObjectEditorViewModelFactory>(),
            Substitute.For<IConfirmationDialogService>());
    }

    private static BulletinsViewModel CreateBulletinsViewModel(INavigationService navigation)
    {
        return new BulletinsViewModel(
            Substitute.For<IBulletinService>(),
            navigation);
    }

    private static SearchViewModel CreateSearchViewModel(INavigationService navigation)
    {
        return new SearchViewModel(
            Substitute.For<IPacketQueryService>(),
            navigation,
            Substitute.For<IPacketDetailsViewModelFactory>());
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(AetherAprs.Models.Aprs.Symbol symbol) => new(64, 64);
        public SKBitmap GetOverlayBitmap(AetherAprs.Models.Aprs.SymbolCode overlayChar) => new(64, 64);
        public void Dispose() { }
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        private int _saveCount;
        public AppSettings Settings { get; } = new();
        public int SaveCount => _saveCount;
        public Task SaveSettingsAsync()
        {
            // Auto-saves now run on the thread pool; count them atomically.
            Interlocked.Increment(ref _saveCount);
            return Task.CompletedTask;
        }

        public Task UpdateSettingsAsync(Action<AppSettings> updateAction)
        {
            updateAction(Settings);
            return Task.CompletedTask;
        }

        public AppSettings GetSettingsSnapshot() => Settings;
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

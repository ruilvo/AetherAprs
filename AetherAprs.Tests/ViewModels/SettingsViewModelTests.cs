// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Imaging;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task ChangingCallsignAutoSavesSettings()
    {
        var configuration = new TestConfigurationService();
        var navigation = new TestNavigationService();
        var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());
        var viewModel = new SettingsViewModel(configuration, navigation, symbolPicker);

        viewModel.Callsign = "CT7ALW";
        
        // Give async save a moment to complete
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal("CT7ALW", configuration.Settings.Aprs.Callsign);
        Assert.True(configuration.SaveCount > 0);
    }

    [Fact]
    public async Task ChangingMultiplePropertiesAutoSavesEachTime()
    {
        var configuration = new TestConfigurationService();
        var navigation = new TestNavigationService();
        var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());
        var viewModel = new SettingsViewModel(configuration, navigation, symbolPicker);

        viewModel.Callsign = "CT7ALW";
        viewModel.DefaultSsid = 7;
        viewModel.DefaultSymbolTableCharacter = "\\";
        viewModel.DefaultSymbolCodeCharacter = ">";
        
        // Give async saves a moment to complete
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal("CT7ALW", configuration.Settings.Aprs.Callsign);
        Assert.Equal(7, configuration.Settings.Aprs.DefaultSsid);
        Assert.Equal("\\", configuration.Settings.Aprs.DefaultSymbolTableCharacter);
        Assert.Equal(">", configuration.Settings.Aprs.DefaultSymbolCodeCharacter);
        Assert.True(configuration.SaveCount >= 4);
    }

    [Fact]
    public async Task ChangingDefaultSsidToNullSavesZero()
    {
        var configuration = new TestConfigurationService();
        configuration.Settings.Aprs.DefaultSsid = 7;
        var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());
        var viewModel = new SettingsViewModel(configuration, new TestNavigationService(), symbolPicker);

        viewModel.DefaultSsid = null;
        
        // Give async save a moment to complete
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(0, configuration.Settings.Aprs.DefaultSsid);
        Assert.True(configuration.SaveCount > 0);
    }

    [Fact]
    public void OpenBeaconingSettingsNavigatesToDynamicBeaconingViewModel()
    {
        var navigation = new TestNavigationService();
        var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());
        var viewModel = new SettingsViewModel(new TestConfigurationService(), navigation, symbolPicker);

        viewModel.OpenBeaconingSettingsCommand.Execute(null);

        Assert.Equal(typeof(DynamicBeaconingViewModel), navigation.LastNavigatedType);
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(AetherAprs.Models.Aprs.Symbol symbol) => new(64, 64);
        public SKBitmap GetOverlayBitmap(AetherAprs.Models.Aprs.SymbolCode overlayChar) => new(64, 64);
        public void Dispose() { }
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public AppSettings Settings { get; } = new();
        public int SaveCount { get; private set; }
        public Task SaveSettingsAsync()
        {
            SaveCount++;
            return Task.CompletedTask;
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
        public bool CanGoBack => false;
        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase => LastNavigatedType = typeof(TViewModel);
        public void NavigateTo(ViewModelBase viewModel) => LastNavigatedType = viewModel.GetType();
        public void GoBack() { }
    }
}

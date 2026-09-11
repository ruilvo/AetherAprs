// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Pages;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task SaveCommandPersistsEditedAprsSettings()
    {
        var configuration = new TestConfigurationService();
        var navigation = new TestNavigationService();
        var viewModel = new SettingsViewModel(configuration, navigation)
        {
            Callsign = "CT7ALW",
            DefaultSsid = 7,
            DefaultSymbolTableCharacter = "\\",
            DefaultSymbolCodeCharacter = ">",
            DefaultBeaconMode = DynamicBeaconMode.Drive
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("CT7ALW", configuration.Settings.Aprs.Callsign);
        Assert.Equal(7, configuration.Settings.Aprs.DefaultSsid);
        Assert.Equal("\\", configuration.Settings.Aprs.DefaultSymbolTableCharacter);
        Assert.Equal(">", configuration.Settings.Aprs.DefaultSymbolCodeCharacter);
        Assert.Equal(DynamicBeaconMode.Drive, configuration.Settings.Aprs.DefaultBeaconMode);
        Assert.True(viewModel.IsSaved);
        Assert.Equal(1, configuration.SaveCount);
    }

    [Fact]
    public void OpenBeaconingSettingsNavigatesToDynamicBeaconingViewModel()
    {
        var navigation = new TestNavigationService();
        var viewModel = new SettingsViewModel(new TestConfigurationService(), navigation);

        viewModel.OpenBeaconingSettingsCommand.Execute(null);

        Assert.Equal(typeof(DynamicBeaconingViewModel), navigation.LastNavigatedType);
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
        public void GoBack() { }
    }
}

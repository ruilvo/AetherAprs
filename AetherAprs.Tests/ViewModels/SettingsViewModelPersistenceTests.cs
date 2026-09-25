// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading.Tasks;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class SettingsViewModelPersistenceTests
{
    [Fact]
    public async Task SettingsViewModel_LoadsAllSavedSettings_IncludingSsidAndSymbol()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            // Create initial settings file with SSID and non-default symbols
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                @"{
                    ""Aprs"": {
                        ""Callsign"": ""CT7ALW"",
                        ""DefaultSsid"": 5,
                        ""DefaultSymbolTableCharacter"": ""\\"",
                        ""DefaultSymbolCodeCharacter"": "">""
                    }
                }");

            var configService = new ConfigurationService(new TestAppDataDirProvider(directory));
            var navService = new TestNavigationService();
            var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());

            // Create SettingsViewModel - this should load settings from config
            var viewModel = new SettingsViewModel(configService, navService, symbolPicker);

            // Verify all settings were loaded correctly
            Assert.Equal("CT7ALW", viewModel.Callsign);
            Assert.Equal(5, viewModel.Ssid);
            Assert.Equal("\\", viewModel.DefaultSymbolTableCharacter);
            Assert.Equal(">", viewModel.DefaultSymbolCodeCharacter);

            // Verify symbol picker was initialized with loaded values
            Assert.Equal("\\", viewModel.SymbolPicker.TableCharacter);
            Assert.Equal(">", viewModel.SymbolPicker.CodeCharacter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SettingsViewModel_LoadsDefaultSsidZero()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            // Create settings with SSID = 0
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                @"{
                    ""Aprs"": {
                        ""Callsign"": ""N0CALL"",
                        ""DefaultSsid"": 0
                    }
                }");

            var configService = new ConfigurationService(new TestAppDataDirProvider(directory));
            var navService = new TestNavigationService();
            var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());

            var viewModel = new SettingsViewModel(configService, navService, symbolPicker);

            // Verify SSID = 0 is loaded correctly
            Assert.Equal(0, viewModel.Ssid);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AetherAprsTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class TestAppDataDirProvider(string directory) : IAppDataDirProviderService
    {
        public string GetAppDataDirectory() => directory;
    }

    private sealed class TestNavigationService : INavigationService
    {
        public ViewModelBase? CurrentViewModel { get; private set; }
        public event EventHandler<ViewModelBase?>? CurrentViewModelChanged;
        public bool CanGoBack => false;
#pragma warning disable CS0067 // Event is never used - required by interface
        public event EventHandler? RequestAppExit;
#pragma warning restore CS0067

        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
        {
            // No-op for tests
        }

        public void NavigateTo(ViewModelBase viewModel)
        {
            CurrentViewModel = viewModel;
            CurrentViewModelChanged?.Invoke(this, viewModel);
        }

        public void GoBack()
        {
            // No-op for tests
        }
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(Symbol symbol) => new(64, 64);
        public SKBitmap GetOverlayBitmap(SymbolCode overlayChar) => new(64, 64);
        public void Dispose() { }
    }
}

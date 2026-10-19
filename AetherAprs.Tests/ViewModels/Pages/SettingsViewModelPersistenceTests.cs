// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading.Tasks;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Platform;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class SettingsViewModelPersistenceTests
{
    [Fact]
    public void SettingsViewModel_LoadsAllSavedSettings_IncludingSsidAndSymbol()
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
                        ""Ssid"": 5,
                        ""SymbolTableCharacter"": ""\\"",
                        ""SymbolCodeCharacter"": "">""
                    }
                }");

            var configService = new ConfigurationService(new TestAppDataDirProvider(directory), NullLogger<ConfigurationService>.Instance);
            var navService = new TestNavigationService();
            var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());

            // Create SettingsViewModel - this should load settings from config
            var viewModel = new SettingsViewModel(configService, navService, symbolPicker, CreateMessageTemplatesViewModel(navService));

            // Verify all settings were loaded correctly
            Assert.Equal("CT7ALW", viewModel.Callsign);
            Assert.Equal(AprsSsid.OtherNetworks, viewModel.Ssid);
            Assert.Equal("\\", viewModel.SymbolTableCharacter);
            Assert.Equal(">", viewModel.SymbolCodeCharacter);

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
    public void SettingsViewModel_LoadsDefaultSsidZero()
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
                        ""Ssid"": 0
                    }
                }");

            var configService = new ConfigurationService(new TestAppDataDirProvider(directory), NullLogger<ConfigurationService>.Instance);
            var navService = new TestNavigationService();
            var symbolPicker = new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider());

            var viewModel = new SettingsViewModel(configService, navService, symbolPicker, CreateMessageTemplatesViewModel(navService));

            // Verify SSID = 0 is loaded correctly
            Assert.Equal(AprsSsid.PrimaryStation, viewModel.Ssid);
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

        public string GetPrivateAppDataDirectory() => directory;
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

    private static MessageTemplatesViewModel CreateMessageTemplatesViewModel(INavigationService navigation)
    {
        return new MessageTemplatesViewModel(
            Substitute.For<IMessageTemplateService>(),
            Substitute.For<IConfirmationDialogService>(),
            navigation,
            Substitute.For<IMessageTemplateEditViewModelFactory>());
    }
}


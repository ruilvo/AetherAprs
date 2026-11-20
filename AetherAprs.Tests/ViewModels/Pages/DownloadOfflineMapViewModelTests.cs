// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Platform;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Pages;
using NSubstitute;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class DownloadOfflineMapViewModelTests
{
    [Fact]
    public async Task Download_Success_SwitchesMapToDownloadedPackage()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var configuration = new TestConfigurationService();
            var navigation = new TestNavigationService();
            var appDataDir = Substitute.For<IAppDataDirProviderService>();
            appDataDir.GetAppDataDirectory().Returns(directory);
            var downloader = Substitute.For<IOfflineTilePackageDownloader>();

            var viewModel = new DownloadOfflineMapViewModel(downloader, navigation, configuration, appDataDir);
            viewModel.MinLongitude = -10;
            viewModel.MinLatitude = 40;
            viewModel.MaxLongitude = 10;
            viewModel.MaxLatitude = 50;

            await viewModel.DownloadCommand.ExecuteAsync(null);

            var expectedPath = Path.Combine(directory, "maps", "offline.mbtiles");
            await downloader.Received(1).DownloadRegionAsync(
                Arg.Is<OfflineDownloadRegion>(r =>
                    r.MinLongitude == -10 && r.MinLatitude == 40 &&
                    r.MaxLongitude == 10 && r.MaxLatitude == 50),
                expectedPath,
                Arg.Any<IProgress<OfflineDownloadProgress>>(),
                Arg.Any<CancellationToken>());

            Assert.Equal(TileSourceType.MbTilesOffline, configuration.Settings.Map.TileSource);
            Assert.Equal(expectedPath, configuration.Settings.Map.MbTilesFilePath);
            Assert.False(viewModel.HasError);
            Assert.False(viewModel.IsDownloading);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Download_InvalidLongitude_SetsErrorWithoutDownloading()
    {
        var configuration = new TestConfigurationService();
        var navigation = new TestNavigationService();
        var appDataDir = Substitute.For<IAppDataDirProviderService>();
        appDataDir.GetAppDataDirectory().Returns(CreateTemporaryDirectory());
        var downloader = Substitute.For<IOfflineTilePackageDownloader>();

        var viewModel = new DownloadOfflineMapViewModel(downloader, navigation, configuration, appDataDir);
        viewModel.MinLongitude = 10;
        viewModel.MaxLongitude = -10;

        await viewModel.DownloadCommand.ExecuteAsync(null);

        await downloader.DidNotReceive().DownloadRegionAsync(
            Arg.Any<OfflineDownloadRegion>(),
            Arg.Any<string>(),
            Arg.Any<IProgress<OfflineDownloadProgress>>(),
            Arg.Any<CancellationToken>());

        Assert.True(viewModel.HasError);
        Assert.Equal(TileSourceType.OsmOnline, configuration.Settings.Map.TileSource);
    }

    [Fact]
    public async Task Download_Failure_SetsErrorAndKeepsCurrentSource()
    {
        var configuration = new TestConfigurationService();
        var navigation = new TestNavigationService();
        var appDataDir = Substitute.For<IAppDataDirProviderService>();
        appDataDir.GetAppDataDirectory().Returns(CreateTemporaryDirectory());
        var downloader = Substitute.For<IOfflineTilePackageDownloader>();
        downloader.DownloadRegionAsync(
                Arg.Any<OfflineDownloadRegion>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<OfflineDownloadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        var viewModel = new DownloadOfflineMapViewModel(downloader, navigation, configuration, appDataDir);
        viewModel.MinLongitude = -10;
        viewModel.MinLatitude = 40;
        viewModel.MaxLongitude = 10;
        viewModel.MaxLatitude = 50;

        await viewModel.DownloadCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsDownloading);
        Assert.Equal(TileSourceType.OsmOnline, configuration.Settings.Map.TileSource);
        Assert.Empty(configuration.Settings.Map.MbTilesFilePath);
    }

    [Fact]
    public void Close_NavigatesBack()
    {
        var configuration = new TestConfigurationService();
        var navigation = new TestNavigationService();
        var appDataDir = Substitute.For<IAppDataDirProviderService>();
        var downloader = Substitute.For<IOfflineTilePackageDownloader>();

        var viewModel = new DownloadOfflineMapViewModel(downloader, navigation, configuration, appDataDir);

        viewModel.CloseCommand.Execute(null);

        Assert.True(navigation.GoBackCalled);
    }

    [Fact]
    public void Download_WhileDownloading_IsNotAllowed()
    {
        var configuration = new TestConfigurationService();
        var navigation = new TestNavigationService();
        var appDataDir = Substitute.For<IAppDataDirProviderService>();
        var downloader = Substitute.For<IOfflineTilePackageDownloader>();

        var viewModel = new DownloadOfflineMapViewModel(downloader, navigation, configuration, appDataDir);
        viewModel.IsDownloading = true;

        Assert.False(viewModel.DownloadCommand.CanExecute(null));
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AetherAprsTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public AppSettings Settings { get; } = new();

        public Task SaveSettingsAsync() => Task.CompletedTask;

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

        public bool CanGoBack => true;

        public bool GoBackCalled { get; private set; }

        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase { }

        public void NavigateTo(ViewModelBase viewModel) { }

        public void GoBack() => GoBackCalled = true;
    }
}

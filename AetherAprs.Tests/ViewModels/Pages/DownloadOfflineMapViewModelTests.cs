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
        string directory = CreateTemporaryDirectory();
        try
        {
            TestConfigurationService configuration = new();
            TestNavigationService navigation = new();
            IAppDataDirProviderService appDataDir = Substitute.For<IAppDataDirProviderService>();
            appDataDir.GetAppDataDirectory().Returns(directory);
            IOfflineTilePackageDownloader downloader = Substitute.For<IOfflineTilePackageDownloader>();

            DownloadOfflineMapViewModel viewModel = new(downloader, navigation, configuration, appDataDir);
            viewModel.SetSelection(-10, 40, 10, 50);

            await viewModel.DownloadCommand.ExecuteAsync(null);

            string expectedPath = Path.Combine(directory, "maps", "offline.mbtiles");
            await downloader.Received(1).DownloadRegionAsync(
                Arg.Is<OfflineDownloadRegion>(r =>
                    r.MinLongitude == -10 && r.MinLatitude == 40 &&
                    r.MaxLongitude == 10 && r.MaxLatitude == 50),
                expectedPath,
                Arg.Any<IProgress<OfflineDownloadProgress>>(),
                Arg.Any<CancellationToken>());

            Assert.Equal(TileSourceType.MbTilesOffline, configuration.Current.Map.TileSource);
            Assert.Equal(expectedPath, configuration.Current.Map.MbTilesFilePath);
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
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        IAppDataDirProviderService appDataDir = Substitute.For<IAppDataDirProviderService>();
        appDataDir.GetAppDataDirectory().Returns(CreateTemporaryDirectory());
        IOfflineTilePackageDownloader downloader = Substitute.For<IOfflineTilePackageDownloader>();

        DownloadOfflineMapViewModel viewModel = new(downloader, navigation, configuration, appDataDir)
        {
            MinLongitude = 10,
            MaxLongitude = -10
        };

        await viewModel.DownloadCommand.ExecuteAsync(null);

        await downloader.DidNotReceive().DownloadRegionAsync(
            Arg.Any<OfflineDownloadRegion>(),
            Arg.Any<string>(),
            Arg.Any<IProgress<OfflineDownloadProgress>>(),
            Arg.Any<CancellationToken>());

        Assert.True(viewModel.HasError);
        Assert.Equal(TileSourceType.OsmOnline, configuration.Current.Map.TileSource);
    }

    [Fact]
    public async Task Download_Failure_SetsErrorAndKeepsCurrentSource()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        IAppDataDirProviderService appDataDir = Substitute.For<IAppDataDirProviderService>();
        appDataDir.GetAppDataDirectory().Returns(CreateTemporaryDirectory());
        IOfflineTilePackageDownloader downloader = Substitute.For<IOfflineTilePackageDownloader>();
        downloader.DownloadRegionAsync(
                Arg.Any<OfflineDownloadRegion>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<OfflineDownloadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        DownloadOfflineMapViewModel viewModel = new(downloader, navigation, configuration, appDataDir);
        viewModel.SetSelection(-10, 40, 10, 50);

        await viewModel.DownloadCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsDownloading);
        Assert.Equal(TileSourceType.OsmOnline, configuration.Current.Map.TileSource);
        Assert.Empty(configuration.Current.Map.MbTilesFilePath);
    }

    [Fact]
    public void Close_NavigatesBack()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        IAppDataDirProviderService appDataDir = Substitute.For<IAppDataDirProviderService>();
        IOfflineTilePackageDownloader downloader = Substitute.For<IOfflineTilePackageDownloader>();

        DownloadOfflineMapViewModel viewModel = new(downloader, navigation, configuration, appDataDir);

        viewModel.CloseCommand.Execute(null);

        Assert.True(navigation.GoBackCalled);
    }

    [Fact]
    public void Download_WhileDownloading_IsNotAllowed()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        IAppDataDirProviderService appDataDir = Substitute.For<IAppDataDirProviderService>();
        IOfflineTilePackageDownloader downloader = Substitute.For<IOfflineTilePackageDownloader>();

        DownloadOfflineMapViewModel viewModel = new(downloader, navigation, configuration, appDataDir)
        {
            IsDownloading = true
        };

        Assert.False(viewModel.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public void Download_WithoutSelection_IsNotAllowed()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        IAppDataDirProviderService appDataDir = Substitute.For<IAppDataDirProviderService>();
        IOfflineTilePackageDownloader downloader = Substitute.For<IOfflineTilePackageDownloader>();

        DownloadOfflineMapViewModel viewModel = new(downloader, navigation, configuration, appDataDir);

        Assert.False(viewModel.HasSelection);
        Assert.False(viewModel.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public void SetSelection_NormalizesBoundsAndEnablesDownload()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        IAppDataDirProviderService appDataDir = Substitute.For<IAppDataDirProviderService>();
        IOfflineTilePackageDownloader downloader = Substitute.For<IOfflineTilePackageDownloader>();

        DownloadOfflineMapViewModel viewModel = new(downloader, navigation, configuration, appDataDir);

        // Reversed corners must still normalize to a valid region.
        viewModel.SetSelection(10, 50, -10, 40);

        Assert.Equal(-10, viewModel.MinLongitude);
        Assert.Equal(40, viewModel.MinLatitude);
        Assert.Equal(10, viewModel.MaxLongitude);
        Assert.Equal(50, viewModel.MaxLatitude);
        Assert.True(viewModel.HasSelection);
        Assert.False(string.IsNullOrEmpty(viewModel.SelectionLabel));
        Assert.True(viewModel.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public void ClearSelection_ResetsBoundsAndDisablesDownload()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        IAppDataDirProviderService appDataDir = Substitute.For<IAppDataDirProviderService>();
        IOfflineTilePackageDownloader downloader = Substitute.For<IOfflineTilePackageDownloader>();

        DownloadOfflineMapViewModel viewModel = new(downloader, navigation, configuration, appDataDir);
        viewModel.SetSelection(-10, 40, 10, 50);

        viewModel.ClearSelectionCommand.Execute(null);

        Assert.False(viewModel.HasSelection);
        Assert.Equal(0, viewModel.MinLongitude);
        Assert.Equal(0, viewModel.MinLatitude);
        Assert.Equal(0, viewModel.MaxLongitude);
        Assert.Equal(0, viewModel.MaxLatitude);
        Assert.Equal(string.Empty, viewModel.SelectionLabel);
        Assert.False(viewModel.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public void ToggleDrawMode_FlipsModeAndButtonLabel()
    {
        TestConfigurationService configuration = new();
        TestNavigationService navigation = new();
        IAppDataDirProviderService appDataDir = Substitute.For<IAppDataDirProviderService>();
        IOfflineTilePackageDownloader downloader = Substitute.For<IOfflineTilePackageDownloader>();

        DownloadOfflineMapViewModel viewModel = new(downloader, navigation, configuration, appDataDir);

        Assert.False(viewModel.IsDrawMode);
        string initialLabel = viewModel.DrawModeButtonLabel;
        Assert.False(string.IsNullOrEmpty(initialLabel));

        viewModel.ToggleDrawModeCommand.Execute(null);

        Assert.True(viewModel.IsDrawMode);
        Assert.NotEqual(initialLabel, viewModel.DrawModeButtonLabel);

        viewModel.ToggleDrawModeCommand.Execute(null);

        Assert.False(viewModel.IsDrawMode);
        Assert.Equal(initialLabel, viewModel.DrawModeButtonLabel);
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"AetherAprsTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
#pragma warning disable CS0067 // Event is never used in this test double
        public event EventHandler? SettingsChanged;
#pragma warning restore CS0067
        public AppSettings Current { get; set; } = new();

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default)
        {
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

        public bool CanGoBack => true;

        public bool GoBackCalled { get; private set; }

        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase { }

        public void NavigateTo(ViewModelBase viewModel) { }

        public void GoBack() => GoBackCalled = true;
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Location;
using AetherAprs.Services.Platform;
using AetherAprs.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for downloading a region of OpenStreetMap tiles into an offline
/// MBTiles package and switching the map to use it.
/// </summary>
public partial class DownloadOfflineMapViewModel : ViewModelBase, IDisposable
{
    private const string OfflineMapsDirectoryName = "maps";
    private const string OfflinePackageFileName = "offline.mbtiles";
    private const int MinSupportedZoom = 0;
    private const int MaxSupportedZoom = 19;

    private readonly IOfflineTilePackageDownloader _downloader;
    private readonly INavigationService _navigationService;
    private readonly IConfigurationService _configurationService;
    private readonly IAppDataDirProviderService _appDataDirProviderService;
    private readonly ILogger<DownloadOfflineMapViewModel>? _logger;
    private readonly ITransientFeedbackService? _transientFeedbackService;
    private readonly ILocationTrackingService? _locationTrackingService;

    private CancellationTokenSource? _downloadCts;

    [ObservableProperty]
    public partial double MinLatitude { get; set; }

    [ObservableProperty]
    public partial double MinLongitude { get; set; }

    [ObservableProperty]
    public partial double MaxLatitude { get; set; }

    [ObservableProperty]
    public partial double MaxLongitude { get; set; }

    [ObservableProperty]
    public partial int MinZoom { get; set; } = 8;

    [ObservableProperty]
    public partial int MaxZoom { get; set; } = 12;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial string SelectionLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDrawMode { get; set; }

    [ObservableProperty]
    public partial string DrawModeButtonLabel { get; set; } = Strings.Get("OfflineMapDrawArea");

    partial void OnIsDrawModeChanged(bool value)
    {
        DrawModeButtonLabel = Strings.Get(value ? "OfflineMapStopDrawing" : "OfflineMapDrawArea");
    }

    /// <summary>
    /// Latitude to center the selection map on when it first loads, or null if the
    /// device has not reported a location yet.
    /// </summary>
    public double? InitialLatitude => _locationTrackingService?.CurrentLocation?.Location.Latitude;

    /// <summary>
    /// Longitude to center the selection map on when it first loads, or null if the
    /// device has not reported a location yet.
    /// </summary>
    public double? InitialLongitude => _locationTrackingService?.CurrentLocation?.Location.Longitude;

    public DownloadOfflineMapViewModel(
        IOfflineTilePackageDownloader downloader,
        INavigationService navigationService,
        IConfigurationService configurationService,
        IAppDataDirProviderService appDataDirProviderService,
        ILogger<DownloadOfflineMapViewModel>? logger = null,
        ITransientFeedbackService? transientFeedbackService = null,
        ILocationTrackingService? locationTrackingService = null)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _appDataDirProviderService = appDataDirProviderService ?? throw new ArgumentNullException(nameof(appDataDirProviderService));
        _logger = logger;
        _transientFeedbackService = transientFeedbackService;
        _locationTrackingService = locationTrackingService;
    }

    private bool CanDownload() => !IsDownloading && HasSelection;

    /// <summary>
    /// Records a selection drawn on the map. The bounds are normalized so the smaller
    /// longitude/latitude always become the minimums.
    /// </summary>
    public void SetSelection(double minLongitude, double minLatitude, double maxLongitude, double maxLatitude)
    {
        MinLongitude = Math.Min(minLongitude, maxLongitude);
        MinLatitude = Math.Min(minLatitude, maxLatitude);
        MaxLongitude = Math.Max(minLongitude, maxLongitude);
        MaxLatitude = Math.Max(minLatitude, maxLatitude);
        HasSelection = true;
        HasError = false;
        StatusMessage = string.Empty;
        SelectionLabel = Strings.Format(
            "OfflineMapRegionSelected",
            FormatBounds(MinLatitude, MinLongitude, MaxLatitude, MaxLongitude));
    }

    [RelayCommand]
    private void ToggleDrawMode()
    {
        IsDrawMode = !IsDrawMode;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        HasSelection = false;
        MinLongitude = 0;
        MinLatitude = 0;
        MaxLongitude = 0;
        MaxLatitude = 0;
        SelectionLabel = string.Empty;
        HasError = false;
        StatusMessage = string.Empty;
    }

    private static string FormatBounds(double minLatitude, double minLongitude, double maxLatitude, double maxLongitude)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:F4}, {1:F4} → {2:F4}, {3:F4}",
            minLatitude,
            minLongitude,
            maxLatitude,
            maxLongitude);
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        if (!ValidateInputs(out var region))
        {
            return;
        }

        IsDownloading = true;
        HasError = false;
        ProgressPercent = 0;
        StatusMessage = Strings.Get("OfflineMapDownloading");
        _downloadCts = new CancellationTokenSource();

        try
        {
            var appDataDirectory = _appDataDirProviderService.GetAppDataDirectory();
            var mapsDirectory = Path.Combine(appDataDirectory, OfflineMapsDirectoryName);
            var outputPath = Path.Combine(mapsDirectory, OfflinePackageFileName);

            var progress = new Progress<OfflineDownloadProgress>(report =>
            {
                if (report.TotalTiles > 0)
                {
                    ProgressPercent = report.CompletedTiles * 100.0 / report.TotalTiles;
                }
            });

            await _downloader.DownloadRegionAsync(region, outputPath, progress, _downloadCts.Token)
                .ConfigureAwait(false);

            await _configurationService.UpdateSettingsAsync(settings =>
            {
                settings.Map.TileSource = Configuration.TileSourceType.MbTilesOffline;
                settings.Map.MbTilesFilePath = outputPath;
            }).ConfigureAwait(false);

            ProgressPercent = 100;
            StatusMessage = Strings.Format("OfflineMapDownloadComplete", outputPath);
            _transientFeedbackService?.Show(Strings.Get("OfflineMapDownloadFinishedFeedback"));
        }
        catch (OperationCanceledException)
        {
            HasError = true;
            StatusMessage = Strings.Get("OfflineMapDownloadCancelled");
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = Strings.Get("OfflineMapDownloadFailed");
            _logger?.LogError(ex, "Error downloading offline map package");
        }
        finally
        {
            IsDownloading = false;
            _downloadCts.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _downloadCts?.Cancel();
    }

    [RelayCommand]
    private void Close()
    {
        _downloadCts?.Cancel();
        _navigationService.GoBack();
    }

    private bool ValidateInputs(out OfflineDownloadRegion region)
    {
        region = default!;

        if (MinLongitude >= MaxLongitude)
        {
            HasError = true;
            StatusMessage = Strings.Get("OfflineMapInvalidLongitude");
            return false;
        }

        if (MinLatitude >= MaxLatitude)
        {
            HasError = true;
            StatusMessage = Strings.Get("OfflineMapInvalidLatitude");
            return false;
        }

        if (MinZoom < MinSupportedZoom || MaxZoom > MaxSupportedZoom || MinZoom > MaxZoom)
        {
            HasError = true;
            StatusMessage = Strings.Format("OfflineMapInvalidZoom", MinSupportedZoom, MaxSupportedZoom);
            return false;
        }

        region = new OfflineDownloadRegion(MinLongitude, MinLatitude, MaxLongitude, MaxLatitude, MinZoom, MaxZoom);
        HasError = false;
        return true;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = null;
    }
}

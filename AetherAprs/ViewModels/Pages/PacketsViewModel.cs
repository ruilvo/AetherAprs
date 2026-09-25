// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Factories;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.Configuration;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for the Packets page showing the most recent packet per callsign.
/// Queries packet data from the database with periodic refresh.
/// </summary>
public partial class PacketsViewModel : ViewModelBase, IDisposable
{
    private readonly IPacketQueryService _packetQueryService;
    private readonly IPacketStorageService _packetStorageService;
    private readonly INavigationService _navigationService;
    private readonly IPacketDetailsViewModelFactory _packetDetailsFactory;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<PacketsViewModel> _logger;
    private readonly DispatcherTimer _refreshTimer;
    private bool _pendingRefresh;
    private bool _disposed;

    [ObservableProperty]
    public partial string Title { get; set; } = "Packets";

    [ObservableProperty]
    public partial ObservableCollection<PacketSummary> Packets { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public PacketsViewModel(
        IPacketQueryService packetQueryService,
        IPacketStorageService packetStorageService,
        INavigationService navigationService,
        IPacketDetailsViewModelFactory packetDetailsFactory,
        IConfigurationService configurationService,
        ILogger<PacketsViewModel> logger)
    {
        _packetQueryService = packetQueryService;
        _packetStorageService = packetStorageService;
        _navigationService = navigationService;
        _packetDetailsFactory = packetDetailsFactory;
        _configurationService = configurationService;
        _logger = logger;

        // Throttle UI updates to every 500ms to avoid overwhelming the UI thread
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();

        _packetStorageService.PacketStored += OnPacketStored;
        
        // Load initial data from database
        _ = LoadPacketsAsync();
    }

    private void OnPacketStored(object? sender, EventArgs e)
    {
        // Mark that we need a refresh, but don't trigger immediately
        // The timer will pick it up on the next tick
        _pendingRefresh = true;
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_pendingRefresh)
        {
            _pendingRefresh = false;
            await Dispatcher.UIThread.InvokeAsync(() => LoadPacketsAsync());
        }
    }

    private async Task LoadPacketsAsync()
    {
        try
        {
            IsLoading = true;

            // Get the time range filter from configuration
            var timeRange = _configurationService.Settings.Aprs.DisplayTimeRange;
            var customHours = _configurationService.Settings.Aprs.CustomDisplayTimeRangeHours;
            
            DateTimeOffset? cutoffTime = GetCutoffTime(timeRange, customHours);

            // Load most recent packets from database
            var allPackets = await _packetQueryService.GetMostRecentPacketsAsync();
            
            // Apply time filter if not "All"
            var filteredPackets = allPackets.Values.AsEnumerable();
            if (cutoffTime.HasValue)
            {
                filteredPackets = filteredPackets.Where(r => new DateTimeOffset(r.ReceivedAt, TimeSpan.Zero) >= cutoffTime.Value);
            }

            // Sort by most recent first and take top 100
            var packets = filteredPackets
                .OrderByDescending(r => r.Id)
                .Take(100)
                .ToList();

            _logger.LogInformation("Loaded {Count} packets from database with time filter {TimeRange}", packets.Count, timeRange);

            // Build the list of summaries
            var summaries = packets
                .Select(record => new PacketSummary
                {
                    Source = record.Source,
                    PacketType = record.PacketType,
                    ReceivedAt = new DateTimeOffset(record.ReceivedAt, TimeSpan.Zero),
                    Preview = GetPacketPreview(record)
                })
                .ToList();

            // Update observable collection on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Packets.Clear();
                foreach (var summary in summaries)
                {
                    Packets.Add(summary);
                }
            });

            _logger.LogInformation("Displayed {Count} packets in UI", Packets.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load packets from database");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static DateTimeOffset? GetCutoffTime(PacketDisplayTimeRange timeRange, int customHours)
    {
        var now = DateTimeOffset.UtcNow;
        return timeRange switch
        {
            PacketDisplayTimeRange.LastHour => now.AddHours(-1),
            PacketDisplayTimeRange.LastDay => now.AddDays(-1),
            PacketDisplayTimeRange.LastWeek => now.AddDays(-7),
            PacketDisplayTimeRange.LastMonth => now.AddDays(-30),
            PacketDisplayTimeRange.Custom => now.AddHours(-customHours),
            PacketDisplayTimeRange.All => null,
            _ => now.AddDays(-1) // Default to last day
        };
    }

    private static string GetPacketPreview(PacketRecord record)
    {
        if (record.Latitude.HasValue && record.Longitude.HasValue)
        {
            return $"Lat: {record.Latitude.Value:F4}, Lon: {record.Longitude.Value:F4}";
        }
        
        if (!string.IsNullOrEmpty(record.MessageText))
        {
            var addressee = record.MessageAddressee ?? "Unknown";
            return $"To {addressee}: {record.MessageText}";
        }
        
        if (!string.IsNullOrEmpty(record.StatusText))
        {
            return record.StatusText;
        }
        
        return record.RawInfo != null && record.RawInfo.Length > 40 ? record.RawInfo[..40] + "..." : record.RawInfo ?? "";
    }

    [RelayCommand]
    private void OpenPacketDetails(PacketSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        var vm = _packetDetailsFactory.Create(summary.Source);
        _navigationService.NavigateTo(vm);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _packetStorageService.PacketStored -= OnPacketStored;
        _disposed = true;
    }
}

/// <summary>
/// Summary of a packet for display in the list.
/// </summary>
public partial class PacketSummary : ObservableObject
{
    [ObservableProperty]
    public partial string Source { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PacketType { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTimeOffset ReceivedAt { get; set; }

    [ObservableProperty]
    public partial string Preview { get; set; } = string.Empty;
}

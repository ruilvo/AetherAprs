// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Factories;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.Data;
using AetherAprs.Configuration;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for the Packets page showing the most recent packet per callsign.
/// Uses the shared PacketCacheService for real-time updates.
/// </summary>
public partial class PacketsViewModel : ViewModelBase, IDisposable
{
    private readonly IPacketCacheService _packetCacheService;
    private readonly INavigationService _navigationService;
    private readonly IPacketDetailsViewModelFactory _packetDetailsFactory;
    private readonly IConfigurationService _configurationService;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
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
        IPacketCacheService packetCacheService,
        INavigationService navigationService,
        IPacketDetailsViewModelFactory packetDetailsFactory,
        IConfigurationService configurationService,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<PacketsViewModel> logger)
    {
        _packetCacheService = packetCacheService;
        _navigationService = navigationService;
        _packetDetailsFactory = packetDetailsFactory;
        _configurationService = configurationService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;

        // Throttle UI updates to every 500ms to avoid overwhelming the UI thread
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();

        _packetCacheService.CacheUpdated += OnCacheUpdated;
        LoadPacketsFromCache();
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_pendingRefresh)
        {
            _pendingRefresh = false;
            await Dispatcher.UIThread.InvokeAsync(() => LoadPacketsAsync());
        }
    }

    private void OnCacheUpdated(object? sender, PacketCacheUpdatedEventArgs e)
    {
        // Mark that we need a refresh, but don't trigger immediately
        // The timer will pick it up on the next tick
        _pendingRefresh = true;
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

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            
            var query = dbContext.Packets
                .AsNoTracking()
                .OrderByDescending(p => p.ReceivedAt);

            // Apply time filter if not "All"
            if (cutoffTime.HasValue)
            {
                query = (IOrderedQueryable<PacketRecord>)query.Where(p => p.ReceivedAt >= cutoffTime.Value);
            }

            // Group by source and take the most recent packet for each
            var packets = await query
                .GroupBy(p => p.Source)
                .Select(g => g.First())
                .Take(100)
                .ToListAsync();

            _logger.LogInformation("Loaded {Count} packets from database with time filter {TimeRange}", packets.Count, timeRange);

            // Build the list of summaries
            var summaries = packets
                .Select(record => new PacketSummary
                {
                    Source = record.Source,
                    PacketType = record.PacketType,
                    ReceivedAt = record.ReceivedAt,
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

    private void LoadPacketsFromCache()
    {
        // Trigger async load
        _ = LoadPacketsAsync();
    }

    private static string GetPacketTypeName(AprsPacket packet)
    {
        return packet switch
        {
            PositionPacket => "Position",
            MessagePacket => "Message",
            StatusPacket => "Status",
            WeatherPacket => "Weather",
            UnknownPacket => "Unknown",
            _ => packet.GetType().Name
        };
    }

    private static string GetPacketPreview(PacketRecord record)
    {
        // For database records, we build preview from stored fields
        if (record.PacketType == "Position" && record.Latitude.HasValue && record.Longitude.HasValue)
        {
            return $"Lat: {record.Latitude.Value:F4}, Lon: {record.Longitude.Value:F4}";
        }
        else if (record.PacketType == "Message" && !string.IsNullOrEmpty(record.MessageAddressee))
        {
            return $"To {record.MessageAddressee}: {record.MessageText ?? ""}";
        }
        else if (record.PacketType == "Weather" && record.Temperature.HasValue)
        {
            return $"Temp: {record.Temperature}°F";
        }
        else if (!string.IsNullOrEmpty(record.RawInfo))
        {
            return record.RawInfo.Length > 40 ? record.RawInfo[..40] + "..." : record.RawInfo;
        }
        
        return "";
    }

    private static string GetPacketPreview(AprsPacket packet)
    {
        return packet switch
        {
            PositionPacket pos => $"Lat: {pos.Latitude:F4}, Lon: {pos.Longitude:F4}",
            MessagePacket msg => $"To {msg.Addressee}: {msg.Text}",
            StatusPacket status => status.Text ?? "",
            WeatherPacket weather => $"Temp: {weather.Temperature}°F",
            _ => packet.Raw.Length > 40 ? packet.Raw[..40] + "..." : packet.Raw
        };
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
        _packetCacheService.CacheUpdated -= OnCacheUpdated;
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

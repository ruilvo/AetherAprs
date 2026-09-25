// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for the Packets page showing the most recent packet per callsign.
/// Uses the shared PacketCacheService for real-time updates.
/// </summary>
public partial class PacketsViewModel : ViewModelBase, IDisposable
{
    private readonly IPacketCacheService _packetCacheService;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;
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
        IServiceProvider serviceProvider,
        ILogger<PacketsViewModel> logger)
    {
        _packetCacheService = packetCacheService;
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;
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
            await Dispatcher.UIThread.InvokeAsync(LoadPacketsFromCache);
        }
    }

    private void OnCacheUpdated(object? sender, PacketCacheUpdatedEventArgs e)
    {
        // Mark that we need a refresh, but don't trigger immediately
        // The timer will pick it up on the next tick
        _pendingRefresh = true;
    }

    private void LoadPacketsFromCache()
    {
        try
        {
            var allPackets = _packetCacheService.GetAllPackets();

            _logger.LogInformation("Loading {Count} packets from cache", allPackets.Count);

            // Sort by most recent first and take top 100
            var sortedPackets = allPackets.Values
                .OrderByDescending(cp => cp.ReceivedAt)
                .Take(100)
                .ToList();

            // Build the list of summaries
            var summaries = sortedPackets
                .Select(cp => new PacketSummary
                {
                    Source = cp.Source,
                    PacketType = GetPacketTypeName(cp.Packet),
                    ReceivedAt = cp.ReceivedAt,
                    Preview = GetPacketPreview(cp.Packet)
                })
                .ToList();

            // Update observable collection
            Packets.Clear();
            foreach (var summary in summaries)
            {
                Packets.Add(summary);
            }

            _logger.LogInformation("Displayed {Count} packets in UI", Packets.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load packets from cache");
        }
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

        var vm = _serviceProvider.GetRequiredService<PacketDetailsViewModel>();
        vm.Initialize(summary.Source);
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

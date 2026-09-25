// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for the Packets page showing the most recent packet per callsign.
/// </summary>
public partial class PacketsViewModel : ViewModelBase, IDisposable
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPortService _portService;
    private readonly ILogger<PacketsViewModel> _logger;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private DateTimeOffset _lastUpdateTime = DateTimeOffset.MinValue;
    private bool _pendingRefresh;
    private bool _disposed;

    [ObservableProperty]
    public partial string Title { get; set; } = "Packets";

    [ObservableProperty]
    public partial ObservableCollection<PacketSummary> Packets { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public PacketsViewModel(
        IDbContextFactory<AppDbContext> dbContextFactory,
        INavigationService navigationService,
        IServiceProvider serviceProvider,
        IPortService portService,
        ILogger<PacketsViewModel> logger)
    {
        _dbContextFactory = dbContextFactory;
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;
        _portService = portService;
        _logger = logger;

        // Throttle UI updates to every 2 seconds to avoid overwhelming the UI thread
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();

        _portService.PacketReceived += OnPacketReceived;
        _ = LoadPacketsAsync();
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_pendingRefresh)
        {
            _pendingRefresh = false;
            await Task.Run(async () => await LoadPacketsIncrementalAsync());
        }
    }

    private void OnPacketReceived(object? sender, PortPacketReceivedEventArgs e)
    {
        // Mark that we need a refresh, but don't trigger immediately
        // The timer will pick it up on the next tick
        _pendingRefresh = true;
    }

    private async Task LoadPacketsIncrementalAsync()
    {
        if (!await _updateLock.WaitAsync(0))
        {
            // Another update is already in progress, skip this one
            return;
        }

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();

            // Only fetch packets that have been updated since our last check
            var newOrUpdatedPackets = await context.Packets
                .Where(p => p.ReceivedAt > _lastUpdateTime)
                .GroupBy(p => p.Source)
                .Select(g => g.OrderByDescending(p => p.ReceivedAt.UtcTicks).First())
                .ToListAsync();

            if (newOrUpdatedPackets.Count == 0)
            {
                return;
            }

            _lastUpdateTime = DateTimeOffset.UtcNow;

            // Update the UI on the UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var packet in newOrUpdatedPackets)
                {
                    // Find existing entry for this source
                    var existing = Packets.FirstOrDefault(p => p.Source == packet.Source);

                    if (existing is not null)
                    {
                        // Update existing entry
                        existing.PacketType = packet.PacketType;
                        existing.ReceivedAt = packet.ReceivedAt;
                        existing.Preview = GetPacketPreview(packet);
                    }
                    else
                    {
                        // Add new entry
                        Packets.Add(new PacketSummary
                        {
                            Source = packet.Source,
                            PacketType = packet.PacketType,
                            ReceivedAt = packet.ReceivedAt,
                            Preview = GetPacketPreview(packet)
                        });
                    }
                }

                // Sort by most recent first (only if we added new items)
                if (Packets.Count > 0)
                {
                    var sorted = Packets.OrderByDescending(p => p.ReceivedAt).Take(100).ToList();

                    // Only rebuild if order changed significantly or we have too many items
                    if (Packets.Count > 100 || !Packets.Take(10).SequenceEqual(sorted.Take(10)))
                    {
                        Packets.Clear();
                        foreach (var item in sorted)
                        {
                            Packets.Add(item);
                        }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load packets incrementally");
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private async Task LoadPacketsAsync()
    {
        try
        {
            IsLoading = true;

            await using var context = await _dbContextFactory.CreateDbContextAsync();

            _logger.LogInformation("Starting to load packets from database");

            // First, get total count to verify database has data
            var totalCount = await context.Packets.CountAsync();
            _logger.LogInformation("Total packets in database: {Count}", totalCount);

            // Get all packets first, then group in memory
            // This avoids potential SQLite GroupBy translation issues
            var allPackets = await context.Packets
                .OrderByDescending(p => p.ReceivedAt.UtcTicks)
                .ToListAsync();

            _logger.LogInformation("Retrieved {Count} packets from database, now grouping by source", allPackets.Count);

            // Group by source and take most recent per source
            var latestPackets = allPackets
                .GroupBy(p => p.Source)
                .Select(g => g.First()) // Already ordered by ReceivedAt descending
                .Take(100)
                .ToList();

            _logger.LogInformation("Grouped into {Count} unique sources", latestPackets.Count);

            // Update the last update time to now so incremental updates work correctly
            _lastUpdateTime = DateTimeOffset.UtcNow;

            // ObservableCollection modifications must happen on the UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Packets.Clear();
                foreach (var packet in latestPackets)
                {
                    Packets.Add(new PacketSummary
                    {
                        Source = packet.Source,
                        PacketType = packet.PacketType,
                        ReceivedAt = packet.ReceivedAt,
                        Preview = GetPacketPreview(packet)
                    });
                }
                _logger.LogInformation("Added {Count} packets to observable collection", Packets.Count);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load packets");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string GetPacketPreview(PacketRecord packet)
    {
        return packet.PacketType switch
        {
            "Position" => $"Lat: {packet.Latitude:F4}, Lon: {packet.Longitude:F4}",
            "Message" => $"To {packet.MessageAddressee}: {packet.MessageText}",
            "Status" => packet.StatusText ?? "",
            "Weather" => $"Temp: {packet.Temperature}°F",
            _ => packet.RawInfo.Length > 40 ? packet.RawInfo[..40] + "..." : packet.RawInfo
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
        _portService.PacketReceived -= OnPacketReceived;
        _updateLock.Dispose();
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

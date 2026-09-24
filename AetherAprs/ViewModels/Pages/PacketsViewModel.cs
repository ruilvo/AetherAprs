// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Data;
using AetherAprs.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        _portService.PacketReceived += OnPacketReceived;
        _ = LoadPacketsAsync();
    }

    private async void OnPacketReceived(object? sender, PortPacketReceivedEventArgs e)
    {
        // Reload packets when new ones arrive
        await LoadPacketsAsync();
    }

    private async Task LoadPacketsAsync()
    {
        try
        {
            IsLoading = true;

            await using var context = await _dbContextFactory.CreateDbContextAsync();

            // Get the most recent packet per source callsign
            var latestPackets = await context.Packets
                .GroupBy(p => p.Source)
                .Select(g => g.OrderByDescending(p => p.ReceivedAt).First())
                .OrderByDescending(p => p.ReceivedAt)
                .Take(100)
                .ToListAsync();

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

        _portService.PacketReceived -= OnPacketReceived;
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

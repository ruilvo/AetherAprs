// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Factories;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for packet details page showing all packets from a specific callsign.
/// </summary>
public partial class PacketDetailsViewModel(
    IDbContextFactory<AppDbContext> dbContextFactory,
    INavigationService navigationService,
    IConversationViewModelFactory conversationFactory,
    ILogger<PacketDetailsViewModel> logger) : ViewModelBase
{
    [ObservableProperty]
    public partial string Callsign { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Title { get; set; } = "Packet Details";

    [ObservableProperty]
    public partial ObservableCollection<PacketRecord> Packets { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public void Initialize(string callsign)
    {
        Callsign = callsign;
        Title = $"Packets from {callsign}";
        _ = LoadPacketsAsync();
    }

    private async Task LoadPacketsAsync()
    {
        try
        {
            IsLoading = true;

            await using var context = await dbContextFactory.CreateDbContextAsync();

            var packets = await context.Packets
                .Where(p => p.Source == Callsign)
                .OrderByDescending(p => p.ReceivedAt)
                .Take(500)
                .ToListAsync();

            Packets.Clear();
            foreach (var packet in packets)
            {
                Packets.Add(packet);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load packets for {Callsign}", Callsign);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SendMessage()
    {
        // Navigate to messages page and open conversation with this callsign
        navigationService.NavigateTo<MessagesViewModel>();

        // Parse callsign to extract base callsign (remove SSID if present)
        var callsignStr = Callsign;
        var dashIndex = callsignStr.IndexOf('-');
        if (dashIndex > 0)
        {
            callsignStr = callsignStr[..dashIndex];
        }

        try
        {
            var callsign = new Callsign(callsignStr);
            var conversationVm = conversationFactory.Create(callsign);
            navigationService.NavigateTo(conversationVm);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse callsign {Callsign}", Callsign);
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        navigationService.NavigateTo<PacketsViewModel>();
    }
}

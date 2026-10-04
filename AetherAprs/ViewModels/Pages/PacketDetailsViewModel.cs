// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Packets;
using AetherAprs.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for packet details page showing all packets from a specific callsign.
/// </summary>
public partial class PacketDetailsViewModel(
    IPacketQueryService packetQueryService,
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
        logger.LogInformation("Initializing PacketDetailsViewModel with callsign: {Callsign}", callsign);
        Callsign = callsign;
        Title = $"Packets from {callsign}";
        _ = LoadPacketsAsync();
    }

    private async Task LoadPacketsAsync()
    {
        try
        {
            IsLoading = true;

            logger.LogInformation("Loading packets for callsign: {Callsign}", Callsign);

            var packets = await packetQueryService.GetPacketsByCallsignAsync(Callsign, 500);

            logger.LogInformation("Found {Count} packets for callsign {Callsign}", packets.Count, Callsign);

            Packets.Clear();
            foreach (var packet in packets)
            {
                Packets.Add(packet);
            }

            logger.LogInformation("Added {Count} packets to observable collection", Packets.Count);
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

        // Parse callsign - try full format first, then extract base if needed
        if (Models.Aprs.Callsign.TryParse(Callsign, out var callsign))
        {
            // Use base callsign without SSID for messaging
            var baseCallsign = new Callsign(callsign.Base);
            var conversationVm = conversationFactory.Create(baseCallsign);
            navigationService.NavigateTo(conversationVm);
        }
        else
        {
            logger.LogError("Failed to parse callsign {Callsign}", Callsign);
        }
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Data;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for packet details page showing all packets from a specific callsign.
/// </summary>
public partial class PacketDetailsViewModel : ViewModelBase
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PacketDetailsViewModel> _logger;

    [ObservableProperty]
    public partial string Callsign { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Title { get; set; } = "Packet Details";

    [ObservableProperty]
    public partial ObservableCollection<PacketRecord> Packets { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public PacketDetailsViewModel(
        IDbContextFactory<AppDbContext> dbContextFactory,
        INavigationService navigationService,
        IServiceProvider serviceProvider,
        ILogger<PacketDetailsViewModel> logger)
    {
        _dbContextFactory = dbContextFactory;
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

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

            await using var context = await _dbContextFactory.CreateDbContextAsync();

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
            _logger.LogError(ex, "Failed to load packets for {Callsign}", Callsign);
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
        _navigationService.NavigateTo<MessagesViewModel>();

        // Open conversation with the callsign
        var conversationVm = _serviceProvider.GetRequiredService<ConversationViewModel>();
        
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
            conversationVm.Initialize(callsign);
            _navigationService.NavigateTo(conversationVm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse callsign {Callsign}", Callsign);
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<PacketsViewModel>();
    }
}

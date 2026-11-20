// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Entities;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Localization;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Packets;
using AetherAprs.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// View model for the station/object search page. Debounces the query text,
/// searches via <see cref="IPacketQueryService.SearchStationsAsync"/>, and opens
/// the packet details page when a result is tapped.
/// </summary>
public sealed partial class SearchViewModel : ViewModelBase
{
    private const int DebounceMilliseconds = 300;

    private readonly IPacketQueryService _packetQueryService;
    private readonly INavigationService _navigationService;
    private readonly IPacketDetailsViewModelFactory _packetDetailsFactory;
    private readonly ILogger<SearchViewModel>? _logger;
    private CancellationTokenSource? _debounceCts;

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<SearchResultItem> Results { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// True once at least one search has run, so the UI can distinguish the
    /// initial prompt from a genuine "no results" state.
    /// </summary>
    [ObservableProperty]
    public partial bool HasSearched { get; set; }

    /// <summary>
    /// True when a search completed with no matches.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowNoResults { get; set; }

    public SearchViewModel(
        IPacketQueryService packetQueryService,
        INavigationService navigationService,
        IPacketDetailsViewModelFactory packetDetailsFactory,
        ILogger<SearchViewModel>? logger = null)
    {
        _packetQueryService = packetQueryService ?? throw new ArgumentNullException(nameof(packetQueryService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _packetDetailsFactory = packetDetailsFactory ?? throw new ArgumentNullException(nameof(packetDetailsFactory));
        _logger = logger;
    }

    partial void OnSearchQueryChanged(string value)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        // Clearing the box resets immediately rather than after the debounce delay.
        if (string.IsNullOrWhiteSpace(value))
        {
            Results.Clear();
            HasSearched = false;
            ShowNoResults = false;
            IsLoading = false;
            return;
        }

        _ = DebouncedSearchAsync(value, token);
    }

    private async Task DebouncedSearchAsync(string query, CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, token);
            await SearchAsync(query, token);
        }
        catch (OperationCanceledException)
        {
            // A newer query superseded this one.
        }
    }

    private async Task SearchAsync(string query, CancellationToken token)
    {
        var trimmed = query?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            Results.Clear();
            HasSearched = false;
            ShowNoResults = false;
            IsLoading = false;
            return;
        }

        try
        {
            IsLoading = true;
            var packets = await _packetQueryService.SearchStationsAsync(trimmed, limit: 100, cancellationToken: token);

            Results.Clear();
            foreach (var packet in packets)
            {
                Results.Add(CreateResultItem(packet));
            }

            HasSearched = true;
            ShowNoResults = Results.Count == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Search failed for query {Query}", trimmed);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static SearchResultItem CreateResultItem(PacketRecord packet)
    {
        var source = FormatCallsign(packet.SourceBase, packet.SourceSsid);

        // Object packets match on their name and are reported by a station; the
        // details page is keyed by source callsign, so navigating an object opens
        // the reporting station's history (which contains the object report).
        if (packet.Object != null)
        {
            return new SearchResultItem(
                packet.Object.Name,
                Strings.Get("SearchObjectKind"),
                source,
                Strings.Format("SearchObjectReportedBy", source));
        }

        return new SearchResultItem(
            source,
            Strings.Get("SearchStationKind"),
            source,
            null);
    }

    private static string FormatCallsign(string baseCallsign, AprsSsid ssid)
    {
        return ssid != AprsSsid.PrimaryStation
            ? $"{baseCallsign}-{(byte)ssid}"
            : baseCallsign;
    }

    [RelayCommand]
    private void OpenResult(SearchResultItem? item)
    {
        if (item is null)
        {
            return;
        }

        var vm = _packetDetailsFactory.Create(item.Source);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void Close()
    {
        _navigationService.GoBack();
    }
}

/// <summary>
/// A single search result for display in the search list.
/// </summary>
public sealed record SearchResultItem(
    string Name,
    string Kind,
    string Source,
    string? Subtitle);

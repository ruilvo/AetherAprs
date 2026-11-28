// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Data.Entities;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Localization;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Packets;
using AetherAprs.Services.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// View model for the station/object search page. Debounces the query text,
/// searches via <see cref="IPacketQueryService.SearchStationsAsync"/>, and opens
/// the packet details page when a result is tapped.
/// </summary>
public sealed partial class SearchViewModel(
    IPacketQueryService packetQueryService,
    INavigationService navigationService,
    IPacketDetailsViewModelFactory packetDetailsFactory,
    IObjectDetailsViewModelFactory objectDetailsFactory,
    ILogger<SearchViewModel>? logger = null) : ViewModelBase
{
    private const int DebounceMilliseconds = 300;

    private readonly IPacketQueryService _packetQueryService = packetQueryService ?? throw new ArgumentNullException(nameof(packetQueryService));
    private readonly INavigationService _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    private readonly IPacketDetailsViewModelFactory _packetDetailsFactory = packetDetailsFactory ?? throw new ArgumentNullException(nameof(packetDetailsFactory));
    private readonly IObjectDetailsViewModelFactory _objectDetailsFactory = objectDetailsFactory ?? throw new ArgumentNullException(nameof(objectDetailsFactory));
    private readonly ILogger<SearchViewModel>? _logger = logger;
    private CancellationTokenSource? _debounceCts;

    // Every query change starts a new generation. A completed search only applies
    // its results, error and loading flag while its generation is still current, so
    // a slow older search can never overwrite a newer one or hide its spinner.
    private int _searchGeneration;

    /// <summary>
    /// The search started by the latest query change. Tests await it instead of
    /// polling, which is what makes the superseded-search cases deterministic.
    /// </summary>
    internal Task? PendingSearch { get; private set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<SearchResultItem> Results { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Results"/> has any items.
    /// Updated whenever the collection is mutated so the page bindings do not
    /// go stale on in-place changes.
    /// </summary>
    [ObservableProperty]
    public partial bool HasResults { get; set; }

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

    partial void OnSearchQueryChanged(string value)
    {
        // Supersede the previous search before starting the next one. The source is
        // not disposed here: the superseded operation may still be using its token,
        // and a CTS used only with Task.Delay holds no unmanaged resource. Disposing
        // it eagerly is what made a newer query able to fault the older search.
        CancellationTokenSource? previous = _debounceCts;
        CancellationTokenSource cancellation = new();
        _debounceCts = cancellation;
        previous?.Cancel();

        int generation = ++_searchGeneration;

        // Clearing the box resets immediately rather than after the debounce delay.
        if (string.IsNullOrWhiteSpace(value))
        {
            PendingSearch = null;
            ResetSearchState();
            return;
        }

        PendingSearch = DebouncedSearchAsync(value, generation, cancellation.Token);
    }

    private async Task DebouncedSearchAsync(string query, int generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, token);
            await SearchAsync(query, generation, token);
        }
        catch (OperationCanceledException)
        {
            // A newer query superseded this one.
        }
        catch (ObjectDisposedException)
        {
            // The page was torn down while this search was waiting.
        }
        catch (Exception exception)
        {
            // This task is fire-and-forget, so nothing else would observe a failure
            // here; record it instead of letting it surface as an unobserved crash.
            _logger?.LogError(exception, "Search task failed for query {Query}", query);
        }
    }

    private async Task SearchAsync(string query, int generation, CancellationToken token)
    {
        string trimmed = query?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            if (IsCurrentGeneration(generation))
            {
                ResetSearchState();
            }

            return;
        }

        try
        {
            if (IsCurrentGeneration(generation))
            {
                IsLoading = true;
            }

            IReadOnlyList<PacketRecord> packets = await _packetQueryService.SearchStationsAsync(trimmed, limit: 100, cancellationToken: token);

            // A search that was superseded while it was running must not touch the
            // collection: the newer search owns it now.
            if (!IsCurrentGeneration(generation))
            {
                return;
            }

            Results.Clear();
            foreach (PacketRecord packet in packets)
            {
                Results.Add(CreateResultItem(packet));
            }

            HasResults = Results.Count > 0;
            HasSearched = true;
            ShowNoResults = !HasResults;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (IsCurrentGeneration(generation))
            {
                _logger?.LogError(ex, "Search failed for query {Query}", trimmed);
            }
        }
        finally
        {
            if (IsCurrentGeneration(generation))
            {
                IsLoading = false;
            }
        }
    }

    private bool IsCurrentGeneration(int generation) =>
        generation == Volatile.Read(ref _searchGeneration);

    private void ResetSearchState()
    {
        Results.Clear();
        HasResults = false;
        HasSearched = false;
        ShowNoResults = false;
        IsLoading = false;
    }

    private static SearchResultItem CreateResultItem(PacketRecord packet)
    {
        string source = AprsSsidExtensions.FormatCallsign(packet.SourceBase, packet.SourceSsid);

        // An object result is identified by the object's own name, not by the
        // station that last reported it: the same object is reported by different
        // stations over time, and its report history belongs to the object.
        if (packet.Object != null)
        {
            return new SearchResultItem(
                packet.Object.Name,
                Strings.Get("SearchObjectKind"),
                source,
                Strings.Format("SearchObjectReportedBy", source),
                packet.Object.Name);
        }

        return new SearchResultItem(
            source,
            Strings.Get("SearchStationKind"),
            source,
            null);
    }

    [RelayCommand]
    private void OpenResult(SearchResultItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(item.ObjectName))
        {
            ObjectDetailsViewModel objectDetails = _objectDetailsFactory.Create(item.ObjectName!);
            _navigationService.NavigateTo(objectDetails);
            return;
        }

        PacketDetailsViewModel vm = _packetDetailsFactory.Create(item.Source);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void Close() => _navigationService.GoBack();

    partial void OnResultsChanged(ObservableCollection<SearchResultItem> value)
    {
        HasResults = Results.Count > 0;
    }
}

/// <summary>
/// A single search result for display in the search list.
/// </summary>
/// <param name="Name">The display name.</param>
/// <param name="Kind">The localized result kind.</param>
/// <param name="Source">The callsign that reported or sent the match.</param>
/// <param name="Subtitle">An optional secondary line.</param>
/// <param name="ObjectName">
/// The object identity when the match is an object, so opening the result shows the
/// object's own report history instead of the reporting station's.
/// </param>
public sealed record SearchResultItem(
    string Name,
    string Kind,
    string Source,
    string? Subtitle,
    string? ObjectName = null);

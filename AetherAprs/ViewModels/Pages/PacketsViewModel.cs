// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Packets;
using AetherAprs.Services.UI;
using AetherAprs.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using AetherAprs.Localization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for the Packets page showing the most recent packet per callsign,
/// object, and item. Supports instant client-side search and lazy-load pagination.
/// </summary>
public partial class PacketsViewModel : ViewModelBase, IDisposable
{
    private const int PageSize = 50;

    private readonly IPacketQueryService _packetQueryService;
    private readonly IPacketStorageService _packetStorageService;
    private readonly INavigationService _navigationService;
    private readonly IPacketDetailsViewModelFactory _packetDetailsFactory;
    private readonly IObjectDetailsViewModelFactory _objectDetailsFactory;
    private readonly IConfigurationService _configurationService;
    private readonly IPacketViewFilterService _filterService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<PacketsViewModel> _logger;
    private readonly ObjectsViewModel _objectsViewModel;
    private readonly SearchViewModel _searchViewModel;
    private readonly CancellationTokenSource _throttleCts = new();
    private readonly List<PacketSummary> _allSummaries = [];
    private readonly List<PacketSummary> _filteredSummaries = [];
    private Task? _throttleTask;
    private bool _disposed;
    private readonly Task? _initializationTask;
    private int _visibleCount;

    [ObservableProperty]
    public partial string Title { get; set; } = Strings.Get("Packets");

    /// <summary>
    /// Gets or sets the current search query text.
    /// When set, triggers client-side filtering of already-loaded packets.
    /// </summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value ?? string.Empty;
                OnPropertyChanged(nameof(SearchQuery));
                ApplyFilterAndReset();
            }
        }
    }
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<PacketSummary> Packets { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasAnyPackets { get; set; }

    /// <summary>
    /// True when packets exist in the database but none match the current search
    /// query (drives the "no packets found" empty state).
    /// </summary>
    [ObservableProperty]
    public partial bool HasNoVisiblePackets { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    /// <summary>
    /// Task that completes when initial load finishes. Useful for testing.
    /// </summary>
    public Task InitializationTask => _initializationTask ?? Task.CompletedTask;

    public PacketsViewModel(
        IPacketQueryService packetQueryService,
        IPacketStorageService packetStorageService,
        INavigationService navigationService,
        IPacketDetailsViewModelFactory packetDetailsFactory,
        IObjectDetailsViewModelFactory objectDetailsFactory,
        IConfigurationService configurationService,
        IPacketViewFilterService filterService,
        IUiDispatcher uiDispatcher,
        ILogger<PacketsViewModel> logger,
        ObjectsViewModel objectsViewModel,
        SearchViewModel searchViewModel)
    {
        _packetQueryService = packetQueryService;
        _packetStorageService = packetStorageService;
        _navigationService = navigationService;
        _packetDetailsFactory = packetDetailsFactory;
        _objectDetailsFactory = objectDetailsFactory;
        _configurationService = configurationService;
        _filterService = filterService;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
        _objectsViewModel = objectsViewModel;
        _searchViewModel = searchViewModel;

        // Subscribe to filter changes (from filter service) and packet storage events
        _filterService.FiltersChanged += OnFiltersChanged;
        _packetStorageService.PacketStored += OnPacketStored;

        // Load initial data from database
        _initializationTask = LoadPacketsAsync();
    }

    private void OnFiltersChanged(object? sender, EventArgs e) =>
        // Trigger a reload when filters change
        _uiDispatcher.Post(() => _ = LoadPacketsAsync());

    private void OnPacketStored(object? sender, EventArgs e)
    {
        // Throttle updates to avoid overwhelming the UI thread
        // Only start a new throttle task if one isn't already running
        if (_throttleTask == null || _throttleTask.IsCompleted)
        {
            _throttleTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, _throttleCts.Token);
                    _uiDispatcher.Post(() => _ = LoadPacketsAsync());
                }
                catch (OperationCanceledException)
                {
                    // Expected during disposal
                }
            }, _throttleCts.Token);
        }
    }

    private async Task LoadPacketsAsync()
    {
        try
        {
            IsLoading = true;

            // Get the time cutoff from the shared filter service
            // (respects LoadHistoryOnStartup, time range, and session start)
            DateTimeOffset? cutoffTime = _filterService.GetCutoffTime();

            // Load most recent station packets, objects, and items. Object/item
            // reports are excluded from the per-callsign list (they belong to the
            // named report, not the reporting station) and loaded separately so they
            // appear under their own name.
            IEnumerable<PacketRecord> stationPackets = (await _packetQueryService.GetMostRecentPacketsAsync())
                .Values
                .Where(r => r.Object == null && r.Item == null);
            IReadOnlyList<PacketRecord> objectPackets = await _packetQueryService.GetMostRecentObjectsAsync();
            IReadOnlyList<PacketRecord> itemPackets = await _packetQueryService.GetMostRecentItemsAsync();

            List<PacketRecord> all = [.. stationPackets];
            all.AddRange(objectPackets);
            all.AddRange(itemPackets);

            // Apply time filter if not "All"
            IEnumerable<PacketRecord> filtered = all;
            if (cutoffTime.HasValue)
            {
                filtered = filtered.Where(r => new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value);
            }

            // Sort by most recent first
            List<PacketRecord> packets = [.. filtered.OrderByDescending(r => r.Id)];

            _logger.LogInformation("Loaded {Count} packets from database with time filter", packets.Count);

            // Build the list of summaries
            _allSummaries.Clear();
            foreach (PacketRecord record in packets)
            {
                _allSummaries.Add(CreateSummary(record));
            }

            // Re-apply the current search filter and reset pagination.
            ApplyFilterAndReset();
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

    private static PacketSummary CreateSummary(PacketRecord record)
    {
        bool isObject = record.Object != null || record.Item != null;
        string source = isObject
            ? (record.Object?.Name ?? record.Item?.Name ?? string.Empty)
            : AprsSsidExtensions.FormatCallsign(record.SourceBase, record.SourceSsid);

        return new PacketSummary
        {
            Source = source,
            PacketType = record.PacketType,
            // Convert UTC timestamp to local time for display
            ReceivedAt = new DateTimeOffset(record.Timestamp, TimeSpan.Zero).ToLocalTime(),
            Preview = GetPacketPreview(record),
            IsObject = isObject
        };
    }

    private static string GetPacketPreview(PacketRecord record)
    {
        if (record.Position != null)
        {
            return Strings.Format("PreviewLatLon",
                record.Position.Location.Latitude.ToString("F4", CultureInfo.InvariantCulture),
                record.Position.Location.Longitude.ToString("F4", CultureInfo.InvariantCulture));
        }

        if (record.Object != null)
        {
            return string.IsNullOrEmpty(record.Object.Comment)
                ? Strings.Format("PreviewLatLon",
                    record.Object.Location.Latitude.ToString("F4", CultureInfo.InvariantCulture),
                    record.Object.Location.Longitude.ToString("F4", CultureInfo.InvariantCulture))
                : record.Object.Comment;
        }

        if (record.Item != null)
        {
            return string.IsNullOrEmpty(record.Item.Comment)
                ? Strings.Format("PreviewLatLon",
                    record.Item.Location.Latitude.ToString("F4", CultureInfo.InvariantCulture),
                    record.Item.Location.Longitude.ToString("F4", CultureInfo.InvariantCulture))
                : record.Item.Comment;
        }

        if (record.Message != null)
        {
            string addressee = AprsSsidExtensions.FormatCallsign(record.Message.AddresseeBase, record.Message.AddresseeSsid);
            return Strings.Format("PreviewMessageTo", addressee, record.Message.Text);
        }

        if (record.Status != null)
        {
            return record.Status.Text;
        }

        if (record.Weather?.Temperature != null)
        {
            return Strings.Format("PreviewTemperature",
                record.Weather.Temperature.Value.ToString("F1", CultureInfo.InvariantCulture));
        }

        return record.RawInfo != null && record.RawInfo.Length > 40 ? record.RawInfo[..40] + "..." : record.RawInfo ?? "";
    }

    /// <summary>
    /// Recomputes the filtered list from the current search query and resets the
    /// pagination to the first page.
    /// </summary>
    private void ApplyFilterAndReset()
    {
        RebuildFiltered();
        _visibleCount = Math.Min(PageSize, _filteredSummaries.Count);
        HasAnyPackets = _allSummaries.Count > 0;
        HasNoVisiblePackets = _allSummaries.Count > 0 && _filteredSummaries.Count == 0;
        HasMore = _visibleCount < _filteredSummaries.Count;

        Packets.Clear();
        for (int index = 0; index < _visibleCount; index++)
        {
            Packets.Add(_filteredSummaries[index]);
        }
    }

    private void RebuildFiltered()
    {
        string query = SearchQuery?.Trim() ?? string.Empty;
        _filteredSummaries.Clear();

        if (query.Length == 0)
        {
            _filteredSummaries.AddRange(_allSummaries);
            return;
        }

        foreach (PacketSummary summary in _allSummaries)
        {
            if (MatchesSearch(summary, query))
            {
                _filteredSummaries.Add(summary);
            }
        }
    }

    private static bool MatchesSearch(PacketSummary summary, string query) => (summary.Source?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (summary.Preview?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Appends the next page of summaries to the visible collection. Invoked by the
    /// page when the scroll position reaches the bottom.
    /// </summary>
    [RelayCommand]
    private void LoadMore()
    {
        if (!HasMore)
        {
            return;
        }

        int next = Math.Min(_visibleCount + PageSize, _filteredSummaries.Count);
        for (int index = _visibleCount; index < next; index++)
        {
            Packets.Add(_filteredSummaries[index]);
        }

        _visibleCount = next;
        HasMore = _visibleCount < _filteredSummaries.Count;
    }

    [RelayCommand]
    private void OpenPacketDetails(PacketSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        // Objects and items open the object details page; stations open packet
        // details. Both are keyed by the summary's Source (object name or callsign).
        if (summary.IsObject)
        {
            ObjectDetailsViewModel objectVm = _objectDetailsFactory.Create(summary.Source);
            _navigationService.NavigateTo(objectVm);
            return;
        }

        PacketDetailsViewModel vm = _packetDetailsFactory.Create(summary.Source);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void OpenObjects() => _navigationService.NavigateTo(_objectsViewModel);

    [RelayCommand]
    private void OpenSearch() => _navigationService.NavigateTo(_searchViewModel);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_disposed)
        {
            return;
        }

        _throttleCts.Cancel();
        _throttleCts.Dispose();
        _filterService.FiltersChanged -= OnFiltersChanged;
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

    /// <summary>
    /// True when this summary represents an object/item report (keyed by its name)
    /// rather than a station (keyed by its callsign).
    /// </summary>
    [ObservableProperty]
    public partial bool IsObject { get; set; }
}

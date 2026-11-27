// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Entities;
using AetherAprs.Localization;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Packets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for the object/item details page showing the named report's current
/// state (name, live/killed, symbol, location, comment) and its report history.
/// </summary>
public partial class ObjectDetailsViewModel(
    IPacketQueryService packetQueryService,
    ILogger<ObjectDetailsViewModel>? logger = null) : ViewModelBase
{
    private const int PageSize = 50;

    private readonly IPacketQueryService _packetQueryService = packetQueryService ?? throw new ArgumentNullException(nameof(packetQueryService));
    private readonly ILogger<ObjectDetailsViewModel>? _logger = logger;

    // An object that reports for months has thousands of rows. Load one page at a
    // time, newest first, using the (timestamp, id) pair of the oldest loaded row as
    // a cursor so a shared timestamp never repeats or skips a report.
    private PacketPageCursor? _pageCursor;
    private bool _isLoadingPage;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Title { get; set; } = Strings.Get("ObjectDetailsTitle");

    [ObservableProperty]
    public partial bool IsLive { get; set; }

    [ObservableProperty]
    public partial bool IsObject { get; set; } = true;

    [ObservableProperty]
    public partial string KindText { get; set; } = Strings.Get("PacketTypeObject");

    [ObservableProperty]
    public partial string SymbolText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLocation { get; set; }

    [ObservableProperty]
    public partial string LocationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasComment { get; set; }

    [ObservableProperty]
    public partial string Comment { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<PacketRecord> Reports { get; set; } = [];

    public bool HasReports => Reports.Count > 0;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// True while older reports remain, so the page can request the next page when the
    /// list reaches its end.
    /// </summary>
    [ObservableProperty]
    public partial bool HasMore { get; set; }

    public void Initialize(string name)
    {
        Name = name;
        Title = Strings.Format("ObjectReportsFrom", name);

        HasMore = true;
        _ = LoadNextPageAsync();
    }

    /// <summary>
    /// Appends the next page of reports. Invoked by the page when the list reaches its end.
    /// </summary>
    [RelayCommand]
    private Task LoadMoreAsync() => LoadNextPageAsync();

    private async Task LoadNextPageAsync()
    {
        if (_isLoadingPage || !HasMore)
        {
            return;
        }

        _isLoadingPage = true;
        IsLoading = true;
        try
        {
            IReadOnlyList<PacketRecord> reports = await _packetQueryService
                .GetPacketsByObjectNamePageAsync(Name, _pageCursor, PageSize);

            // The current state belongs to the newest report, which is only in the
            // first page; later pages append history and never rewrite the header.
            bool isFirstPage = Reports.Count == 0;

            PacketPageCursor? oldest = null;
            foreach (PacketRecord report in reports)
            {
                Reports.Add(report);
                PacketPageCursor candidate = PacketPageCursor.FromPacket(report);
                if (oldest is not { } current || IsOlder(candidate, current))
                {
                    oldest = candidate;
                }
            }

            if (oldest is { } cursor)
            {
                _pageCursor = cursor;
            }

            if (isFirstPage)
            {
                ApplyCurrentState(reports.Count > 0 ? reports[0] : null);
            }

            HasMore = reports.Count == PageSize;

            _logger?.LogInformation("Loaded {Count} reports for object/item {Name}", reports.Count, Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load reports for object/item {Name}", Name);
            HasMore = false;
        }
        finally
        {
            _isLoadingPage = false;
            OnPropertyChanged(nameof(HasReports));
            IsLoading = false;
        }
    }

    private void ApplyCurrentState(PacketRecord? latest)
    {
        IsLive = false;
        HasLocation = false;
        HasComment = false;
        LocationText = string.Empty;
        Comment = string.Empty;
        SymbolText = string.Empty;

        if (latest is null)
        {
            return;
        }

        if (latest.Object is { } obj)
        {
            IsObject = true;
            KindText = Strings.Get("PacketTypeObject");
            IsLive = obj.IsLive;
            SymbolText = $"{obj.SymbolTable.ToChar()}{obj.SymbolCode.ToChar()}";
            HasLocation = true;
            LocationText = Strings.Format("PreviewLatLon",
                obj.Location.Latitude.ToString("F4", CultureInfo.InvariantCulture),
                obj.Location.Longitude.ToString("F4", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(obj.Comment))
            {
                HasComment = true;
                Comment = obj.Comment!;
            }
        }
        else if (latest.Item is { } item)
        {
            IsObject = false;
            KindText = Strings.Get("PacketTypeItem");
            IsLive = item.IsLive;
            SymbolText = $"{item.SymbolTable.ToChar()}{item.SymbolCode.ToChar()}";
            HasLocation = true;
            LocationText = Strings.Format("PreviewLatLon",
                item.Location.Latitude.ToString("F4", CultureInfo.InvariantCulture),
                item.Location.Longitude.ToString("F4", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(item.Comment))
            {
                HasComment = true;
                Comment = item.Comment!;
            }
        }
    }

    /// <summary>
    /// Ordering helper mirroring the query: a row is older when its receipt time is
    /// earlier, or when the timestamps match and its id is smaller.
    /// </summary>
    private static bool IsOlder(PacketPageCursor candidate, PacketPageCursor current) =>
        candidate.Timestamp < current.Timestamp ||
        (candidate.Timestamp == current.Timestamp && candidate.Id < current.Id);
}

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
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for the object/item details page showing the named report's current
/// state (name, live/killed, symbol, location, comment) and its report history.
/// </summary>
public partial class ObjectDetailsViewModel : ViewModelBase
{
    private readonly IPacketQueryService _packetQueryService;
    private readonly ILogger<ObjectDetailsViewModel>? _logger;

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

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public ObjectDetailsViewModel(
        IPacketQueryService packetQueryService,
        ILogger<ObjectDetailsViewModel>? logger = null)
    {
        _packetQueryService = packetQueryService ?? throw new ArgumentNullException(nameof(packetQueryService));
        _logger = logger;
    }

    public void Initialize(string name)
    {
        Name = name;
        Title = Strings.Format("ObjectReportsFrom", name);

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            IsLoading = true;

            var reports = await _packetQueryService.GetPacketsByObjectNameAsync(Name, 500);

            Reports.Clear();
            foreach (var report in reports)
            {
                Reports.Add(report);
            }

            ApplyCurrentState(reports.Count > 0 ? reports[0] : null);

            _logger?.LogInformation("Loaded {Count} reports for object/item {Name}", reports.Count, Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load reports for object/item {Name}", Name);
        }
        finally
        {
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
}

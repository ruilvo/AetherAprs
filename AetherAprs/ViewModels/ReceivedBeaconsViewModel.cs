// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.Configuration;
using AetherAprs.Data;
using Avalonia;
using Avalonia.Threading;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Nts;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels;

/// <summary>
/// Manages the display of received APRS beacons on a Mapsui map layer.
/// Uses the shared PacketCacheService and updates features with APRS symbols.
/// Port ShowOnMap is a visual filter only.
/// </summary>
public sealed class ReceivedBeaconsViewModel : IDisposable
{
    private readonly IPortService _portService;
    private readonly IPacketCacheService _packetCacheService;
    private readonly IConfigurationService _configurationService;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly AprsSymbolMapConverter _symbolConverter;
    private readonly ILogger<ReceivedBeaconsViewModel> _logger;
    private readonly WritableLayer _beaconsLayer;
    private readonly WritableLayer _trailsLayer;
    private readonly Dictionary<string, (Symbol symbol, ImageStyle imageStyle)> _symbolStyleCache = new();
    private bool _disposed;

    /// <summary>
    /// Gets the layer containing all received beacon features.
    /// Returns ILayer to prevent external modification of the internal WritableLayer.
    /// </summary>
    public ILayer BeaconsLayer => _beaconsLayer;

    /// <summary>
    /// Gets the layer containing position trails.
    /// </summary>
    public ILayer TrailsLayer => _trailsLayer;

    public ReceivedBeaconsViewModel(
        IPortService portService,
        IPacketCacheService packetCacheService,
        IConfigurationService configurationService,
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAprsSymbolBitmapProvider symbolBitmapProvider,
        ILogger<ReceivedBeaconsViewModel> logger)
    {
        _portService = portService ?? throw new ArgumentNullException(nameof(portService));
        _packetCacheService = packetCacheService ?? throw new ArgumentNullException(nameof(packetCacheService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _symbolConverter = new AprsSymbolMapConverter(
            symbolBitmapProvider ?? throw new ArgumentNullException(nameof(symbolBitmapProvider)));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _beaconsLayer = new WritableLayer
        {
            Name = "Received Beacons",
            // Mapsui layers default to a white VectorStyle with a grey outline.
            Style = null
        };

        _trailsLayer = new WritableLayer
        {
            Name = "Position Trails",
            Style = null
        };

        _packetCacheService.CacheUpdated += OnCacheUpdated;
        _portService.PortsChanged += OnPortsChanged;
    }

    private void OnCacheUpdated(object? sender, PacketCacheUpdatedEventArgs e)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (e.UpdatedPacket.Packet is not PositionPacket)
        {
            _logger.LogDebug(
                "Ignoring non-position packet Type={PacketType}",
                e.UpdatedPacket.Packet.GetType().Name);
            return;
        }

        RunOnUi(() => _ = RebuildVisibleLayerAsync());
    }

    private void OnPortsChanged(object? sender, EventArgs e)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RunOnUi(() => _ = RebuildVisibleLayerAsync());
    }

    private static void RunOnUi(Action action)
    {
        // Unit tests have no Avalonia application; apply immediately.
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private async Task RebuildVisibleLayerAsync()
    {
        try
        {
            var visiblePortIds = _portService.Ports
                .Where(p => p.ShowOnMap)
                .Select(p => p.Id)
                .ToHashSet();

            // Get time range filter from configuration
            var timeRange = _configurationService.Settings.Aprs.DisplayTimeRange;
            var customHours = _configurationService.Settings.Aprs.CustomDisplayTimeRangeHours;
            var cutoffTime = GetCutoffTime(timeRange, customHours);

            // Query database for position packets within time range
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            
            var query = dbContext.Packets
                .AsNoTracking()
                .Where(p => p.Latitude != null && p.Longitude != null);

            // Apply time filter
            if (cutoffTime.HasValue)
            {
                query = query.Where(p => p.ReceivedAt >= cutoffTime.Value);
            }

            // Apply port filter - only include non-null PortIds that are in visiblePortIds
            if (visiblePortIds.Any())
            {
                query = query.Where(p => p.PortId.HasValue && visiblePortIds.Contains(p.PortId.Value));
            }

            var packets = await query.ToListAsync();

            // Group by source to get most recent position for markers
            var latestBySource = packets
                .GroupBy(p => p.Source)
                .Select(g => g.OrderByDescending(p => p.ReceivedAt).First())
                .ToList();

            // Clear layers
            _beaconsLayer.Clear();
            _trailsLayer.Clear();

            // Add markers for latest positions
            foreach (var record in latestBySource)
            {
                if (record.Latitude.HasValue && record.Longitude.HasValue)
                {
                    _logger.LogDebug(
                        "Adding map beacon for {Callsign} at Lat={Latitude:F5}, Lon={Longitude:F5}",
                        record.Source,
                        record.Latitude,
                        record.Longitude);

                    var feature = CreateFeature(record);
                    _beaconsLayer.Add(feature);
                }
            }

            // Add trails for each station
            foreach (var source in latestBySource.Select(p => p.Source))
            {
                var trail = packets
                    .Where(p => p.Source == source && p.Latitude.HasValue && p.Longitude.HasValue)
                    .OrderBy(p => p.ReceivedAt)
                    .ToList();

                if (trail.Count > 1)
                {
                    var trailFeature = CreateTrailFeature(trail);
                    if (trailFeature != null)
                    {
                        _trailsLayer.Add(trailFeature);
                    }
                }
            }

            _beaconsLayer.DataHasChanged();
            _trailsLayer.DataHasChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild map layers");
        }
    }

    private static DateTimeOffset? GetCutoffTime(PacketDisplayTimeRange timeRange, int customHours)
    {
        var now = DateTimeOffset.UtcNow;
        return timeRange switch
        {
            PacketDisplayTimeRange.LastHour => now.AddHours(-1),
            PacketDisplayTimeRange.LastDay => now.AddDays(-1),
            PacketDisplayTimeRange.LastWeek => now.AddDays(-7),
            PacketDisplayTimeRange.LastMonth => now.AddDays(-30),
            PacketDisplayTimeRange.Custom => now.AddHours(-customHours),
            PacketDisplayTimeRange.All => null,
            _ => now.AddDays(-1) // Default to last day
        };
    }

    private GeometryFeature? CreateTrailFeature(List<PacketRecord> trail)
    {
        if (trail.Count < 2)
        {
            return null;
        }

        var coordinates = trail
            .Where(p => p.Latitude.HasValue && p.Longitude.HasValue)
            .Select(p =>
            {
                var (x, y) = SphericalMercator.FromLonLat(p.Longitude!.Value, p.Latitude!.Value);
                return new Coordinate(x, y);
            })
            .ToArray();

        if (coordinates.Length < 2)
        {
            return null;
        }

        var lineString = new LineString(coordinates);
        var feature = new GeometryFeature { Geometry = lineString };
        
        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(Color.FromArgb(200, 0, 120, 215), 2)
        });

        return feature;
    }

    private PointFeature CreateFeature(PacketRecord record)
    {
        var (x, y) = SphericalMercator.FromLonLat(
            record.Longitude!.Value,
            record.Latitude!.Value);
        var mapPoint = new MPoint(x, y);

        // Get symbol from record or use default
        var symbolTable = record.SymbolTable ?? "/";
        var symbolCode = record.SymbolCode ?? "[";
        var symbol = new Symbol(symbolTable[0].ToSymbolTable(), symbolCode[0].ToSymbolCode());

        var symbolKey = $"{symbolTable}{symbolCode}";
        if (!_symbolStyleCache.TryGetValue(symbolKey, out var cachedStyle) ||
            cachedStyle.symbol != symbol)
        {
            var imageStyle = _symbolConverter.CreateImageStyle(symbol, scale: 0.25);
            cachedStyle = (symbol, imageStyle);
            _symbolStyleCache[symbolKey] = cachedStyle;
        }

        var labelStyle = new LabelStyle
        {
            Text = record.Source,
            Offset = new Offset(45, 0),
            Font = new Font { FontFamily = "Arial", Size = 14, Bold = true },
            ForeColor = Color.Black,
            BackColor = new Brush(new Color(255, 255, 255, 200)),
            Halo = new Pen(Color.White, 2)
        };

        return new PointFeature(mapPoint)
        {
            Styles = [cachedStyle.imageStyle, labelStyle],
            ["Callsign"] = record.Source // Store for click handling
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _packetCacheService.CacheUpdated -= OnCacheUpdated;
        _portService.PortsChanged -= OnPortsChanged;
        _beaconsLayer.Clear();
        _trailsLayer.Clear();
        _symbolStyleCache.Clear();
        _symbolConverter.Dispose();
    }
}

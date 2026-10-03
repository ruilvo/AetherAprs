// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text.Json.Serialization;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Configuration.Settings;

public class AprsSettings
{
    // Backing fields for properties with validation
    private AprsSsid _ssid = AprsSsid.PrimaryStation;
    private SymbolTable _symbolTable = SymbolTable.Primary;
    private SymbolCode _symbolCode = SymbolCode.LeftSquareBracket;
    private SymbolCode? _symbolOverlay;
    private string _callsign = "N0CALL";

    // Constants for validation
    private readonly int _maxCallsignLength = 6;

    /// <summary>
    /// Gets or sets the APRS callsign (without SSID).
    /// Must be 1-6 alphanumeric characters.
    /// </summary>
    public string Callsign
    {
        get => _callsign;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Callsign cannot be empty.",
                    nameof(value)
                );
            }

            if (value.Length > _maxCallsignLength)
            {
                throw new ArgumentException("Callsign cannot exceed "
                    + $"{_maxCallsignLength} characters.",
                    nameof(value)
                );
            }

            // Basic alphanumeric validation
            // (APRS callsigns are typically alphanumeric)
            foreach (var ch in value)
            {
                if (!char.IsLetterOrDigit(ch))
                {
                    throw new ArgumentException("Callsign contains invalid "
                        + $"character '{ch}'. "
                        + "Only alphanumeric characters are allowed.",
                        nameof(value)
                    );
                }
            }

            _callsign = value.ToUpperInvariant();
        }
    }

    /// <summary>
    /// Gets or sets the station SSID.
    /// </summary>
    public AprsSsid Ssid
    {
        get => _ssid;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "SSID must be between 0 and 15."
                );
            }
            _ssid = value;
        }
    }

    /// <summary>
    /// Gets or sets the station APRS symbol table.
    /// </summary>
    [JsonPropertyName("SymbolTableCharacter")]
    public SymbolTable SymbolTable
    {
        get => _symbolTable;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Symbol table must be '/' or '\\'."
                );
            }

            _symbolTable = value;
        }
    }

    /// <summary>
    /// Gets or sets the station APRS symbol code.
    /// </summary>
    [JsonPropertyName("SymbolCodeCharacter")]
    public SymbolCode SymbolCode
    {
        get => _symbolCode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Symbol code must be printable ASCII (33-126)."
                );
            }

            _symbolCode = value;
        }
    }

    /// <summary>
    /// Gets or sets the station APRS symbol overlay.
    /// Null means that no overlay is configured.
    /// </summary>
    [JsonPropertyName("SymbolOverlayCharacter")]
    public SymbolCode? SymbolOverlay
    {
        get => _symbolOverlay;
        set
        {
            if (value.HasValue && !Enum.IsDefined(typeof(SymbolCode), value.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Symbol overlay must be printable ASCII (33-126)."
                );
            }

            _symbolOverlay = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of days to retain received APRS packets
    /// in the database.
    /// Packets older than this will be automatically deleted.
    /// Default is 30 days.
    /// </summary>
    public int PacketRetentionDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets the time range for displaying packets in the list and on
    /// the map.
    /// Default is LastDay (24 hours).
    /// </summary>
    public PacketDisplayTimeRange DisplayTimeRange { get; set; } = PacketDisplayTimeRange.LastDay;

    /// <summary>
    /// Gets or sets the custom time range in hours when DisplayTimeRange
    /// is set to Custom.
    /// Default is 12 hours.
    /// </summary>
    public int CustomDisplayTimeRangeHours { get; set; } = 12;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for unacknowledged
    /// messages.
    /// Default is 5.
    /// </summary>
    public int MessageMaxRetries { get; set; } = 5;

    /// <summary>
    /// Gets or sets the initial retry timeout in seconds for unacknowledged
    /// messages.
    /// Default is 30 seconds.
    /// </summary>
    public int MessageRetryTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether to automatically send ACK/REJ for incoming
    /// messages.
    /// Default is true.
    /// </summary>
    public bool AutoAcknowledgeMessages { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable digipeater functionality.
    /// When enabled, packets received on one port can be retransmitted on
    /// other ports.
    /// Default is false.
    /// </summary>
    public bool EnableDigipeater { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to add the station callsign to the path when
    /// digipeating.
    /// Default is true.
    /// </summary>
    public bool DigipeaterInsertCallsign { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the digipeater responds to WIDE1-1
    /// (fill-in digi mode).
    /// Fill-in digis provide local coverage in areas with weak signal.
    /// Default is false.
    /// </summary>
    public bool DigipeaterRespondToWide1 { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the digipeater responds to WIDE2-N
    /// (full digi mode).
    /// Full digis provide wider area coverage.
    /// Default is true.
    /// </summary>
    public bool DigipeaterRespondToWide2 { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable APRS-IS to RF gating.
    /// When enabled, packets received from APRS-IS can be transmitted on
    /// RF ports.
    /// Default is false (requires careful consideration due to potential
    /// flooding).
    /// </summary>
    public bool EnableAprsIsToRfGate { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable RF to APRS-IS gating.
    /// When enabled, packets received from RF ports can be transmitted to
    /// APRS-IS.
    /// Default is true.
    /// </summary>
    public bool EnableRfToAprsIsGate { get; set; } = true;

    /// <summary>
    /// Gets or sets the comment for position beacons.
    /// Maximum length is 43 characters (APRS spec limit).
    /// </summary>
    public string? BeaconComment { get; set; } = "Using AetherAPRS!";

    /// <summary>
    /// Gets or sets the digipeater path for transmitted packets.
    /// Common values: "WIDE1-1,WIDE2-1" (recommended), "WIDE2-2", or empty
    /// for direct only.
    /// Format: comma-separated list of callsign-SSID pairs.
    /// </summary>
    public string? DigipeaterPath { get; set; } = "WIDE1-1,WIDE2-1";
}

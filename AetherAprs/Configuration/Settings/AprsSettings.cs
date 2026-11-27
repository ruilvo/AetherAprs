// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text.Json.Serialization;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Configuration.Settings;

/// <summary>
/// Station identity and APRS behavior. Every member is read-only after
/// construction, so the snapshot published by
/// <see cref="Services.Configuration.IConfigurationService.Current"/> is safe to
/// read from any thread.
/// </summary>
public sealed record AprsSettings
{
    private AprsSsid _ssid = AprsSsid.PrimaryStation;
    private SymbolTable _symbolTable = SymbolTable.Primary;
    private SymbolCode _symbolCode = SymbolCode.LeftSquareBracket;
    private SymbolCode? _symbolOverlay;
    private string _callsign = "N0CALL";
    private int _packetRetentionDays = DefaultPacketRetentionDays;
    private int _packetRetentionMaxRows = DefaultPacketRetentionMaxRows;

    /// <summary>
    /// The default number of days of packet history to keep: the 30-day target
    /// the roadmap requires the operator to be able to configure.
    /// </summary>
    public const int DefaultPacketRetentionDays = 30;

    /// <summary>
    /// The default number of packet rows to keep regardless of age.
    /// </summary>
    public const int DefaultPacketRetentionMaxRows = 100_000;

    /// <summary>
    /// Gets the APRS callsign (without SSID).
    /// Must be 1-6 alphanumeric characters.
    /// </summary>
    public string Callsign
    {
        get => _callsign;
        init => _callsign = SettingsValidation.Callsign(value);
    }

    /// <summary>
    /// Gets the station SSID.
    /// </summary>
    public AprsSsid Ssid
    {
        get => _ssid;
        init => _ssid = SettingsValidation.Ssid(value);
    }

    /// <summary>
    /// Gets the station APRS symbol table.
    /// </summary>
    [JsonPropertyName("SymbolTableCharacter")]
    public SymbolTable SymbolTable
    {
        get => _symbolTable;
        init => _symbolTable = SettingsValidation.SymbolTable(value);
    }

    /// <summary>
    /// Gets the station APRS symbol code.
    /// </summary>
    [JsonPropertyName("SymbolCodeCharacter")]
    public SymbolCode SymbolCode
    {
        get => _symbolCode;
        init => _symbolCode = SettingsValidation.SymbolCode(value);
    }

    /// <summary>
    /// Gets the station APRS symbol overlay.
    /// Null means that no overlay is configured.
    /// </summary>
    [JsonPropertyName("SymbolOverlayCharacter")]
    public SymbolCode? SymbolOverlay
    {
        get => _symbolOverlay;
        init => _symbolOverlay = SettingsValidation.SymbolOverlay(value);
    }

    /// <summary>
    /// Gets the maximum number of days to retain received APRS packets in the
    /// database. Packets older than this are automatically deleted.
    /// Default is 30 days.
    /// </summary>
    public int PacketRetentionDays
    {
        get => _packetRetentionDays;
        init => _packetRetentionDays = SettingsValidation.PacketRetentionDays(value);
    }

    /// <summary>
    /// Gets the maximum number of packet records retained after cleanup.
    /// Pending outbound messages are preserved beyond this bound until terminal.
    /// </summary>
    public int PacketRetentionMaxRows
    {
        get => _packetRetentionMaxRows;
        init => _packetRetentionMaxRows = SettingsValidation.PacketRetentionMaxRows(value);
    }

    /// <summary>
    /// Gets the time range for displaying packets in the list and on the map.
    /// Default is LastDay (24 hours).
    /// </summary>
    public PacketDisplayTimeRange DisplayTimeRange { get; init; } = PacketDisplayTimeRange.LastDay;

    /// <summary>
    /// Gets the custom time range in hours when
    /// <see cref="DisplayTimeRange"/> is Custom.
    /// Default is 12 hours.
    /// </summary>
    public int CustomDisplayTimeRangeHours { get; init; } = 12;

    /// <summary>
    /// Gets whether to load historical packets when the app starts.
    /// When true, packets within the configured display time range are shown.
    /// When false, the app starts with an empty map and packet list.
    /// Default is true.
    /// </summary>
    public bool LoadHistoryOnStartup { get; init; } = true;

    /// <summary>
    /// Gets the maximum number of retry attempts for unacknowledged messages,
    /// not counting the initial send.
    /// Default is 3.
    /// </summary>
    public int MessageMaxRetries { get; init; } = 3;

    /// <summary>
    /// Gets the initial retry timeout in seconds for unacknowledged messages.
    /// Default is 30 seconds.
    /// </summary>
    public int MessageRetryTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets how long an outbound message stays eligible for delivery before it
    /// expires. Default is 15 minutes.
    /// </summary>
    public int MessageExpiryMinutes { get; init; } = 15;

    /// <summary>
    /// Gets whether incoming message text is shown in notifications. Disabled by
    /// default so a message body is not readable on a locked screen.
    /// </summary>
    public bool ShowMessageTextInNotifications { get; init; }

    /// <summary>
    /// Gets whether to automatically send ACK/REJ for incoming messages.
    /// Default is true.
    /// </summary>
    public bool AutoAcknowledgeMessages { get; init; } = true;

    /// <summary>
    /// Gets whether digipeater functionality is enabled. When enabled, packets
    /// received on one port can be retransmitted on other ports.
    /// Default is false.
    /// </summary>
    public bool EnableDigipeater { get; init; }

    /// <summary>
    /// Gets whether the station callsign is added to the path when digipeating.
    /// Default is true.
    /// </summary>
    public bool DigipeaterInsertCallsign { get; init; } = true;

    /// <summary>
    /// Gets whether the digipeater responds to WIDE1-1 (fill-in digi mode).
    /// Default is false.
    /// </summary>
    public bool DigipeaterRespondToWide1 { get; init; }

    /// <summary>
    /// Gets whether the digipeater responds to WIDE2-N (full digi mode).
    /// Default is true.
    /// </summary>
    public bool DigipeaterRespondToWide2 { get; init; } = true;

    /// <summary>
    /// Gets whether the digipeater responds to TRACEn-N (trace digi mode).
    /// Default is false.
    /// </summary>
    public bool DigipeaterRespondToTrace { get; init; }

    /// <summary>
    /// Gets whether APRS-IS to RF gating is enabled. When enabled, packets
    /// received from APRS-IS can be transmitted on RF ports.
    /// Default is false.
    /// </summary>
    public bool EnableAprsIsToRfGate { get; init; }

    /// <summary>
    /// Gets the callsign allowlist that limits which APRS-IS sources may be gated to
    /// RF. Entries are comma, semicolon or whitespace separated; an entry without an
    /// SSID matches every SSID of that base. Empty or missing means no restriction,
    /// so every source that passed the other gates may be transmitted on RF.
    /// </summary>
    public string? AprsIsToRfAllowlist { get; init; }

    /// <summary>
    /// Gets whether RF to APRS-IS gating is enabled. When enabled, packets
    /// received from RF ports can be transmitted to APRS-IS.
    /// Default is false: both forwarding directions start off, so nothing leaves
    /// the station until the operator turns the gate on deliberately.
    /// </summary>
    public bool EnableRfToAprsIsGate { get; init; }

    /// <summary>
    /// Gets the comment for position beacons.
    /// Maximum length is 43 characters (APRS spec limit).
    /// </summary>
    public string? BeaconComment { get; init; } = "Using AetherAPRS!";

    /// <summary>
    /// Gets the wire encoding used when transmitting position beacons.
    /// Compressed (base-91) is the default for mobile use; Mic-E additionally
    /// encodes latitude into the AX.25 destination address.
    /// </summary>
    public PositionFormat PositionFormat { get; init; } = PositionFormat.Compressed;

    /// <summary>
    /// Gets the digipeater path for transmitted packets.
    /// Common values: "WIDE1-1,WIDE2-1" (recommended), "WIDE2-2", or empty
    /// for direct only. Format: comma-separated list of callsign-SSID pairs.
    /// </summary>
    public string? DigipeaterPath { get; init; } = "WIDE1-1,WIDE2-1";
}

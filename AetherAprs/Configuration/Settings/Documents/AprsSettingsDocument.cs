// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text.Json.Serialization;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Configuration.Settings.Documents;

/// <summary>
/// Mutable binding target for the <c>Aprs</c> settings section. See
/// <see cref="AppSettingsDocument"/> for why this shape exists.
/// </summary>
internal sealed class AprsSettingsDocument
{
    private AprsSsid _ssid = AprsSsid.PrimaryStation;
    private SymbolTable _symbolTable = SymbolTable.Primary;
    private SymbolCode _symbolCode = SymbolCode.LeftSquareBracket;
    private SymbolCode? _symbolOverlay;
    private string _callsign = "N0CALL";

    public string Callsign
    {
        get => _callsign;
        set => _callsign = SettingsValidation.Callsign(value);
    }

    public AprsSsid Ssid
    {
        get => _ssid;
        set => _ssid = SettingsValidation.Ssid(value);
    }

    [JsonPropertyName("SymbolTableCharacter")]
    public SymbolTable SymbolTable
    {
        get => _symbolTable;
        set => _symbolTable = SettingsValidation.SymbolTable(value);
    }

    [JsonPropertyName("SymbolCodeCharacter")]
    public SymbolCode SymbolCode
    {
        get => _symbolCode;
        set => _symbolCode = SettingsValidation.SymbolCode(value);
    }

    [JsonPropertyName("SymbolOverlayCharacter")]
    public SymbolCode? SymbolOverlay
    {
        get => _symbolOverlay;
        set => _symbolOverlay = SettingsValidation.SymbolOverlay(value);
    }

    public int PacketRetentionDays { get; set; } = 30;

    public int PacketRetentionMaxRows { get; set; } = 100_000;

    public PacketDisplayTimeRange DisplayTimeRange { get; set; } = PacketDisplayTimeRange.LastDay;

    public int CustomDisplayTimeRangeHours { get; set; } = 12;

    public bool LoadHistoryOnStartup { get; set; } = true;

    public int MessageMaxRetries { get; set; } = 5;

    public int MessageRetryTimeoutSeconds { get; set; } = 30;

    public bool AutoAcknowledgeMessages { get; set; } = true;

    public bool EnableDigipeater { get; set; }

    public bool DigipeaterInsertCallsign { get; set; } = true;

    public bool DigipeaterRespondToWide1 { get; set; }

    public bool DigipeaterRespondToWide2 { get; set; } = true;

    public bool DigipeaterRespondToTrace { get; set; }

    public bool EnableAprsIsToRfGate { get; set; }

    public bool EnableRfToAprsIsGate { get; set; } = true;

    public string? BeaconComment { get; set; } = "Using AetherAPRS!";

    public PositionFormat PositionFormat { get; set; } = PositionFormat.Compressed;

    public string? DigipeaterPath { get; set; } = "WIDE1-1,WIDE2-1";

    public AprsSettings ToSettings() => new()
    {
        Callsign = Callsign,
        Ssid = Ssid,
        SymbolTable = SymbolTable,
        SymbolCode = SymbolCode,
        SymbolOverlay = SymbolOverlay,
        PacketRetentionDays = PacketRetentionDays,
        PacketRetentionMaxRows = PacketRetentionMaxRows,
        DisplayTimeRange = DisplayTimeRange,
        CustomDisplayTimeRangeHours = CustomDisplayTimeRangeHours,
        LoadHistoryOnStartup = LoadHistoryOnStartup,
        MessageMaxRetries = MessageMaxRetries,
        MessageRetryTimeoutSeconds = MessageRetryTimeoutSeconds,
        AutoAcknowledgeMessages = AutoAcknowledgeMessages,
        EnableDigipeater = EnableDigipeater,
        DigipeaterInsertCallsign = DigipeaterInsertCallsign,
        DigipeaterRespondToWide1 = DigipeaterRespondToWide1,
        DigipeaterRespondToWide2 = DigipeaterRespondToWide2,
        DigipeaterRespondToTrace = DigipeaterRespondToTrace,
        EnableAprsIsToRfGate = EnableAprsIsToRfGate,
        EnableRfToAprsIsGate = EnableRfToAprsIsGate,
        BeaconComment = BeaconComment,
        PositionFormat = PositionFormat,
        DigipeaterPath = DigipeaterPath,
    };
}

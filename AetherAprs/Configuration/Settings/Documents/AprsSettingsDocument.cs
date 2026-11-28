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
    public string Callsign
    {
        get;
        set => field = SettingsValidation.Callsign(value);
    } = "N0CALL";

    public AprsSsid Ssid
    {
        get;
        set => field = SettingsValidation.Ssid(value);
    } = AprsSsid.PrimaryStation;

    [JsonPropertyName("SymbolTableCharacter")]
    public SymbolTable SymbolTable
    {
        get;
        set => field = SettingsValidation.SymbolTable(value);
    } = SymbolTable.Primary;

    [JsonPropertyName("SymbolCodeCharacter")]
    public SymbolCode SymbolCode
    {
        get;
        set => field = SettingsValidation.SymbolCode(value);
    } = SymbolCode.LeftSquareBracket;

    [JsonPropertyName("SymbolOverlayCharacter")]
    public SymbolCode? SymbolOverlay
    {
        get;
        set => field = SettingsValidation.SymbolOverlay(value);
    }

    public int PacketRetentionDays
    {
        get;
        set => field = SettingsValidation.PacketRetentionDays(value);
    } = AprsSettings.DefaultPacketRetentionDays;

    public int PacketRetentionMaxRows
    {
        get;
        set => field = SettingsValidation.PacketRetentionMaxRows(value);
    } = AprsSettings.DefaultPacketRetentionMaxRows;

    public PacketDisplayTimeRange DisplayTimeRange { get; set; } = PacketDisplayTimeRange.LastDay;

    public int CustomDisplayTimeRangeHours { get; set; } = 12;

    public bool LoadHistoryOnStartup { get; set; } = true;

    public int MessageMaxRetries { get; set; } = 3;

    public int MessageRetryTimeoutSeconds { get; set; } = 30;

    public int MessageExpiryMinutes { get; set; } = 15;

    public bool ShowMessageTextInNotifications { get; set; }

    public bool AutoAcknowledgeMessages { get; set; } = true;

    public bool EnableDigipeater { get; set; }

    public bool DigipeaterInsertCallsign { get; set; } = true;

    public bool DigipeaterRespondToWide1 { get; set; }

    public bool DigipeaterRespondToWide2 { get; set; } = true;

    public bool DigipeaterRespondToTrace { get; set; }

    public bool EnableAprsIsToRfGate { get; set; }

    public string? AprsIsToRfAllowlist { get; set; }

    public bool EnableRfToAprsIsGate { get; set; }

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
        MessageExpiryMinutes = MessageExpiryMinutes,
        ShowMessageTextInNotifications = ShowMessageTextInNotifications,
        AutoAcknowledgeMessages = AutoAcknowledgeMessages,
        EnableDigipeater = EnableDigipeater,
        DigipeaterInsertCallsign = DigipeaterInsertCallsign,
        DigipeaterRespondToWide1 = DigipeaterRespondToWide1,
        DigipeaterRespondToWide2 = DigipeaterRespondToWide2,
        DigipeaterRespondToTrace = DigipeaterRespondToTrace,
        EnableAprsIsToRfGate = EnableAprsIsToRfGate,
        AprsIsToRfAllowlist = AprsIsToRfAllowlist,
        EnableRfToAprsIsGate = EnableRfToAprsIsGate,
        BeaconComment = BeaconComment,
        PositionFormat = PositionFormat,
        DigipeaterPath = DigipeaterPath,
    };
}

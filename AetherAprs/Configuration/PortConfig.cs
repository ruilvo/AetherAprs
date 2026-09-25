// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text.Json.Serialization;

namespace AetherAprs.Configuration;

/// <summary>
/// Base configuration for all port types.
/// </summary>
public class PortConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public bool IsRx { get; set; } = true;

    public bool IsTx { get; set; } = true;

    /// <summary>
    /// When true, position packets received on this port are shown on the map.
    /// History is retained while hidden and restored when re-enabled.
    /// </summary>
    public bool ShowOnMap { get; set; } = true;

    /// <summary>
    /// When true, packets received on this port can be digipeated to other ports.
    /// Requires EnableDigipeater=true in APRS settings.
    /// Default is true.
    /// </summary>
    public bool AllowDigipeat { get; set; } = true;

    /// <summary>
    /// Type-specific configuration. Use AprsIsSettings for APRS-IS ports, KissSettings for KISS ports.
    /// </summary>
    public IPortTypeSettings? TypeSettings { get; set; }
}

/// <summary>
/// Marker interface for port type-specific settings.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AprsIsSettings), "aprs-is")]
[JsonDerivedType(typeof(KissSettings), "kiss")]
public interface IPortTypeSettings
{
}

/// <summary>
/// APRS-IS specific port configuration.
/// </summary>
public class AprsIsSettings : IPortTypeSettings
{
    public const string DefaultServer = "rotate.aprs2.net";
    public const int DefaultServerPort = 14580;
    public const string DefaultPasscode = "-1";
    public const string DefaultFilter = "m/50";

    private string _server = DefaultServer;
    private int _serverPort = DefaultServerPort;
    private string _passcode = DefaultPasscode;
    private string _filter = DefaultFilter;

    /// <summary>
    /// Gets or sets the APRS-IS server hostname.
    /// </summary>
    public string Server
    {
        get => _server;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Server cannot be empty.", nameof(value));
            }
            _server = value;
        }
    }

    /// <summary>
    /// Gets or sets the APRS-IS server port (1-65535).
    /// </summary>
    public int ServerPort
    {
        get => _serverPort;
        set
        {
            if (value < 1 || value > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Server port must be between 1 and 65535.");
            }
            _serverPort = value;
        }
    }

    /// <summary>
    /// Gets or sets the APRS-IS passcode. Use "-1" for read-only access.
    /// </summary>
    public string Passcode
    {
        get => _passcode;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Passcode cannot be empty. Use \"-1\" for read-only access.", nameof(value));
            }
            _passcode = value;
        }
    }

    /// <summary>
    /// Gets or sets the APRS-IS filter string (e.g., "m/50" for 50km radius).
    /// </summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Filter cannot be empty.", nameof(value));
            }
            _filter = value;
        }
    }
}

/// <summary>
/// KISS TNC specific port configuration.
/// </summary>
public class KissSettings : IPortTypeSettings
{
    public IKissTransportSettings? Transport { get; set; }
}
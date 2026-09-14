// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models;

namespace AetherAprs.Configuration;

public class AprsSettings
{
    private string _callsign = "N0CALL";
    private int _defaultSsid = 0;
    private string _defaultSymbolTableCharacter = "/";
    private string _defaultSymbolCodeCharacter = "[";

    /// <summary>
    /// Gets or sets the APRS callsign (without SSID). Must be 1-6 alphanumeric characters.
    /// </summary>
    public string Callsign
    {
        get => _callsign;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Callsign cannot be empty.", nameof(value));
            }

            if (value.Length > 6)
            {
                throw new ArgumentException("Callsign cannot exceed 6 characters.", nameof(value));
            }

            // Basic alphanumeric validation (APRS callsigns are typically alphanumeric)
            foreach (var ch in value)
            {
                if (!char.IsLetterOrDigit(ch))
                {
                    throw new ArgumentException($"Callsign contains invalid character '{ch}'. Only alphanumeric characters are allowed.", nameof(value));
                }
            }

            _callsign = value.ToUpperInvariant();
        }
    }

    /// <summary>
    /// Gets or sets the default SSID (0-15). 0 means no SSID.
    /// </summary>
    public int DefaultSsid
    {
        get => _defaultSsid;
        set
        {
            if (value < 0 || value > 15)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "SSID must be between 0 and 15.");
            }
            _defaultSsid = value;
        }
    }

    /// <summary>
    /// Gets or sets the default APRS symbol table character ('/' or '\').
    /// </summary>
    public string DefaultSymbolTableCharacter
    {
        get => _defaultSymbolTableCharacter;
        set
        {
            if (string.IsNullOrEmpty(value) || value.Length != 1)
            {
                throw new ArgumentException("Symbol table character must be exactly one character.", nameof(value));
            }

            var ch = value[0];
            if (ch != '/' && ch != '\\')
            {
                throw new ArgumentException("Symbol table character must be '/' or '\\'.", nameof(value));
            }

            _defaultSymbolTableCharacter = value;
        }
    }

    /// <summary>
    /// Gets or sets the default APRS symbol code character (printable ASCII 33-126).
    /// </summary>
    public string DefaultSymbolCodeCharacter
    {
        get => _defaultSymbolCodeCharacter;
        set
        {
            if (string.IsNullOrEmpty(value) || value.Length != 1)
            {
                throw new ArgumentException("Symbol code character must be exactly one character.", nameof(value));
            }

            var ch = value[0];
            if (ch < 33 || ch > 126)
            {
                throw new ArgumentException($"Symbol code character must be printable ASCII (33-126), got '{ch}' ({(int)ch}).", nameof(value));
            }

            _defaultSymbolCodeCharacter = value;
        }
    }

    public DynamicBeaconMode DefaultBeaconMode { get; set; } = DynamicBeaconMode.Walk;
}

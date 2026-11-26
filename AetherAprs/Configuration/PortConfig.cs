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

    /// <summary>
    /// Gets or sets a value indicating whether stored credentials for this port could
    /// not be restored from the platform secret store.
    /// </summary>
    /// <remarks>
    /// Runtime state only; it is never persisted. The port keeps its other settings and
    /// stays in the configuration so unrelated ports load normally, and the operator has
    /// to enter the credential again before this port can transmit.
    /// </remarks>
    public bool CredentialsUnavailable { get; set; }

    /// <summary>
    /// Creates an independent copy, including the type-specific settings graph.
    /// </summary>
    /// <remarks>
    /// Published port configuration is a snapshot: owners hand out copies so a
    /// caller cannot mutate state that the service believes it has persisted.
    /// </remarks>
    public PortConfig Clone() => new()
    {
        Id = Id,
        Name = Name,
        IsEnabled = IsEnabled,
        IsRx = IsRx,
        IsTx = IsTx,
        ShowOnMap = ShowOnMap,
        AllowDigipeat = AllowDigipeat,
        TypeSettings = TypeSettings?.Clone(),
        CredentialsUnavailable = CredentialsUnavailable
    };

    /// <summary>Returns a copy that differs only in <see cref="IsEnabled"/>.</summary>
    public PortConfig WithEnabled(bool enabled)
    {
        PortConfig copy = Clone();
        copy.IsEnabled = enabled;
        return copy;
    }

    /// <summary>Returns a copy that differs only in <see cref="ShowOnMap"/>.</summary>
    public PortConfig WithShowOnMap(bool showOnMap)
    {
        PortConfig copy = Clone();
        copy.ShowOnMap = showOnMap;
        return copy;
    }
}

/// <summary>
/// Marker interface for port type-specific settings.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AprsIsSettings), "aprs-is")]
[JsonDerivedType(typeof(KissSettings), "kiss")]
[JsonDerivedType(typeof(AfskSettings), "afsk")]
public interface IPortTypeSettings
{
    /// <summary>Creates an independent copy of these settings.</summary>
    IPortTypeSettings Clone();
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
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Server port must be between 1 and 65535."
                );
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
                throw new ArgumentException(
                    "Passcode cannot be empty. Use \"-1\" for read-only access.",
                    nameof(value)
                );
            }
            _passcode = value;
        }
    }

    /// <summary>
    /// Gets or sets the APRS-IS filter string (e.g., "m/50" for 50km radius).
    /// An empty or whitespace string represents "no filter" (receive everything).
    /// </summary>
    public string Filter
    {
        get => _filter;
        set => _filter = string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }

    /// <inheritdoc />
    public IPortTypeSettings Clone() => new AprsIsSettings
    {
        Server = Server,
        ServerPort = ServerPort,
        Passcode = Passcode,
        Filter = Filter
    };
}

/// <summary>
/// KISS TNC specific port configuration.
/// </summary>
public class KissSettings : IPortTypeSettings
{
    public IKissTransportSettings? Transport { get; set; }

    /// <inheritdoc />
    public IPortTypeSettings Clone() => new KissSettings { Transport = Transport?.Clone() };
}

/// <summary>
/// How the AFSK modem keys the transmitter.
/// </summary>
public enum PttMode
{
    /// <summary>No PTT control; the radio keys itself (VOX).</summary>
    None = 0,

    /// <summary>Toggle the device speaker-mic (voice transmit) routing.</summary>
    SpeakerMic = 1
}

/// <summary>
/// AFSK soundcard-modem specific port configuration.
/// </summary>
public class AfskSettings : IPortTypeSettings
{
    public const PttMode DefaultPttMode = PttMode.None;
    public const int DefaultTxDelayMs = 300;
    public const int DefaultTxTailMs = 50;
    public const int DefaultSlotTimeMs = 100;
    public const int DefaultPersist = 63;
    public const double DefaultAudioLevel = 0.9;
    public const double DefaultSquelchThreshold = 0.1;

    private PttMode _pttMode = DefaultPttMode;
    private int _txDelayMs = DefaultTxDelayMs;
    private int _txTailMs = DefaultTxTailMs;
    private int _slotTimeMs = DefaultSlotTimeMs;
    private int _persist = DefaultPersist;
    private double _audioLevel = DefaultAudioLevel;
    private double _squelchThreshold = DefaultSquelchThreshold;

    /// <summary>
    /// Gets or sets how the transmitter is keyed. Default is <see cref="PttMode.None"/> (VOX).
    /// </summary>
    public PttMode PttMode
    {
        get => _pttMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown PTT mode.");
            }

            _pttMode = value;
        }
    }

    /// <summary>
    /// Gets or sets the transmit delay in milliseconds: the time between keying
    /// PTT and starting audio, allowing the radio to ramp up.
    /// </summary>
    public int TxDelayMs
    {
        get => _txDelayMs;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "TX delay cannot be negative.");
            }

            _txDelayMs = value;
        }
    }

    /// <summary>
    /// Gets or sets the transmit tail in milliseconds: the time audio keeps
    /// flowing after the packet before PTT is released.
    /// </summary>
    public int TxTailMs
    {
        get => _txTailMs;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "TX tail cannot be negative.");
            }

            _txTailMs = value;
        }
    }

    /// <summary>
    /// Gets or sets the channel-access slottime in milliseconds: the base interval
    /// used by the randomized p-persistent backoff before keying the transmitter.
    /// Zero disables the randomized backoff.
    /// </summary>
    public int SlotTimeMs
    {
        get => _slotTimeMs;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Slottime cannot be negative.");
            }

            _slotTimeMs = value;
        }
    }

    /// <summary>
    /// Gets or sets the p-persistence value (0-255) controlling how eagerly the modem
    /// transmits in each slottime. Higher values transmit sooner (255 = immediately).
    /// </summary>
    public int Persist
    {
        get => _persist;
        set
        {
            if (value is < 0 or > 255)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Persist must be in [0, 255].");
            }

            _persist = value;
        }
    }

    /// <summary>
    /// Gets or sets the transmit audio level in the range (0, 1].
    /// </summary>
    public double AudioLevel
    {
        get => _audioLevel;
        set
        {
            if (value is <= 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Audio level must be in (0, 1].");
            }

            _audioLevel = value;
        }
    }

    /// <summary>
    /// Gets or sets the receive squelch threshold as a signal amplitude in the
    /// range [0, 1]. Input below this level is treated as noise. A value of 0
    /// disables squelch.
    /// </summary>
    public double SquelchThreshold
    {
        get => _squelchThreshold;
        set
        {
            if (value is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Squelch threshold must be in [0, 1].");
            }

            _squelchThreshold = value;
        }
    }

    /// <inheritdoc />
    public IPortTypeSettings Clone() => new AfskSettings
    {
        PttMode = PttMode,
        TxDelayMs = TxDelayMs,
        TxTailMs = TxTailMs,
        SlotTimeMs = SlotTimeMs,
        Persist = Persist,
        AudioLevel = AudioLevel,
        SquelchThreshold = SquelchThreshold
    };
}

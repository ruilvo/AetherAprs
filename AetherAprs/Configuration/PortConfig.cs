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
    /// <summary>
    /// Longest port name that fits the persisted column.
    /// </summary>
    public const int MaxNameLength = 200;

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
/// How an APRS-IS connection is secured.
/// </summary>
public enum AprsIsSecurityMode
{
    /// <summary>
    /// Legacy plain-text TCP connection. Credentials and position data travel unencrypted.
    /// </summary>
    Plaintext = 0,

    /// <summary>
    /// TLS connection with normal certificate and hostname validation.
    /// </summary>
    Tls = 1
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
    public const AprsIsSecurityMode DefaultSecurity = AprsIsSecurityMode.Plaintext;

    /// <summary>
    /// Longest server address that fits the persisted column.
    /// </summary>
    public const int MaxServerLength = 255;

    /// <summary>
    /// Longest passcode that fits the persisted column.
    /// </summary>
    public const int MaxPasscodeLength = 32;

    /// <summary>
    /// Longest filter expression that fits the persisted column.
    /// </summary>
    public const int MaxFilterLength = 256;

    /// <summary>
    /// Gets or sets the APRS-IS server hostname.
    /// </summary>
    public string Server
    {
        get;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Server cannot be empty.", nameof(value));
            }
            field = value;
        }
    } = DefaultServer;

    /// <summary>
    /// Gets or sets the APRS-IS server port (1-65535).
    /// </summary>
    public int ServerPort
    {
        get;
        set
        {
            if (value is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Server port must be between 1 and 65535."
                );
            }
            field = value;
        }
    } = DefaultServerPort;

    /// <summary>
    /// Gets or sets the APRS-IS passcode. Use "-1" for read-only access.
    /// </summary>
    public string Passcode
    {
        get;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Passcode cannot be empty. Use \"-1\" for read-only access.",
                    nameof(value)
                );
            }
            field = value;
        }
    } = DefaultPasscode;

    /// <summary>
    /// Gets or sets the APRS-IS filter string (e.g., "m/50" for 50km radius).
    /// An empty or whitespace string represents "no filter" (receive everything).
    /// </summary>
    public string Filter
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    } = DefaultFilter;

    /// <summary>
    /// Gets or sets whether the connection uses TLS. Default is
    /// <see cref="AprsIsSecurityMode.Plaintext"/> for backwards compatibility.
    /// </summary>
    public AprsIsSecurityMode Security
    {
        get;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown APRS-IS security mode.");
            }
            field = value;
        }
    } = DefaultSecurity;

    /// <inheritdoc />
    public IPortTypeSettings Clone() => new AprsIsSettings
    {
        Server = Server,
        ServerPort = ServerPort,
        Passcode = Passcode,
        Filter = Filter,
        Security = Security
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
/// <remarks>
/// The mode states what the operator chose; whether the platform can key a
/// transmitter at all is a separate capability, reported by
/// <see cref="Services.Platform.IPttService.SupportsPhysicalPtt"/>. An operator on a
/// platform without that capability can only select <see cref="Vox"/>.
/// </remarks>
public enum PttMode
{
    /// <summary>
    /// The radio keys itself (VOX); the application does not control PTT.
    /// </summary>
    Vox = 0,

    /// <summary>Toggle the device speaker-mic (voice transmit) routing.</summary>
    SpeakerMic = 1
}

/// <summary>
/// AFSK soundcard-modem specific port configuration.
/// </summary>
public class AfskSettings : IPortTypeSettings
{
    public const PttMode DefaultPttMode = PttMode.Vox;
    public const int DefaultTxDelayMs = 300;
    public const int DefaultTxTailMs = 50;
    public const int DefaultSlotTimeMs = 100;
    public const int DefaultPersist = 63;

    /// <summary>
    /// Gets or sets how the transmitter is keyed. Default is <see cref="PttMode.Vox"/>,
    /// where the radio keys itself.
    /// </summary>
    public PttMode PttMode
    {
        get;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown PTT mode.");
            }

            field = value;
        }
    } = DefaultPttMode;

    /// <summary>
    /// Gets or sets the transmit delay in milliseconds: the time between keying
    /// PTT and starting audio, allowing the radio to ramp up.
    /// </summary>
    public int TxDelayMs
    {
        get;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "TX delay cannot be negative.");
            }

            field = value;
        }
    } = DefaultTxDelayMs;

    /// <summary>
    /// Gets or sets the transmit tail in milliseconds: the time audio keeps
    /// flowing after the packet before PTT is released.
    /// </summary>
    public int TxTailMs
    {
        get;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "TX tail cannot be negative.");
            }

            field = value;
        }
    } = DefaultTxTailMs;

    /// <summary>
    /// Gets or sets the channel-access slottime in milliseconds: the base interval
    /// used by the randomized p-persistent backoff before keying the transmitter.
    /// Zero disables the randomized backoff.
    /// </summary>
    public int SlotTimeMs
    {
        get;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Slottime cannot be negative.");
            }

            field = value;
        }
    } = DefaultSlotTimeMs;

    /// <summary>
    /// Gets or sets the p-persistence value (0-255) controlling how eagerly the modem
    /// transmits in each slottime. Higher values transmit sooner (255 = immediately).
    /// </summary>
    public int Persist
    {
        get;
        set
        {
            if (value is < 0 or > 255)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Persist must be in [0, 255].");
            }

            field = value;
        }
    } = DefaultPersist;

    /// <inheritdoc />
    public IPortTypeSettings Clone() => new AfskSettings
    {
        PttMode = PttMode,
        TxDelayMs = TxDelayMs,
        TxTailMs = TxTailMs,
        SlotTimeMs = SlotTimeMs,
        Persist = Persist
    };
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;

namespace AetherAprs.Transports.Kiss;

/// <summary>
/// Opens a duplex byte stream for a specific KISS transport settings type.
/// </summary>
public interface IKissStreamConnector
{
    Type SettingsType { get; }

    bool IsSupported { get; }

    Task<Stream> ConnectAsync(IKissTransportSettings settings, CancellationToken cancellationToken = default);
}

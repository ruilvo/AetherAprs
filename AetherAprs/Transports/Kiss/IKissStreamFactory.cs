// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Transports.Kiss;

/// <summary>
/// Resolves and opens KISS transport streams from <see cref="KissSettings"/>.
/// </summary>
public interface IKissStreamFactory
{
    /// <summary>
    /// Settings types that have a supported connector registered.
    /// </summary>
    IReadOnlyCollection<Type> SupportedTransports { get; }

    /// <summary>
    /// Opens a duplex stream for the configured KISS transport.
    /// </summary>
    Task<Stream> OpenAsync(KissSettings settings, CancellationToken cancellationToken = default);
}

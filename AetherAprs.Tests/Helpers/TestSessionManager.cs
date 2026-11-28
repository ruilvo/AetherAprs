// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Ports;

using Microsoft.Extensions.Logging.Abstractions;

namespace AetherAprs.Tests.Helpers;

/// <summary>
/// Builds the real session manager around a test modem factory, so tests exercise
/// the same gates, leases and ownership rules as production.
/// </summary>
internal static class TestSessionManager
{
    public static PortSessionManager Create(IModemFactory modemFactory) =>
        new(modemFactory, NullLogger<PortSessionManager>.Instance);
}

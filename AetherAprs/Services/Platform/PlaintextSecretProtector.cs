// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Development and desktop fallback. Android replaces this with Keystore protection.
/// </summary>
/// <remarks>
/// This protector is intentionally a no-op, so it never pretends to protect a secret:
/// <see cref="IsProtected"/> always returns <see langword="false"/> and lets the
/// load-time migration path re-write the value. The constructor logs a warning so
/// an operator can tell that credentials on this host are stored in cleartext.
/// </remarks>
public sealed class PlaintextSecretProtector : ISecretProtector
{
    public PlaintextSecretProtector(ILogger<PlaintextSecretProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger.LogWarning(
            "No platform secret protector is registered; APRS-IS passcodes will be stored in cleartext. " +
            "This fallback is intended for development and desktop hosts only.");
    }

    public bool IsProtected(string value) => false;

    public string Protect(string secret) => secret;

    public string Unprotect(string protectedSecret) => protectedSecret;
}

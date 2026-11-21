// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// Development and desktop fallback. Android replaces this with Keystore protection.
/// </summary>
public sealed class PlaintextSecretProtector : ISecretProtector
{
    public bool IsProtected(string value) => false;

    public string Protect(string secret) => secret;

    public string Unprotect(string protectedSecret) => protectedSecret;
}

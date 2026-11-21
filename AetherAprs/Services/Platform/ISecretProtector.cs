// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// Protects secrets before persistence and restores them for runtime use.
/// </summary>
public interface ISecretProtector
{
    bool IsProtected(string value);

    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

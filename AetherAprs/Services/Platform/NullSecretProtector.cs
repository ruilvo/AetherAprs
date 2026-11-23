// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Fail-closed protector for hosts where secret storage is impossible.
/// </summary>
/// <remarks>
/// Register this only for targets that never persist credentials, such as tests
/// that do not touch passcodes. Any attempt to protect or unprotect a secret
/// throws instead of silently passing the plaintext through, so a missing
/// platform implementation cannot degrade into cleartext storage.
/// </remarks>
public sealed class NullSecretProtector : ISecretProtector
{
    private const string Message =
        "No secret protector is configured for this host. Register a platform ISecretProtector " +
        "implementation before storing or reading credentials.";

    /// <summary>
    /// Treats every value as already protected so the load-time migration path
    /// does not try to re-protect records it cannot handle.
    /// </summary>
    public bool IsProtected(string value) => true;

    public string Protect(string secret) => throw new NotSupportedException(Message);

    public string Unprotect(string protectedSecret) => throw new NotSupportedException(Message);
}

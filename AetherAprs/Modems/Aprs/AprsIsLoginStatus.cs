// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// Authentication outcome the APRS-IS server reported for the current connection.
/// </summary>
/// <remarks>
/// A verified login may transmit. An unverified login is the server's answer to an
/// anonymous or wrong passcode: receiving works, but the server will not relay what
/// this client sends, so the connection is read-only.
/// </remarks>
internal enum AprsIsLoginStatus
{
    /// <summary>The login reply has not arrived yet, so nothing may be transmitted.</summary>
    Pending,

    /// <summary>The server accepted the passcode; both receive and transmit work.</summary>
    Verified,

    /// <summary>The server accepted the connection read-only; nothing may be transmitted.</summary>
    Unverified
}

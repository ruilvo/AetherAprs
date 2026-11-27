// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;

namespace AetherAprs.Models.Objects;

/// <summary>
/// Why an object or bulletin could not be broadcast.
/// </summary>
/// <remarks>
/// These are operator-visible states, not exception messages. A caller renders
/// <see cref="Describe"/> directly, so the reason is localized and stays stable even
/// when the transport or storage layer changes underneath.
/// </remarks>
public enum BroadcastFailure
{
    /// <summary>Nothing failed.</summary>
    None,

    /// <summary>No enabled port can transmit, so there is no route to the air.</summary>
    NoTransmitRoute,

    /// <summary>The object the report refers to is not known from received traffic.</summary>
    UnknownObject,

    /// <summary>The transmission reached the transport but was not accepted.</summary>
    TransmissionRejected,
}

/// <summary>
/// Localized text for <see cref="BroadcastFailure"/>.
/// </summary>
public static class BroadcastFailureText
{
    /// <summary>
    /// Describes <paramref name="failure"/> for the operator.
    /// </summary>
    /// <param name="failure">The failure to describe.</param>
    /// <returns>A localized sentence, or an empty string for <see cref="BroadcastFailure.None"/>.</returns>
    public static string Describe(BroadcastFailure failure) => failure switch
    {
        BroadcastFailure.NoTransmitRoute => Strings.Get("FailureNoTransmitRoute"),
        BroadcastFailure.UnknownObject => Strings.Get("FailureUnknownObject"),
        BroadcastFailure.TransmissionRejected => Strings.Get("FailureTransmissionRejected"),
        _ => string.Empty,
    };
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models.Objects;

/// <summary>
/// Thrown when an object or bulletin cannot be broadcast, carrying the
/// operator-visible reason instead of a developer-facing message.
/// </summary>
/// <remarks>
/// The exception exists so the reason survives the service boundary as a typed value.
/// The view model renders <see cref="BroadcastFailureText.Describe"/> for
/// <see cref="Failure"/> and never shows <see cref="Exception.Message"/>, which keeps
/// the text localized and free of transport detail.
/// </remarks>
public sealed class BroadcastFailedException : InvalidOperationException
{
    /// <summary>
    /// Initializes a broadcast failure.
    /// </summary>
    /// <param name="failure">The operator-visible reason.</param>
    /// <param name="innerException">The underlying failure, when there is one.</param>
    public BroadcastFailedException(BroadcastFailure failure, Exception? innerException = null)
        : base($"Broadcast failed: {failure}.", innerException) => Failure = failure;

    /// <summary>
    /// Gets the operator-visible reason the broadcast failed.
    /// </summary>
    public BroadcastFailure Failure { get; }
}

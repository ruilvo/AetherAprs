// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Thrown when the platform secret store cannot protect or restore a secret, for
/// example when an Android Keystore key was lost with a device restore.
/// </summary>
/// <remarks>
/// Callers catch this to treat the affected credentials as unavailable while the rest
/// of the configuration keeps working. It is deliberately narrower than
/// <see cref="InvalidOperationException"/> so a genuine programming error is not
/// mistaken for a lost key.
/// </remarks>
public sealed class SecretUnavailableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="SecretUnavailableException"/> class.</summary>
    public SecretUnavailableException()
    {
    }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The failure description.</param>
    public SecretUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and the platform failure.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The platform exception that caused the failure.</param>
    public SecretUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

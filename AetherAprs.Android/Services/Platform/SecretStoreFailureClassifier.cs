// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Platform-neutral category for a secret-store failure. The Android adapter maps
/// its Java exceptions onto these values.
/// </summary>
internal enum SecretStoreExceptionCategory
{
    /// <summary>Not a secret-store failure; the caller should rethrow it.</summary>
    Unrelated,

    /// <summary>A keystore security failure, such as an invalidated or missing key.</summary>
    KeyStoreSecurity,

    /// <summary>The stored payload is malformed and can never be decrypted again.</summary>
    CorruptPayload,

    /// <summary>An argument was rejected, which points at a programming error.</summary>
    InvalidArgument,

    /// <summary>A transient I/O failure that may succeed on a retry.</summary>
    TransientIo,

    /// <summary>The keystore or cipher was used in an invalid state.</summary>
    InvalidState
}

/// <summary>What a secret-store failure means for the stored secret.</summary>
internal enum SecretStoreFailureOutcome
{
    /// <summary>The exception is unrelated; it must propagate unchanged.</summary>
    NotFailure,

    /// <summary>The key or payload is gone, so this secret cannot be recovered.</summary>
    KeyLost,

    /// <summary>The failure is temporary and a later attempt may succeed.</summary>
    Transient,

    /// <summary>The failure is not recoverable and is not a lost key.</summary>
    Fatal
}

/// <summary>
/// Classifies secret-store failures so the keystore adapter can distinguish a
/// lost key from a transient failure. Kept free of Android types so every
/// outcome can be unit-tested.
/// </summary>
internal static class SecretStoreFailureClassifier
{
    /// <summary>Maps an exception category onto a failure outcome.</summary>
    /// <param name="category">Category derived from the platform exception.</param>
    public static SecretStoreFailureOutcome Classify(SecretStoreExceptionCategory category) => category switch
    {
        SecretStoreExceptionCategory.Unrelated => SecretStoreFailureOutcome.NotFailure,
        SecretStoreExceptionCategory.KeyStoreSecurity => SecretStoreFailureOutcome.KeyLost,
        SecretStoreExceptionCategory.CorruptPayload => SecretStoreFailureOutcome.KeyLost,
        SecretStoreExceptionCategory.TransientIo => SecretStoreFailureOutcome.Transient,
        _ => SecretStoreFailureOutcome.Fatal
    };
}

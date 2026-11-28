// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Android.Services.Platform;

using Xunit;

namespace AetherAprs.Tests.Android;

/// <summary>
/// Regression tests: a keystore key invalidated by a device restore or lock
/// change must be reported as a lost key, while an I/O failure stays transient
/// and unrelated exceptions still propagate as fatal.
/// </summary>
public sealed class SecretStoreFailureClassifierTests
{
    [Fact]
    public void Classify_MapsCategoryToOutcome()
    {
        (SecretStoreExceptionCategory Category, SecretStoreFailureOutcome Expected)[] cases =
        [
            (SecretStoreExceptionCategory.KeyStoreSecurity, SecretStoreFailureOutcome.KeyLost),
            (SecretStoreExceptionCategory.CorruptPayload, SecretStoreFailureOutcome.KeyLost),
            (SecretStoreExceptionCategory.TransientIo, SecretStoreFailureOutcome.Transient),
            (SecretStoreExceptionCategory.InvalidArgument, SecretStoreFailureOutcome.Fatal),
            (SecretStoreExceptionCategory.InvalidState, SecretStoreFailureOutcome.Fatal),
            (SecretStoreExceptionCategory.Unrelated, SecretStoreFailureOutcome.NotFailure)
        ];

        foreach ((SecretStoreExceptionCategory category, SecretStoreFailureOutcome expected) in cases)
        {
            Assert.Equal(expected, SecretStoreFailureClassifier.Classify(category));
        }
    }

    [Fact]
    public void Classify_KeyInvalidated_IsKeyLost() => Assert.Equal(
        SecretStoreFailureOutcome.KeyLost,
        SecretStoreFailureClassifier.Classify(SecretStoreExceptionCategory.KeyStoreSecurity));

    [Fact]
    public void Classify_TransientIo_IsTransient() => Assert.Equal(
        SecretStoreFailureOutcome.Transient,
        SecretStoreFailureClassifier.Classify(SecretStoreExceptionCategory.TransientIo));

    [Fact]
    public void Classify_Unrelated_IsNotAFailure() => Assert.Equal(
        SecretStoreFailureOutcome.NotFailure,
        SecretStoreFailureClassifier.Classify(SecretStoreExceptionCategory.Unrelated));
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.ComponentModel.DataAnnotations;

namespace AetherAprs.ViewModels.Validation;

/// <summary>
/// Rejects text longer than the given number of characters.
/// </summary>
/// <remarks>
/// <see cref="MaxLengthAttribute"/> reflects for a <c>Count</c> property on values
/// that are neither strings nor collections, which cannot be analyzed for a
/// trimmed application. The view models only ever limit strings, so a direct
/// length comparison expresses the same rule without reflection.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    AllowMultiple = false)]
public sealed class MaxTextLengthAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new text length limit.
    /// </summary>
    /// <param name="length">The maximum number of characters.</param>
    public MaxTextLengthAttribute(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Length = length;
    }

    /// <summary>
    /// Gets the maximum number of characters.
    /// </summary>
    public int Length { get; }

    /// <inheritdoc />
    public override bool IsValid(object? value) => value switch
    {
        null => true,
        string text => text.Length <= Length,
        _ => false
    };
}

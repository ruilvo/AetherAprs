// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.ComponentModel.DataAnnotations;
using AetherAprs.Models.Aprs;

namespace AetherAprs.ViewModels.Validation;

/// <summary>
/// Accepts a comma-separated APRS digipeater path such as <c>WIDE1-1,WIDE2-1</c>.
/// </summary>
/// <remarks>
/// The saved setting is what <see cref="PathHelper.ParsePath"/> turns into the AX.25
/// digipeater field at transmit time, and that parser throws on a hop it cannot read.
/// Rejecting the same input in the field keeps the bad value out of the saved settings
/// instead of failing the beacon after the operator has moved on.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    AllowMultiple = false)]
public sealed class AprsPathAttribute : ValidationAttribute
{
    /// <summary>Longest accepted path, matching the AX.25 digipeater field limit.</summary>
    public const int MaxHops = 8;

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not string path)
        {
            return false;
        }

        // An empty path is a legal beacon with no digipeaters.
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        string[] hops = path.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (hops.Length is 0 or > MaxHops)
        {
            return false;
        }

        foreach (string hop in hops)
        {
            // A trailing '*' marks a hop that has already been repeated.
            string candidate = hop.EndsWith('*') ? hop[..^1] : hop;
            if (!Callsign.TryParse(candidate, out _))
            {
                return false;
            }
        }

        return true;
    }
}

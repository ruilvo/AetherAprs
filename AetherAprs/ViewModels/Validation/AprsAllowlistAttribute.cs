// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Services.Ports;
using System;
using System.ComponentModel.DataAnnotations;

namespace AetherAprs.ViewModels.Validation;

/// <summary>
/// Accepts the Internet-to-RF allowlist: callsigns separated by commas, semicolons or
/// whitespace, or an empty field meaning "no restriction".
/// </summary>
/// <remarks>
/// <see cref="DigipeatPolicy.ParseAllowlist"/> ignores entries it cannot read so a bad
/// edit can never widen the gate at runtime. The settings field must not swallow the
/// mistake silently, though: it validates the very entries the policy would split and
/// rejects the edit, so the operator sees the problem instead of losing an entry.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    AllowMultiple = false)]
public sealed class AprsAllowlistAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not string allowlist)
        {
            return false;
        }

        foreach (string entry in DigipeatPolicy.SplitAllowlist(allowlist))
        {
            if (!Callsign.TryParse(entry, out _))
            {
                return false;
            }
        }

        return true;
    }
}

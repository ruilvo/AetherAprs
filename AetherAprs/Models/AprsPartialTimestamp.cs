// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models;

using System;

public sealed class AprsPartialTimestamp
{
    public int? Day { get; init; }

    public required int Hour { get; init; }

    public required int Minute { get; init; }

    public int? Second { get; init; }

    public required char Suffix { get; init; }

    public void Validate()
    {
        if (Hour is < 0 or > 23)
        {
            throw new InvalidOperationException("APRS timestamp hour must be between 0 and 23.");
        }

        if (Minute is < 0 or > 59)
        {
            throw new InvalidOperationException("APRS timestamp minute must be between 0 and 59.");
        }

        switch (Suffix)
        {
            case 'z':
            case '/':
                if (Day is null or < 1 or > 31)
                {
                    throw new InvalidOperationException("APRS timestamp day must be between 1 and 31.");
                }

                if (Second is not null)
                {
                    throw new InvalidOperationException("DDHHMM APRS timestamps must not include seconds.");
                }

                break;

            case 'h':
                if (Day is not null)
                {
                    throw new InvalidOperationException("HHMMSSh APRS timestamps must not include a day.");
                }

                if (Second is null or < 0 or > 59)
                {
                    throw new InvalidOperationException("HHMMSSh APRS timestamps require seconds between 0 and 59.");
                }

                break;

            default:
                throw new InvalidOperationException($"Unsupported APRS timestamp suffix '{Suffix}'.");
        }
    }
}

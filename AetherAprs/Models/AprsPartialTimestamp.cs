// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models;

using System;

public sealed class AprsPartialTimestamp
{
    public required int Day { get; init; }

    public required int Hour { get; init; }

    public required int Minute { get; init; }

    public required char Suffix { get; init; }

    public void Validate()
    {
        if (Day is < 1 or > 31)
        {
            throw new InvalidOperationException("APRS timestamp day must be between 1 and 31.");
        }

        if (Hour is < 0 or > 23)
        {
            throw new InvalidOperationException("APRS timestamp hour must be between 0 and 23.");
        }

        if (Minute is < 0 or > 59)
        {
            throw new InvalidOperationException("APRS timestamp minute must be between 0 and 59.");
        }

        if (Suffix != 'z')
        {
            throw new InvalidOperationException("Only zulu APRS timestamps are supported.");
        }
    }
}

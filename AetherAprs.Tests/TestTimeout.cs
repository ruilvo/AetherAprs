// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Tests;

/// <summary>
/// Shared time limits for test waits, so a stalled subject fails the test
/// instead of hanging the whole run.
/// </summary>
public static class TestTimeouts
{
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
}

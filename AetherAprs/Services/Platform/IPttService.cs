// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// Abstraction over a radio push-to-talk (PTT) control. Implementations key
/// and unkey the transmitter (e.g. Android's speaker-mic toggle via
/// <c>AudioManager</c>).
/// </summary>
public interface IPttService
{
    /// <summary>
    /// Keys the transmitter. Safe to call when already keyed.
    /// </summary>
    void Engage();

    /// <summary>
    /// Unkeys the transmitter. Safe to call when already unkeyed.
    /// </summary>
    void Release();
}

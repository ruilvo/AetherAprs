// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Bluetooth;

/// <summary>
/// The fate of one advertisement offered to <see cref="BluetoothAdvertisementBuffer"/>.
/// </summary>
public enum BluetoothAdvertisementPublishResult
{
    /// <summary>The advertisement is waiting for the consumer.</summary>
    Buffered,

    /// <summary>The advertisement named only services this application does not speak.</summary>
    Filtered,

    /// <summary>The buffer already completed, so the advertisement was not examined.</summary>
    Completed
}

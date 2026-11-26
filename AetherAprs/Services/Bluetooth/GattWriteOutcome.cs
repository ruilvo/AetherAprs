// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Bluetooth;

/// <summary>
/// The result of writing one ATT-sized chunk to a Bluetooth LE characteristic.
/// </summary>
/// <remarks>
/// The platform reports statuses as integers, so the adapter maps them to these
/// outcomes and the retry policy lives in one place. Only <see cref="Busy"/> and
/// <see cref="ConnectionError"/> are transient; everything else means the stack
/// rejected the write and retrying would repeat the same rejection.
/// </remarks>/// </remarks>
public enum GattWriteOutcome
{
    /// <summary>The stack accepted the chunk.</summary>
    Success,

    /// <summary>Another ATT request is still in flight; the chunk can be retried.</summary>
    Busy,

    /// <summary>The stack failed transiently; the chunk can be retried.</summary>
    ConnectionError,

    /// <summary>The stack refused the chunk and will refuse it again.</summary>
    Rejected
}

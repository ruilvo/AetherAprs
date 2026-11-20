// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// Safe default implementation of <see cref="IPttService"/> for platforms
/// without PTT control (or when operating VOX). Keying is a no-op.
/// </summary>
public sealed class NoOpPttService : IPttService
{
    /// <inheritdoc/>
    public void Engage()
    {
    }

    /// <inheritdoc/>
    public void Release()
    {
    }
}

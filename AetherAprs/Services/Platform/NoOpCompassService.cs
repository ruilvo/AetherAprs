// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Safe default implementation of <see cref="ICompassService"/> for platforms
/// without a compass sensor. Always reports unavailable and never raises updates.
/// </summary>
public sealed class NoOpCompassService : ICompassService
{
    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public double? HeadingDegrees => null;

    /// <inheritdoc/>
    public event EventHandler<double>? HeadingUpdated
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public void StartListening()
    {
    }

    /// <inheritdoc/>
    public void StopListening()
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

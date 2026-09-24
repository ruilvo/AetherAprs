// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using AetherAprs.Localization;

namespace AetherAprs.Tests;

/// <summary>
/// Base class for test fixtures that ensures English culture for consistent string assertions.
/// </summary>
public abstract class TestFixtureBase : IDisposable
{
    private readonly CultureInfo _originalCulture;
    private readonly CultureInfo _originalUiCulture;

    protected TestFixtureBase()
    {
        // Save original culture
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUiCulture = CultureInfo.CurrentUICulture;

        // Force English culture for tests
        UiCulture.Apply(CultureInfo.GetCultureInfo("en-US"));
    }

    public void Dispose()
    {
        // Restore original culture
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        CultureInfo.DefaultThreadCurrentCulture = _originalCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _originalUiCulture;
        GC.SuppressFinalize(this);
    }
}

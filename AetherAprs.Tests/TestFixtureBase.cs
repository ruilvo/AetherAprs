// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Tests;

/// <summary>
/// Base class for test fixtures.
/// </summary>
/// <remarks>
/// The English UI culture is applied once for the entire test process by
/// <see cref="TestCulture"/>. Fixtures deliberately do not touch or restore it:
/// restoring a captured culture from one fixture leaked the host language into
/// tests running in parallel, which made localized assertions fail intermittently.
/// The type stays as a base so existing fixtures keep their shape.
/// </remarks>
public abstract class TestFixtureBase : IDisposable
{
    public void Dispose() => GC.SuppressFinalize(this);
}

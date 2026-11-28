// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;

using Xunit;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Guards the operator's access to beaconing. The manual send and the beacon status
/// were both implemented in the view model well before anything on screen used them,
/// so a suppressed or failed beacon reported nothing at all: the operator pressed
/// nothing and learned nothing. These assertions read the shipped AXAML, because the
/// test project has no Avalonia headless host.
/// </summary>
public sealed class BeaconSurfaceTests
{
    private static readonly string HomePage = Path.Combine(
        FindRepositoryRoot(), "AetherAprs", "Views", "Pages", "HomePage.axaml");

    [Fact]
    public void TheHomePageCanSendAManualBeacon()
    {
        string page = File.ReadAllText(HomePage);

        Assert.Contains("{Binding SendManualBeaconCommand}", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHomePageShowsTheBeaconStatus()
    {
        string page = File.ReadAllText(HomePage);

        // The status is what turns a suppressed or failed beacon into something the
        // operator can see, so it must be bound and must collapse while it is empty.
        Assert.Contains("{Binding BeaconStatus}", page, StringComparison.Ordinal);
        Assert.Contains(
            "{Binding BeaconStatus, Converter={x:Static StringConverters.IsNotNullOrEmpty}}",
            page,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AetherAprs.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Data.Ports;

public sealed class PortRegistryCredentialTests
{
    [Fact]
    public async Task LoadAsync_WhenOneStoredCredentialIsLost_KeepsTheOtherPorts()
    {
        PortConfig lostPort = AprsIsPort("Lost server", "12345");
        PortConfig healthyPort = AprsIsPort("Healthy server", "54321");
        PortConfig rfPort = new()
        {
            Id = Guid.NewGuid(),
            Name = "RF",
            IsEnabled = true,
            TypeSettings = new KissSettings()
        };
        using TempAppDatabase db = TempAppDatabase.Create(lostPort, healthyPort, rfPort);
        IPortRegistry registry = StoreFactory.CreatePortRegistry(db, new KeyLostFor("12345"));

        IReadOnlyList<PortConfig> ports = await registry.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, ports.Count);

        PortConfig lost = Assert.Single(ports, port => port.Id == lostPort.Id);
        Assert.True(lost.CredentialsUnavailable);
        AprsIsSettings lostSettings = Assert.IsType<AprsIsSettings>(lost.TypeSettings);
        Assert.Equal(AprsIsSettings.DefaultPasscode, lostSettings.Passcode);
        Assert.Equal("Lost server", lost.Name);

        PortConfig healthy = Assert.Single(ports, port => port.Id == healthyPort.Id);
        Assert.False(healthy.CredentialsUnavailable);
        Assert.Equal("54321", Assert.IsType<AprsIsSettings>(healthy.TypeSettings).Passcode);

        PortConfig rf = Assert.Single(ports, port => port.Id == rfPort.Id);
        Assert.False(rf.CredentialsUnavailable);
        Assert.IsType<KissSettings>(rf.TypeSettings);
    }

    [Fact]
    public async Task LoadAsync_WhenEveryCredentialIsReadable_MarksNoPort()
    {
        PortConfig port = AprsIsPort("Healthy server", "54321");
        using TempAppDatabase db = TempAppDatabase.Create(port);
        IPortRegistry registry = StoreFactory.CreatePortRegistry(db, new KeyLostFor("unrelated"));

        PortConfig loaded = Assert.Single(await registry.LoadAsync(TestContext.Current.CancellationToken));

        Assert.False(loaded.CredentialsUnavailable);
        Assert.Equal("54321", Assert.IsType<AprsIsSettings>(loaded.TypeSettings).Passcode);
    }

    [Fact]
    public void Clone_PreservesTheCredentialsUnavailableMarker()
    {
        PortConfig port = new()
        {
            Name = "Server",
            CredentialsUnavailable = true,
            TypeSettings = new AprsIsSettings()
        };

        Assert.True(port.Clone().CredentialsUnavailable);
    }

    private static PortConfig AprsIsPort(string name, string passcode) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsEnabled = true,
        TypeSettings = new AprsIsSettings { Passcode = passcode }
    };

    /// <summary>
    /// Restores every stored secret except one, so tests can prove that a single lost
    /// Keystore entry does not fail the whole port list.
    /// </summary>
    private sealed class KeyLostFor(string lostSecret) : ISecretProtector
    {
        public bool IsProtected(string value) =>
            !string.IsNullOrEmpty(value) && value.StartsWith("ks1:", StringComparison.Ordinal);

        public string Protect(string secret) => secret;

        public string Unprotect(string protectedSecret) =>
            protectedSecret == lostSecret
                ? throw new SecretUnavailableException("The Keystore key for this port is gone.")
                : protectedSecret;
    }
}

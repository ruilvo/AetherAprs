// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using System;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Data.Ports;

public sealed class PortRegistryCredentialTests
{
    [Fact]
    public async Task LoadAsync_WhenOneStoredCredentialIsLost_KeepsTheOtherPorts()
    {
        var lostPort = AprsIsPort("Lost server", "12345");
        var healthyPort = AprsIsPort("Healthy server", "54321");
        var rfPort = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "RF",
            IsEnabled = true,
            TypeSettings = new KissSettings()
        };
        using var db = TempAppDatabase.Create(lostPort, healthyPort, rfPort);
        var registry = StoreFactory.CreatePortRegistry(db, new KeyLostFor("12345"));

        var ports = await registry.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, ports.Count);

        var lost = Assert.Single(ports, port => port.Id == lostPort.Id);
        Assert.True(lost.CredentialsUnavailable);
        var lostSettings = Assert.IsType<AprsIsSettings>(lost.TypeSettings);
        Assert.Equal(AprsIsSettings.DefaultPasscode, lostSettings.Passcode);
        Assert.Equal("Lost server", lost.Name);

        var healthy = Assert.Single(ports, port => port.Id == healthyPort.Id);
        Assert.False(healthy.CredentialsUnavailable);
        Assert.Equal("54321", Assert.IsType<AprsIsSettings>(healthy.TypeSettings).Passcode);

        var rf = Assert.Single(ports, port => port.Id == rfPort.Id);
        Assert.False(rf.CredentialsUnavailable);
        Assert.IsType<KissSettings>(rf.TypeSettings);
    }

    [Fact]
    public async Task LoadAsync_WhenEveryCredentialIsReadable_MarksNoPort()
    {
        var port = AprsIsPort("Healthy server", "54321");
        using var db = TempAppDatabase.Create(port);
        var registry = StoreFactory.CreatePortRegistry(db, new KeyLostFor("unrelated"));

        var loaded = Assert.Single(await registry.LoadAsync(TestContext.Current.CancellationToken));

        Assert.False(loaded.CredentialsUnavailable);
        Assert.Equal("54321", Assert.IsType<AprsIsSettings>(loaded.TypeSettings).Passcode);
    }

    [Fact]
    public void Clone_PreservesTheCredentialsUnavailableMarker()
    {
        var port = new PortConfig
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

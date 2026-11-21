// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Data.Mappers;
using AetherAprs.Services.Platform;
using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class SecretStorageTests
{
    [Fact]
    public void PortRecordMapper_StoresProtectedPasscodeRepresentation()
    {
        var port = new PortConfig
        {
            Name = "APRS-IS",
            TypeSettings = new AprsIsSettings { Passcode = "12345" }
        };

        var record = PortRecordMapper.ToRecord(port, "ks1:encrypted-value");

        Assert.Equal("ks1:encrypted-value", record.AprsIs?.Passcode);
        Assert.NotEqual("12345", record.AprsIs?.Passcode);
    }

    [Fact]
    public void PlaintextSecretProtector_IsOnlyTheNonAndroidFallback()
    {
        var protector = new PlaintextSecretProtector();

        Assert.False(protector.IsProtected("12345"));
        Assert.Equal("12345", protector.Protect("12345"));
        Assert.Equal("12345", protector.Unprotect("12345"));
    }
}

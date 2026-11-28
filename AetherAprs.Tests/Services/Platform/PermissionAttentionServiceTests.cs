// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Linq;

using AetherAprs.Services.Platform;

using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class PermissionAttentionServiceTests
{
    [Fact]
    public void Report_WhenAPermissionIsDenied_KeepsItOutstanding()
    {
        PermissionAttentionService service = new();

        service.Report(PermissionRequestResult.Denied(PermissionKind.Notifications));

        PermissionAttention attention = Assert.Single(service.Outstanding);
        Assert.Equal(PermissionKind.Notifications, attention.Kind);
        Assert.Equal(PermissionOutcome.Denied, attention.Outcome);
        Assert.False(attention.RequiresApplicationSettings);
    }

    [Fact]
    public void Report_WhenAPermissionIsBlockedInSettings_RequiresApplicationSettings()
    {
        PermissionAttentionService service = new();

        service.Report(PermissionRequestResult.PermanentlyDenied(PermissionKind.Bluetooth));

        PermissionAttention attention = Assert.Single(service.Outstanding);
        Assert.True(attention.RequiresApplicationSettings);
    }

    [Fact]
    public void Report_WhenTheOperatorGrantsItAgain_ClearsItAndRaisesChanged()
    {
        PermissionAttentionService service = new();
        int raised = 0;
        service.Changed += (_, _) => raised++;
        service.Report(PermissionRequestResult.Denied(PermissionKind.Audio));

        service.Report(PermissionRequestResult.Granted(PermissionKind.Audio));

        Assert.Empty(service.Outstanding);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Report_WhenThePlatformCannotAnswer_ClearsIt()
    {
        PermissionAttentionService service = new();
        service.Report(PermissionRequestResult.Denied(PermissionKind.Bluetooth));

        service.Report(PermissionRequestResult.Unavailable(PermissionKind.Bluetooth));

        Assert.Empty(service.Outstanding);
    }

    [Fact]
    public void Report_WhenTheSameOutcomeRepeats_RaisesNothing()
    {
        PermissionAttentionService service = new();
        int raised = 0;
        service.Changed += (_, _) => raised++;
        service.Report(PermissionRequestResult.Denied(PermissionKind.Notifications));

        service.Report(PermissionRequestResult.Denied(PermissionKind.Notifications));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Outstanding_IsOrderedByPermissionKind()
    {
        PermissionAttentionService service = new();

        service.Report(PermissionRequestResult.Denied(PermissionKind.Bluetooth));
        service.Report(PermissionRequestResult.Denied(PermissionKind.Notifications));

        Assert.Equal(
            [PermissionKind.Notifications, PermissionKind.Bluetooth],
            service.Outstanding.Select(attention => attention.Kind));
    }
}

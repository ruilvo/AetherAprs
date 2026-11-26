// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Data.Entities;
using AetherAprs.Services.Packets;
using AetherAprs.ViewModels.Pages;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class ObjectDetailsViewModelTests : TestFixtureBase
{
    private static PacketRecord Report(long id) => new()
    {
        Id = id,
        SourceBase = "N0CALL",
        PacketType = "Object",
        RawInfo = ";",
        Timestamp = DateTime.UtcNow
    };

    [Fact]
    public void Initialize_WithReports_PublishesHasReports()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByObjectNameAsync("TENT", 500, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>([Report(1), Report(2)]));

        ObjectDetailsViewModel viewModel = new(query);
        List<string?> raised = [];
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.False(viewModel.HasReports);

        viewModel.Initialize("TENT");

        Assert.Equal(2, viewModel.Reports.Count);
        Assert.True(viewModel.HasReports);
        Assert.Contains(nameof(ObjectDetailsViewModel.HasReports), raised);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public void Initialize_WithoutReports_KeepsTheEmptyStateVisible()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByObjectNameAsync("TENT", 500, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>([]));

        ObjectDetailsViewModel viewModel = new(query);

        viewModel.Initialize("TENT");

        Assert.Empty(viewModel.Reports);
        Assert.False(viewModel.HasReports);
        Assert.False(viewModel.IsLoading);
    }
}

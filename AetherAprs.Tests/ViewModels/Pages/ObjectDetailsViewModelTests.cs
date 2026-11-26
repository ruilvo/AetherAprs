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

    private static IReadOnlyList<PacketRecord> CreatePage(long newestId, int count)
    {
        List<PacketRecord> page = [];
        for (int index = 0; index < count; index++)
        {
            page.Add(Report(newestId - index));
        }

        return page;
    }

    [Fact]
    public void Initialize_WithReports_PublishesHasReports()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByObjectNamePageAsync("TENT", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>([Report(2), Report(1)]));

        ObjectDetailsViewModel viewModel = new(query);
        List<string?> raised = [];
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.False(viewModel.HasReports);

        viewModel.Initialize("TENT");

        Assert.Equal(2, viewModel.Reports.Count);
        Assert.True(viewModel.HasReports);
        Assert.False(viewModel.HasMore);
        Assert.Contains(nameof(ObjectDetailsViewModel.HasReports), raised);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public void Initialize_WithoutReports_KeepsTheEmptyStateVisible()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByObjectNamePageAsync("TENT", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>([]));

        ObjectDetailsViewModel viewModel = new(query);

        viewModel.Initialize("TENT");

        Assert.Empty(viewModel.Reports);
        Assert.False(viewModel.HasReports);
        Assert.False(viewModel.HasMore);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task LoadMore_AppendsTheNextPageFromTheOldestLoadedId()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByObjectNamePageAsync("TENT", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreatePage(newestId: 100, count: 50)));
        query.GetPacketsByObjectNamePageAsync("TENT", 51, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreatePage(newestId: 50, count: 1)));

        ObjectDetailsViewModel viewModel = new(query);
        viewModel.Initialize("TENT");

        Assert.Equal(50, viewModel.Reports.Count);
        Assert.True(viewModel.HasMore);

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        // Pages continue from the oldest id already shown and never disturb the rows
        // that are already on screen.
        await query.Received(1).GetPacketsByObjectNamePageAsync(
            "TENT", 51, 50, Arg.Any<CancellationToken>());
        Assert.Equal(51, viewModel.Reports.Count);
        Assert.Equal(100, viewModel.Reports[0].Id);
        Assert.Equal(50, viewModel.Reports[^1].Id);
        Assert.False(viewModel.HasMore);
    }

    [Fact]
    public async Task LoadMore_AfterAShortPage_DoesNotQueryAgain()
    {
        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByObjectNamePageAsync("TENT", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreatePage(newestId: 3, count: 3)));

        ObjectDetailsViewModel viewModel = new(query);
        viewModel.Initialize("TENT");

        Assert.False(viewModel.HasMore);

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        await query.Received(1).GetPacketsByObjectNamePageAsync(
            "TENT", Arg.Any<long?>(), 50, Arg.Any<CancellationToken>());
        Assert.Equal(3, viewModel.Reports.Count);
    }
}

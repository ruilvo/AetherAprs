// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
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
    private static PacketRecord Report(long id, DateTime? timestamp = null) => new()
    {
        Id = id,
        SourceBase = "N0CALL",
        PacketType = "Object",
        RawInfo = ";",
        Timestamp = timestamp ?? DateTime.UtcNow
    };

    private static IReadOnlyList<PacketRecord> CreatePage(long newestId, int count)
    {
        // Ids descend with receipt time so the page is ordered the way the query
        // orders it: newest first by timestamp, then by id.
        DateTime newerThan = DateTime.UtcNow.AddSeconds(newestId);
        List<PacketRecord> page = [];
        for (int index = 0; index < count; index++)
        {
            long id = newestId - index;
            page.Add(Report(id, newerThan.AddSeconds(-index)));
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
        IReadOnlyList<PacketRecord> firstPage = CreatePage(newestId: 100, count: 50);
        IReadOnlyList<PacketRecord> secondPage = CreatePage(newestId: 50, count: 1);
        PacketPageCursor firstCursor = PacketPageCursor.FromPacket(firstPage[^1]);

        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByObjectNamePageAsync("TENT", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(firstPage));
        query.GetPacketsByObjectNamePageAsync("TENT", firstCursor, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(secondPage));

        ObjectDetailsViewModel viewModel = new(query);
        viewModel.Initialize("TENT");

        Assert.Equal(50, viewModel.Reports.Count);
        Assert.True(viewModel.HasMore);

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        // Pages continue from the (timestamp, id) pair of the oldest report already
        // shown and never disturb the rows that are already on screen.
        await query.Received(1).GetPacketsByObjectNamePageAsync(
            "TENT", firstCursor, 50, Arg.Any<CancellationToken>());
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
            "TENT", Arg.Any<PacketPageCursor?>(), 50, Arg.Any<CancellationToken>());
        Assert.Equal(3, viewModel.Reports.Count);
    }

    [Fact]
    public async Task LoadMore_WithASharedTimestamp_ContinuesFromTheOldestReport()
    {
        // Every report shares one timestamp, so the cursor must carry both halves of
        // the ordering key to keep paging. A full first page keeps HasMore true.
        DateTime sharedTimestamp = new(2026, 11, 27, 6, 0, 0, DateTimeKind.Utc);
        List<PacketRecord> all = [];
        for (int index = 0; index < 51; index++)
        {
            all.Add(Report(100 - index, sharedTimestamp));
        }

        IPacketQueryService query = Substitute.For<IPacketQueryService>();
        query.GetPacketsByObjectNamePageAsync("TENT", null, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>([.. all.Take(50)]));
        query.GetPacketsByObjectNamePageAsync("TENT", new PacketPageCursor(51, sharedTimestamp), 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PacketRecord>>([.. all.Skip(50)]));

        ObjectDetailsViewModel viewModel = new(query);
        viewModel.Initialize("TENT");

        // Act
        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        // Assert
        await query.Received(1).GetPacketsByObjectNamePageAsync(
            "TENT", new PacketPageCursor(51, sharedTimestamp), 50, Arg.Any<CancellationToken>());
        Assert.Equal(51, viewModel.Reports.Count);
        Assert.Equal(51, viewModel.Reports.Select(report => report.Id).Distinct().Count());
    }
}

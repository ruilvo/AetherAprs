// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Messaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Tests.Helpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace AetherAprs.Tests.Data;

public sealed class OutboxMessageStoreTests : IAsyncDisposable
{
    private static readonly AprsAddress Peer = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));

    private readonly TempAppDatabase _database;
    private readonly SqliteRuntimeWriter _writer;
    private readonly TestClock _clock = new();
    private readonly OutboxMessageStore _store;

    public OutboxMessageStoreTests()
    {
        _database = TempAppDatabase.CreateEmpty();
        using (AppDbContext context = _database.CreateContext())
        {
            context.Database.EnsureCreated();
        }

        _writer = new SqliteRuntimeWriter(_database.Factory);
        _store = new OutboxMessageStore(
            _writer,
            new SqliteRuntimeReader(_database.Factory),
            _clock,
            NullLogger<OutboxMessageStore>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    public async Task EnqueueAsync_FiveConcurrentSendsForOneLogicalMessage_CreateOneDurableRecord()
    {
        Guid messageId = Guid.CreateVersion7();
        OutboxMessageDraft draft = CreateDraft(messageId);

        OutboxEnqueueResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => _store.EnqueueAsync(draft, TestContext.Current.CancellationToken)));

        Assert.Single(results, result => result.IsNew);
        Assert.All(results, result => Assert.Equal(messageId, result.Id));
        Assert.All(results, result => Assert.Equal(1, result.MessageNumber));
        Assert.All(results, result => Assert.Equal(MessageDeliveryStatus.Pending, result.Status));

        await using AppDbContext context = _database.CreateContext();
        Assert.Equal(1, await context.OutboxMessages.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordAttemptAsync_IncrementsAttemptCountOnTheRecord()
    {
        OutboxMessageDraft draft = CreateDraft(Guid.CreateVersion7());
        await _store.EnqueueAsync(draft, TestContext.Current.CancellationToken);

        await _store.RecordAttemptAsync(draft.Id, draft.NextAttemptAt!.Value, TestContext.Current.CancellationToken);
        OutboxTransitionResult second = await _store.RecordAttemptAsync(
            draft.Id, draft.NextAttemptAt!.Value, TestContext.Current.CancellationToken);

        Assert.Equal(OutboxTransitionOutcome.Applied, second.Outcome);
        Assert.NotNull(second.Message);
        Assert.Equal(2, second.Message.AttemptCount);

        await using AppDbContext context = _database.CreateContext();
        Assert.Equal(2, Assert.Single(context.OutboxMessages).AttemptCount);
    }

    [Fact]
    public async Task ApplyDeliveryOutcomeAsync_DuplicateOrLateOutcome_NeverRegressesTerminalState()
    {
        OutboxMessageDraft draft = CreateDraft(Guid.CreateVersion7());
        OutboxEnqueueResult enqueued = await _store.EnqueueAsync(draft, TestContext.Current.CancellationToken);

        OutboxTransitionResult acknowledged = await _store.ApplyDeliveryOutcomeAsync(
            Peer, enqueued.MessageNumber!.Value, MessageDeliveryStatus.Acknowledged,
            TestContext.Current.CancellationToken);
        OutboxTransitionResult lateRejection = await _store.ApplyDeliveryOutcomeAsync(
            Peer, enqueued.MessageNumber!.Value, MessageDeliveryStatus.Rejected,
            TestContext.Current.CancellationToken);

        Assert.Equal(OutboxTransitionOutcome.Applied, acknowledged.Outcome);
        Assert.Equal(OutboxTransitionOutcome.AlreadyTerminal, lateRejection.Outcome);
        Assert.Equal(MessageDeliveryStatus.Acknowledged, lateRejection.Message!.Status);
    }

    [Fact]
    public async Task ApplyDeliveryOutcomeAsync_FromAnotherStation_DoesNotMatchTheMessage()
    {
        OutboxMessageDraft draft = CreateDraft(Guid.CreateVersion7());
        OutboxEnqueueResult enqueued = await _store.EnqueueAsync(draft, TestContext.Current.CancellationToken);

        OutboxTransitionResult result = await _store.ApplyDeliveryOutcomeAsync(
            AprsAddress.FromCallsign(new Callsign("K1IMP", 7)),
            enqueued.MessageNumber!.Value,
            MessageDeliveryStatus.Acknowledged,
            TestContext.Current.CancellationToken);

        Assert.Equal(OutboxTransitionOutcome.UnknownMessage, result.Outcome);
        Assert.Single(await _store.GetOpenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NumberReservation_BlocksReuseUntilTheGuardExpires_ThenTheNumberIsFreeAgain()
    {
        OutboxEnqueueResult first = await _store.EnqueueAsync(
            CreateDraft(Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        await _store.ApplyDeliveryOutcomeAsync(
            Peer, first.MessageNumber!.Value, MessageDeliveryStatus.Acknowledged,
            TestContext.Current.CancellationToken);

        OutboxEnqueueResult guarded = await _store.EnqueueAsync(
            CreateDraft(Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        Assert.Equal(2, guarded.MessageNumber);

        _clock.Advance(OutboxMessageStore.NumberReuseGuard + TimeSpan.FromSeconds(1));
        await _store.CleanupAsync(TestContext.Current.CancellationToken);

        OutboxEnqueueResult reused = await _store.EnqueueAsync(
            CreateDraft(Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        Assert.Equal(1, reused.MessageNumber);

        await using AppDbContext context = _database.CreateContext();
        Assert.Empty(await context.OutboxMessages
            .Where(m => m.Id == first.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await context.OutboxMessages.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EnqueueAsync_WhenEveryNumberIsReserved_QueuesInsteadOfReusingOne()
    {
        OutboxMessageStore limited = new(
            _writer,
            new SqliteRuntimeReader(_database.Factory),
            _clock,
            NullLogger<OutboxMessageStore>.Instance,
            maximumWireNumber: 1);

        OutboxEnqueueResult first = await limited.EnqueueAsync(
            CreateDraft(Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        OutboxEnqueueResult blocked = await limited.EnqueueAsync(
            CreateDraft(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        Assert.Equal(1, first.MessageNumber);
        Assert.Null(blocked.MessageNumber);
        Assert.Equal(MessageDeliveryStatus.Queued, blocked.Status);

        await limited.ApplyDeliveryOutcomeAsync(
            Peer, first.MessageNumber!.Value, MessageDeliveryStatus.Acknowledged,
            TestContext.Current.CancellationToken);
        _clock.Advance(OutboxMessageStore.NumberReuseGuard + TimeSpan.FromSeconds(1));
        await limited.CleanupAsync(TestContext.Current.CancellationToken);

        OutboxTransitionResult promoted = await limited.TryReserveNumberAsync(
            blocked.Id, _clock.GetUtcNow(), _clock.GetUtcNow(), TestContext.Current.CancellationToken);

        Assert.Equal(OutboxTransitionOutcome.Applied, promoted.Outcome);
        Assert.Equal(1, promoted.Message!.MessageNumber);
        Assert.Equal(MessageDeliveryStatus.Pending, promoted.Message.Status);
    }

    [Fact]
    public async Task ExpireAsync_MarksOpenWorkExpired_AndRestorableQueriesSkipIt()
    {
        OutboxMessageDraft expiring = CreateDraft(Guid.CreateVersion7(), expiresAt: _clock.GetUtcNow().AddSeconds(-1));
        await _store.EnqueueAsync(expiring, TestContext.Current.CancellationToken);

        IReadOnlyList<OutboxMessageSnapshot> expired = await _store.ExpireAsync(
            _clock.GetUtcNow(), TestContext.Current.CancellationToken);
        IReadOnlyList<OutboxMessageSnapshot> open = await _store.GetOpenAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<OutboxMessageSnapshot> due = await _store.GetDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal(MessageDeliveryStatus.Expired, Assert.Single(expired).Status);
        Assert.Empty(open);
        Assert.Empty(due);
    }

    [Fact]
    public async Task CleanupAsync_RemovesTerminalRowsAfterTheGuard_ButKeepsPendingWork()
    {
        OutboxEnqueueResult pending = await _store.EnqueueAsync(
            CreateDraft(Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        OutboxEnqueueResult terminal = await _store.EnqueueAsync(
            CreateDraft(Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        await _store.ApplyDeliveryOutcomeAsync(
            Peer, terminal.MessageNumber!.Value, MessageDeliveryStatus.Rejected,
            TestContext.Current.CancellationToken);
        await _store.RecordAttemptAsync(
            pending.Id, _clock.GetUtcNow().AddMinutes(1), TestContext.Current.CancellationToken);

        int immediately = await _store.CleanupAsync(TestContext.Current.CancellationToken);
        _clock.Advance(OutboxMessageStore.NumberReuseGuard + TimeSpan.FromSeconds(1));
        int afterGuard = await _store.CleanupAsync(TestContext.Current.CancellationToken);

        await using AppDbContext context = _database.CreateContext();
        OutboxMessageRecord remaining = Assert.Single(context.OutboxMessages);
        Assert.Equal(pending.Id, remaining.Id);
        Assert.Equal(0, immediately);
        Assert.Equal(1, afterGuard);
    }

    [Fact]
    public async Task MessageCommitted_FailingSubscriber_DoesNotFailTheCommandOrOtherSubscribers()
    {
        List<Guid> delivered = [];
        _store.MessageCommitted += (_, _) => throw new InvalidOperationException("subscriber failure");
        _store.MessageCommitted += (_, args) => delivered.Add(args.Message.Id);

        OutboxEnqueueResult enqueued = await _store.EnqueueAsync(
            CreateDraft(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        Assert.Contains(enqueued.Id, delivered);
    }

    private OutboxMessageDraft CreateDraft(Guid id, DateTimeOffset? expiresAt = null) => new(
        id,
        Peer,
        "Hello",
        _clock.GetUtcNow(),
        expiresAt ?? _clock.GetUtcNow().AddMinutes(15),
        PortId: null,
        _clock.GetUtcNow().AddSeconds(30));
}

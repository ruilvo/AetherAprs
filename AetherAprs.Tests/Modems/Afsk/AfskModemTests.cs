// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Afsk;
using AetherAprs.Services.Platform;
using Geo;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Modems.Afsk;

public sealed class AfskModemTests
{
    private static readonly Callsign Source = new("N0CALL");
    private static readonly Callsign Dest = new("APZ001");

    private static PositionPacket CreatePositionPacket() => new()
    {
        Source = Source,
        Destination = Dest,
        Location = new Coordinate(38.5, -9.10),
        Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
        Precision = 2
    };

    private static IAudioStreamService CreateAudioStream(int sampleRate = 48000)
    {
        var stream = Substitute.For<IAudioStreamService>();
        stream.SampleRate.Returns(sampleRate);
        return stream;
    }

    private static IAudioPlaybackService CreatePlayback(
        int sampleRate = 48000,
        Action<ReadOnlyMemory<float>>? onPlay = null)
    {
        var playback = Substitute.For<IAudioPlaybackService>();
        playback.SampleRate.Returns(sampleRate);
        playback.PlayAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                onPlay?.Invoke(callInfo.Arg<ReadOnlyMemory<float>>());
                return Task.CompletedTask;
            });
        return playback;
    }

    private static AfskModem CreateModem(
        AfskSettings settings,
        IAudioStreamService? audioStream = null,
        IAudioPlaybackService? playback = null,
        IPttService? ptt = null,
        TxTiming? txTiming = null)
    {
        return new AfskModem(
            audioStream ?? CreateAudioStream(),
            playback ?? CreatePlayback(),
            ptt ?? Substitute.For<IPttService>(),
            settings,
            txTiming: txTiming ?? new TxTiming(new FixedRandom(0)));
    }

    [Fact]
    public async Task SendAsync_PttModeNone_DoesNotKeyPtt()
    {
        var ptt = Substitute.For<IPttService>();
        var modem = CreateModem(new AfskSettings { PttMode = PttMode.None }, ptt: ptt);

        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);

        ptt.DidNotReceive().Engage();
        ptt.DidNotReceive().Release();
    }

    [Fact]
    public async Task SendAsync_PttModeSpeakerMic_EngagesThenReleasesPtt()
    {
        var order = new List<string>();
        var ptt = Substitute.For<IPttService>();
        ptt.When(p => p.Engage()).Do(_ => order.Add("engage"));
        ptt.When(p => p.Release()).Do(_ => order.Add("release"));

        var modem = CreateModem(new AfskSettings { PttMode = PttMode.SpeakerMic }, ptt: ptt);

        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);

        Assert.Equal(["engage", "release"], order);
    }

    [Fact]
    public async Task SendAsync_PlaysModulatedSamples_AtConfiguredLevel()
    {
        ReadOnlyMemory<float>? played = null;
        var playback = CreatePlayback(onPlay: samples => played = samples);
        var modem = CreateModem(
            new AfskSettings { PttMode = PttMode.None, AudioLevel = 0.9 },
            playback: playback);

        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);

        Assert.NotNull(played);
        Assert.False(played.Value.IsEmpty);

        // Peak amplitude must not exceed the configured level.
        foreach (var sample in played.Value.Span)
        {
            Assert.InRange(Math.Abs(sample), 0.0, 0.9001);
        }
    }

    [Fact]
    public async Task SendAsync_KeysPttBeforePlayback_AndReleasesAfterTail()
    {
        var order = new List<string>();
        var ptt = Substitute.For<IPttService>();
        ptt.When(p => p.Engage()).Do(_ => order.Add("engage"));
        ptt.When(p => p.Release()).Do(_ => order.Add("release"));

        var playback = CreatePlayback(onPlay: _ => order.Add("play"));
        var modem = CreateModem(
            new AfskSettings { PttMode = PttMode.SpeakerMic, TxDelayMs = 1, TxTailMs = 1 },
            playback: playback,
            ptt: ptt);

        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);

        Assert.Equal(["engage", "play", "release"], order);
    }

    [Fact]
    public async Task SendAsync_PlaybackThrows_StillReleasesPtt()
    {
        var ptt = Substitute.For<IPttService>();
        var playback = Substitute.For<IAudioPlaybackService>();
        playback.SampleRate.Returns(48000);
        playback.PlayAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        var modem = CreateModem(new AfskSettings { PttMode = PttMode.SpeakerMic }, playback: playback, ptt: ptt);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken));

        ptt.Received(1).Engage();
        ptt.Received(1).Release();
    }

    [Fact]
    public async Task SendAsync_NullPacket_Throws()
    {
        var modem = CreateModem(new AfskSettings());
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => modem.SendAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_AfterDispose_Throws()
    {
        var modem = CreateModem(new AfskSettings());
        await modem.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_RecentlyTransmittedFrame_IsSuppressed()
    {
        var playback = Substitute.For<IAudioPlaybackService>();
        playback.SampleRate.Returns(48000);
        playback.PlayAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var modem = CreateModem(new AfskSettings { PttMode = PttMode.None }, playback: playback);

        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);
        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);

        await playback.Received(1).PlayAsync(
            Arg.Any<ReadOnlyMemory<float>>(),
            Arg.Any<CancellationToken>());
    }
}

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
        IAudioStreamService stream = Substitute.For<IAudioStreamService>();
        stream.SampleRate.Returns(sampleRate);
        return stream;
    }

    private static IAudioPlaybackService CreatePlayback(
        int sampleRate = 48000,
        Action<ReadOnlyMemory<float>>? onPlay = null)
    {
        IAudioPlaybackService playback = Substitute.For<IAudioPlaybackService>();
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
        TxTiming? txTiming = null) => new(
            audioStream ?? CreateAudioStream(),
            playback ?? CreatePlayback(),
            ptt ?? Substitute.For<IPttService>(),
            settings,
            txTiming: txTiming ?? new TxTiming(new FixedRandom(0)));

    [Fact]
    public async Task DisposeAsync_DoesNotDisposeTheSharedAudioStream()
    {
        IAudioStreamService audioStream = CreateAudioStream();
        AfskModem modem = CreateModem(new AfskSettings(), audioStream: audioStream);

        await modem.DisposeAsync();

        // The audio capture service is a shared application resource. A modem that
        // disposed it would break every later session that reuses it.
        audioStream.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task SendAsync_PttModeVox_DoesNotKeyPtt()
    {
        IPttService ptt = Substitute.For<IPttService>();
        AfskModem modem = CreateModem(new AfskSettings { PttMode = PttMode.Vox }, ptt: ptt);

        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);

        ptt.DidNotReceive().TryEngage();
        ptt.DidNotReceive().Release();
    }

    [Fact]
    public async Task SendAsync_PttModeSpeakerMic_KeysAndReleasesTheTransmitter()
    {
        List<string> order = [];
        IPttService ptt = Substitute.For<IPttService>();
        ptt.When(p => p.TryEngage()).Do(_ => order.Add("engage"));
        ptt.When(p => p.Release()).Do(_ => order.Add("release"));

        AfskModem modem = CreateModem(new AfskSettings { PttMode = PttMode.SpeakerMic }, ptt: ptt);

        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);

        Assert.Equal(["engage", "release"], order);
    }

    [Fact]
    public async Task SendAsync_AudioRouteBlocksKeying_ThrowsWithoutPlayingAndWithoutReleasing()
    {
        List<string> order = [];
        IPttService ptt = Substitute.For<IPttService>();
        ptt.TryEngage().Returns(AudioTransmitBlockReason.FocusLost);
        ptt.When(p => p.Release()).Do(_ => order.Add("release"));

        IAudioPlaybackService playback = CreatePlayback(onPlay: _ => order.Add("play"));
        AfskModem modem = CreateModem(new AfskSettings { PttMode = PttMode.SpeakerMic }, playback: playback, ptt: ptt);

        AudioRouteBlockedException exception = await Assert.ThrowsAsync<AudioRouteBlockedException>(
            () => modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken));

        // The refusal is terminal for this transmission: no audio is played into a
        // route that lost focus, and nothing was keyed that would need releasing.
        Assert.Equal(AudioTransmitBlockReason.FocusLost, exception.BlockReason);
        Assert.Empty(order);
        ptt.Received(1).TryEngage();
        ptt.DidNotReceive().Release();
    }

    [Fact]
    public async Task SendAsync_PlaysModulatedSamples_AtConfiguredLevel()
    {
        ReadOnlyMemory<float>? played = null;
        IAudioPlaybackService playback = CreatePlayback(onPlay: samples => played = samples);
        AfskModem modem = CreateModem(
            new AfskSettings { PttMode = PttMode.Vox },
            playback: playback);

        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);

        Assert.NotNull(played);
        Assert.False(played.Value.IsEmpty);

        // Peak amplitude must not exceed the configured level.
        foreach (float sample in played.Value.Span)
        {
            Assert.InRange(Math.Abs(sample), 0.0, 0.9001);
        }
    }

    [Fact]
    public async Task SendAsync_KeysPttBeforePlayback_AndReleasesAfterTail()
    {
        List<string> order = [];
        IPttService ptt = Substitute.For<IPttService>();
        ptt.When(p => p.TryEngage()).Do(_ => order.Add("engage"));
        ptt.When(p => p.Release()).Do(_ => order.Add("release"));

        IAudioPlaybackService playback = CreatePlayback(onPlay: _ => order.Add("play"));
        AfskModem modem = CreateModem(
            new AfskSettings { PttMode = PttMode.SpeakerMic, TxDelayMs = 1, TxTailMs = 1 },
            playback: playback,
            ptt: ptt);

        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);

        Assert.Equal(["engage", "play", "release"], order);
    }

    [Fact]
    public async Task SendAsync_PlaybackThrows_StillReleasesPtt()
    {
        IPttService ptt = Substitute.For<IPttService>();
        IAudioPlaybackService playback = Substitute.For<IAudioPlaybackService>();
        playback.SampleRate.Returns(48000);
        playback.PlayAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        AfskModem modem = CreateModem(new AfskSettings { PttMode = PttMode.SpeakerMic }, playback: playback, ptt: ptt);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken));

        ptt.Received(1).TryEngage();
        ptt.Received(1).Release();
    }

    [Fact]
    public async Task SendAsync_NullPacket_Throws()
    {
        AfskModem modem = CreateModem(new AfskSettings());
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => modem.SendAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_AfterDispose_Throws()
    {
        AfskModem modem = CreateModem(new AfskSettings());
        await modem.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_RecentlyTransmittedFrame_IsSuppressed()
    {
        IAudioPlaybackService playback = Substitute.For<IAudioPlaybackService>();
        playback.SampleRate.Returns(48000);
        playback.PlayAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        AfskModem modem = CreateModem(new AfskSettings { PttMode = PttMode.Vox }, playback: playback);

        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);
        await modem.SendAsync(CreatePositionPacket(), TestContext.Current.CancellationToken);

        await playback.Received(1).PlayAsync(
            Arg.Any<ReadOnlyMemory<float>>(),
            Arg.Any<CancellationToken>());
    }
}

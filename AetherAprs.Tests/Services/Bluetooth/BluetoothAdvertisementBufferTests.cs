// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using AetherAprs.Services.Bluetooth;
using Xunit;

namespace AetherAprs.Tests.Services.Bluetooth;

public sealed class BluetoothAdvertisementBufferTests
{
    [Fact]
    public async Task Publish_WhenTheConsumerFallsBehind_KeepsTheNewestAdvertisements()
    {
        var buffer = new BluetoothAdvertisementBuffer(capacity: 3);

        for (var index = 1; index <= 5; index++)
        {
            buffer.Publish(Ad($"DEVICE-{index}"));
        }

        Assert.Equal(3, buffer.Capacity);
        Assert.Equal(2, buffer.DroppedCount);

        var retained = new[]
        {
            await ReadAsync(buffer),
            await ReadAsync(buffer),
            await ReadAsync(buffer)
        };

        Assert.Equal(["DEVICE-3", "DEVICE-4", "DEVICE-5"], Array.ConvertAll(retained, ad => ad.Address));
    }

    [Fact]
    public async Task Publish_BelowCapacity_DropsNothing()
    {
        var buffer = new BluetoothAdvertisementBuffer(capacity: 3);

        Assert.True(buffer.Publish(Ad("DEVICE-1")));
        Assert.True(buffer.Publish(Ad("DEVICE-2")));

        Assert.Equal(0, buffer.DroppedCount);
        Assert.Equal("DEVICE-1", (await ReadAsync(buffer)).Address);
        Assert.Equal("DEVICE-2", (await ReadAsync(buffer)).Address);
    }

    [Fact]
    public void Publish_AfterCompletion_IsRefused()
    {
        var buffer = new BluetoothAdvertisementBuffer();

        buffer.Complete();

        Assert.False(buffer.Publish(Ad("DEVICE-1")));
    }

    [Fact]
    public async Task Reader_EndsWhenTheBufferCompletes()
    {
        var buffer = new BluetoothAdvertisementBuffer();
        buffer.Publish(Ad("DEVICE-1"));
        buffer.Complete();

        Assert.Equal("DEVICE-1", (await ReadAsync(buffer)).Address);
        await Assert.ThrowsAsync<ChannelClosedException>(async () =>
            await buffer.Reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Complete_WithAFailure_SurfacesItOnTheReader()
    {
        var buffer = new BluetoothAdvertisementBuffer();
        var failure = new InvalidOperationException("BLE scan failed: TooManyMatches");
        buffer.Complete(failure);

        var thrown = await Assert.ThrowsAsync<ChannelClosedException>(async () =>
            await buffer.Reader.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown.InnerException);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithANonPositiveCapacity_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BluetoothAdvertisementBuffer(capacity));
    }

    private static BluetoothLeAdvertisement Ad(string address) => new(address, address, -60, []);

    private static async Task<BluetoothLeAdvertisement> ReadAsync(BluetoothAdvertisementBuffer buffer) =>
        await buffer.Reader.ReadAsync(TestContext.Current.CancellationToken);
}

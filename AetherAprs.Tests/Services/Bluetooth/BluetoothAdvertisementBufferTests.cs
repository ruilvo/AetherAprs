// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Channels;
using System.Threading.Tasks;

using AetherAprs.Configuration;
using AetherAprs.Services.Bluetooth;

using Xunit;

namespace AetherAprs.Tests.Services.Bluetooth;

public sealed class BluetoothAdvertisementBufferTests
{
    [Fact]
    public async Task Publish_WhenTheConsumerFallsBehind_KeepsTheNewestAdvertisements()
    {
        BluetoothAdvertisementBuffer buffer = new(capacity: 3);

        for (int index = 1; index <= 5; index++)
        {
            buffer.Publish(Ad($"DEVICE-{index}"));
        }

        Assert.Equal(3, buffer.Capacity);
        Assert.Equal(2, buffer.DroppedCount);

        BluetoothLeAdvertisement[] retained =
        [
            await ReadAsync(buffer),
            await ReadAsync(buffer),
            await ReadAsync(buffer)
        ];

        Assert.Equal(["DEVICE-3", "DEVICE-4", "DEVICE-5"], Array.ConvertAll(retained, ad => ad.Address));
    }

    [Fact]
    public async Task Publish_BelowCapacity_DropsNothing()
    {
        BluetoothAdvertisementBuffer buffer = new(capacity: 3);

        Assert.Equal(BluetoothAdvertisementPublishResult.Buffered, buffer.Publish(Ad("DEVICE-1")));
        Assert.Equal(BluetoothAdvertisementPublishResult.Buffered, buffer.Publish(Ad("DEVICE-2")));

        Assert.Equal(0, buffer.DroppedCount);
        Assert.Equal("DEVICE-1", (await ReadAsync(buffer)).Address);
        Assert.Equal("DEVICE-2", (await ReadAsync(buffer)).Address);
    }

    [Fact]
    public void Publish_WhenTheAdvertisementNamesNoService_IsBuffered()
    {
        // Most KISS-over-BLE bridges reveal their services only after connecting,
        // so an advertisement without service UUIDs must still reach the picker.
        BluetoothAdvertisementBuffer buffer = FilteredBuffer();

        Assert.Equal(BluetoothAdvertisementPublishResult.Buffered, buffer.Publish(Ad("BRIDGE")));
        Assert.Equal(0, buffer.RejectedCount);
    }

    [Fact]
    public void Publish_WhenTheAdvertisementNamesASupportedService_IsBuffered()
    {
        BluetoothAdvertisementBuffer buffer = FilteredBuffer();

        BluetoothAdvertisementPublishResult result = buffer.Publish(Ad("TNC", BluetoothLeKissTransportSettings.DefaultServiceUuid));

        Assert.Equal(BluetoothAdvertisementPublishResult.Buffered, result);
        Assert.Equal(0, buffer.RejectedCount);
    }

    [Fact]
    public void Publish_WhenTheAdvertisementNamesOnlyForeignServices_IsFiltered()
    {
        BluetoothAdvertisementBuffer buffer = FilteredBuffer();

        BluetoothAdvertisementPublishResult result = buffer.Publish(Ad("HEADPHONES", ForeignServiceUuid));

        Assert.Equal(BluetoothAdvertisementPublishResult.Filtered, result);
        Assert.Equal(1, buffer.RejectedCount);
        Assert.Equal(0, buffer.DroppedCount);
    }

    [Fact]
    public async Task Publish_WhenForeignAdvertisementsCompeteForCapacity_KeepsOnlySupportedOnes()
    {
        BluetoothAdvertisementBuffer buffer = new(
            capacity: 2,
            filter: BluetoothLeServiceFilter.SupportedTransports);

        buffer.Publish(Ad("TNC-1", BluetoothLeKissTransportSettings.DefaultServiceUuid));
        buffer.Publish(Ad("HEADPHONES", ForeignServiceUuid));
        buffer.Publish(Ad("TNC-2", BluetoothLeKissTransportSettings.LoRaAprsTrackerServiceUuid));
        buffer.Publish(Ad("SPEAKER", ForeignServiceUuid));
        buffer.Publish(Ad("TNC-3", BluetoothLeKissTransportSettings.DefaultServiceUuid));

        Assert.Equal(2, buffer.RejectedCount);
        Assert.Equal(1, buffer.DroppedCount);
        Assert.Equal("TNC-2", (await ReadAsync(buffer)).Address);
        Assert.Equal("TNC-3", (await ReadAsync(buffer)).Address);
    }

    [Fact]
    public void Publish_AfterCompletion_IsRefused()
    {
        BluetoothAdvertisementBuffer buffer = new();

        buffer.Complete();

        Assert.Equal(BluetoothAdvertisementPublishResult.Completed, buffer.Publish(Ad("DEVICE-1")));
    }

    [Fact]
    public async Task Reader_EndsWhenTheBufferCompletes()
    {
        BluetoothAdvertisementBuffer buffer = new();
        buffer.Publish(Ad("DEVICE-1"));
        buffer.Complete();

        Assert.Equal("DEVICE-1", (await ReadAsync(buffer)).Address);
        await Assert.ThrowsAsync<ChannelClosedException>(async () =>
            await buffer.Reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Complete_WithAFailure_SurfacesItOnTheReader()
    {
        BluetoothAdvertisementBuffer buffer = new();
        InvalidOperationException failure = new("BLE scan failed: TooManyMatches");
        buffer.Complete(failure);

        ChannelClosedException thrown = await Assert.ThrowsAsync<ChannelClosedException>(async () =>
            await buffer.Reader.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown.InnerException);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithANonPositiveCapacity_Throws(int capacity) => Assert.Throws<ArgumentOutOfRangeException>(() => new BluetoothAdvertisementBuffer(capacity));

    private static readonly Guid ForeignServiceUuid = Guid.Parse("00001812-0000-1000-8000-00805F9B34FB");

    private static BluetoothAdvertisementBuffer FilteredBuffer() =>
        new(filter: BluetoothLeServiceFilter.SupportedTransports);

    private static BluetoothLeAdvertisement Ad(string address, params Guid[] serviceUuids) =>
        new(address, address, -60, serviceUuids);

    private static async Task<BluetoothLeAdvertisement> ReadAsync(BluetoothAdvertisementBuffer buffer) =>
        await buffer.Reader.ReadAsync(TestContext.Current.CancellationToken);
}

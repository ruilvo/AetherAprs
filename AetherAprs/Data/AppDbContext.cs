// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Converters;
using AetherAprs.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;

namespace AetherAprs.Data;

/// <summary>
/// The application's SQLite model.
/// </summary>
/// <remarks>
/// EF Core 11 still marks its <see cref="DbContext"/> constructors as requiring
/// unreferenced code, because its model building uses reflection. This context is
/// a closed model: every entity is a public type listed in <see cref="OnModelCreating"/>,
/// the mappings are explicit, and the model is never built from assembly scanning.
/// The suppression records that the reflective targets are all rooted rather than
/// hiding a dynamic model.
/// </remarks>
[UnconditionalSuppressMessage(
    "Trimming",
    "IL2026",
    Justification = "The EF model is a closed set of statically referenced entity types configured explicitly in OnModelCreating.")]
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string DatabaseFileName = "aetheraprs.db";

    public DbSet<PortRecord> Ports => Set<PortRecord>();

    public DbSet<PacketRecord> Packets => Set<PacketRecord>();

    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

    public DbSet<BeaconConfigRecord> BeaconConfigs => Set<BeaconConfigRecord>();

    public DbSet<BeaconingStateRecord> BeaconingState => Set<BeaconingStateRecord>();

    public static DbContextOptions<AppDbContext> CreateOptions(string databasePath, bool pooling = true)
    {
        // Pooling keeps the SQLite file handle open after all contexts are
        // disposed, which prevents deleting database files. Production wants the
        // pool; the test suite disables it so its temporary databases can be
        // removed.
        string connectionString = pooling
            ? $"Data Source={databasePath}"
            : $"Data Source={databasePath};Pooling=False";
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortRecord>(entity =>
        {
            entity.ToTable("Ports");
            entity.HasKey(port => port.Id);
            entity.Property(port => port.Name).IsRequired().HasMaxLength(200);
            entity.Property(port => port.Type).HasMaxLength(16);
            entity.OwnsOne(port => port.AprsIs, aprsIs =>
            {
                aprsIs.Property(settings => settings.Server).HasMaxLength(255);
                aprsIs.Property(settings => settings.Passcode).HasMaxLength(32);
                aprsIs.Property(settings => settings.Filter).HasMaxLength(256);
            });
            entity.OwnsOne(port => port.Kiss, kiss =>
            {
                kiss.Property(settings => settings.Transport).IsRequired().HasMaxLength(16);
                kiss.OwnsOne(settings => settings.Tcp, tcp => tcp.Property(transport => transport.Host).HasMaxLength(255));
                kiss.OwnsOne(settings => settings.BluetoothClassic, classic =>
                {
                    classic.Property(transport => transport.DeviceAddress).HasMaxLength(64);
                    classic.Property(transport => transport.DeviceName).HasMaxLength(200);
                });
                kiss.OwnsOne(settings => settings.BluetoothLe, ble =>
                {
                    ble.Property(transport => transport.DeviceAddress).HasMaxLength(64);
                    ble.Property(transport => transport.DeviceName).HasMaxLength(200);
                });
            });
            entity.OwnsOne(port => port.Afsk, afsk =>
            {
                afsk.Property(settings => settings.PttMode).IsRequired();
                afsk.Property(settings => settings.TxDelayMs).IsRequired();
                afsk.Property(settings => settings.TxTailMs).IsRequired();
                afsk.Property(settings => settings.SlotTimeMs).IsRequired();
                afsk.Property(settings => settings.Persist).IsRequired();
                afsk.Property(settings => settings.AudioLevel).IsRequired();
                afsk.Property(settings => settings.SquelchThreshold).IsRequired();
            });
        });

        modelBuilder.Entity<PacketRecord>(entity =>
        {
            entity.ToTable("Packets");
            entity.HasKey(packet => packet.Id);
            entity.Property(packet => packet.SourceBase).IsRequired().HasMaxLength(9);
            entity.Property(packet => packet.SourceSsid).IsRequired();
            entity.Property(packet => packet.DestinationBase).IsRequired().HasMaxLength(9);
            entity.Property(packet => packet.DestinationSsid).IsRequired();
            entity.Property(packet => packet.PacketType).IsRequired().HasMaxLength(16);
            entity.Property(packet => packet.RawInfo).IsRequired().HasMaxLength(256);
            entity.Property(packet => packet.RawFrameHex).IsRequired().HasMaxLength(512);
            entity.Property(packet => packet.RawPath).IsRequired().HasMaxLength(256);
            entity.Property(packet => packet.IsOutbound).IsRequired();

            // Indexes for efficient querying
            entity.HasIndex(packet => new { packet.SourceBase, packet.SourceSsid });
            entity.Property(packet => packet.Timestamp)
                .HasConversion(new UtcDateTimeValueConverter());
            entity.Property(packet => packet.PacketTimestamp)
                .HasConversion(new UtcDateTimeValueConverter());
            entity.HasIndex(packet => packet.Timestamp);
            entity.HasIndex(packet => packet.PacketType);
            entity.HasIndex(packet => new { packet.SourceBase, packet.SourceSsid, packet.Timestamp });
            entity.HasIndex(packet => packet.IsOutbound);
            entity.HasIndex(packet => packet.PortId);

            // Position data
            entity.OwnsOne(packet => packet.Position, position =>
            {
                // Store Coordinate as "latitude,longitude" string using value converter
                position.Property(p => p.Location)
                    .HasConversion(new CoordinateValueConverter())
                    .HasColumnName("Location")
                    .IsRequired();

                position.Property(p => p.SymbolTable).IsRequired();
                position.Property(p => p.SymbolCode).IsRequired();
                position.Property(p => p.Comment).HasMaxLength(43);
            });

            // Message data
            entity.OwnsOne(packet => packet.Message, message =>
            {
                message.Property(m => m.AddresseeBase).IsRequired().HasMaxLength(9);
                message.Property(m => m.AddresseeSsid).IsRequired();
                message.Property(m => m.Text).IsRequired().HasMaxLength(67);
                message.Property(m => m.RetryCount).IsRequired();
                message.Property(m => m.NextRetryTime)
                    .HasConversion(new UtcDateTimeValueConverter());
                message.HasIndex(m => m.LogicalId);
            });

            // Status data
            entity.OwnsOne(packet => packet.Status, status => status.Property(s => s.Text).IsRequired().HasMaxLength(256));

            // Weather data
            entity.OwnsOne(packet => packet.Weather, weather =>
            {
                // All weather fields are nullable
            });

            // Telemetry data (T#nnn): five nullable analog channels + digital value
            entity.OwnsOne(packet => packet.Telemetry, telemetry => telemetry.Property(t => t.SequenceNumber).IsRequired());

            // Capabilities data
            entity.OwnsOne(packet => packet.Capabilities, capabilities => capabilities.Property(c => c.Text).IsRequired().HasMaxLength(256));

            // Object data (named position report)
            entity.OwnsOne(packet => packet.Object, obj =>
            {
                obj.Property(o => o.Name).IsRequired().HasMaxLength(9);
                obj.Property(o => o.IsLive).IsRequired();
                obj.Property(o => o.RawTimestamp).HasMaxLength(7);
                obj.Property(o => o.Location)
                    .HasConversion(new CoordinateValueConverter())
                    .HasColumnName("Object_Location")
                    .IsRequired();
                obj.Property(o => o.SymbolTable).IsRequired();
                obj.Property(o => o.SymbolCode).IsRequired();
                obj.Property(o => o.Comment).HasMaxLength(43);
            });

            // Item data (named position report)
            entity.OwnsOne(packet => packet.Item, item =>
            {
                item.Property(i => i.Name).IsRequired().HasMaxLength(9);
                item.Property(i => i.IsLive).IsRequired();
                item.Property(i => i.Location)
                    .HasConversion(new CoordinateValueConverter())
                    .HasColumnName("Item_Location")
                    .IsRequired();
                item.Property(i => i.SymbolTable).IsRequired();
                item.Property(i => i.SymbolCode).IsRequired();
                item.Property(i => i.Comment).HasMaxLength(43);
            });

            // Bulletin data (broadcast message)
            entity.OwnsOne(packet => packet.Bulletin, bulletin =>
            {
                bulletin.Property(b => b.Name).IsRequired().HasMaxLength(9);
                bulletin.Property(b => b.Text).IsRequired().HasMaxLength(67);
                bulletin.Property(b => b.Number);
            });
        });

        modelBuilder.Entity<OutboxMessageRecord>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.AddresseeBase).IsRequired().HasMaxLength(9);
            entity.Property(message => message.AddresseeSsid).IsRequired();
            entity.Property(message => message.Text).IsRequired().HasMaxLength(67);
            entity.Property(message => message.MessageNumber);
            entity.Property(message => message.Status).IsRequired();
            entity.Property(message => message.AttemptCount).IsRequired();
            entity.Property(message => message.CreatedAt)
                .HasConversion(new UtcDateTimeValueConverter());
            entity.Property(message => message.ExpiresAt)
                .HasConversion(new UtcDateTimeValueConverter());
            entity.Property(message => message.NextAttemptAt)
                .HasConversion(new UtcDateTimeValueConverter());
            entity.Property(message => message.NumberReleaseAt)
                .HasConversion(new UtcDateTimeValueConverter());
            entity.Property(message => message.UpdatedAt)
                .HasConversion(new UtcDateTimeValueConverter());
            entity.HasIndex(message => new { message.AddresseeBase, message.AddresseeSsid, message.MessageNumber });
            entity.HasIndex(message => message.Status);
            entity.HasIndex(message => message.ExpiresAt);
        });

        modelBuilder.Entity<BeaconConfigRecord>(entity =>
        {
            entity.ToTable("BeaconConfigs");
            entity.HasKey(config => config.Mode);
            entity.Property(config => config.DisplayName).IsRequired().HasMaxLength(64);
            entity.Property(config => config.BeaconComment).HasMaxLength(43);
        });

        modelBuilder.Entity<BeaconingStateRecord>(entity =>
        {
            entity.ToTable("BeaconingState");
            entity.HasKey(state => state.Id);
        });
    }
}

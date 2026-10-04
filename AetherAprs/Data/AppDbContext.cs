// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Converters;
using AetherAprs.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AetherAprs.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string DatabaseFileName = "aetheraprs.db";

    public DbSet<PortRecord> Ports => Set<PortRecord>();

    public DbSet<PacketRecord> Packets => Set<PacketRecord>();

    public DbSet<BeaconConfigRecord> BeaconConfigs => Set<BeaconConfigRecord>();

    public DbSet<BeaconingStateRecord> BeaconingState => Set<BeaconingStateRecord>();

    public static DbContextOptions<AppDbContext> CreateOptions(string databasePath)
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath}")
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
                kiss.OwnsOne(settings => settings.Tcp, tcp =>
                {
                    tcp.Property(transport => transport.Host).HasMaxLength(255);
                });
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
            entity.Property(packet => packet.IsOutbound).IsRequired();

            // Indexes for efficient querying
            entity.HasIndex(packet => new { packet.SourceBase, packet.SourceSsid });
            entity.HasIndex(packet => packet.Timestamp);
            entity.HasIndex(packet => packet.PacketType);
            entity.HasIndex(packet => new { packet.SourceBase, packet.SourceSsid, packet.Timestamp });
            entity.HasIndex(packet => packet.IsOutbound);

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
            });

            // Status data
            entity.OwnsOne(packet => packet.Status, status =>
            {
                status.Property(s => s.Text).IsRequired().HasMaxLength(256);
            });

            // Weather data
            entity.OwnsOne(packet => packet.Weather, weather =>
            {
                // All weather fields are nullable
            });
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

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.EntityFrameworkCore;

namespace AetherAprs.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string DatabaseFileName = "aetheraprs.db";

    public DbSet<PortRecord> Ports => Set<PortRecord>();

    public DbSet<MessageRecord> Messages => Set<MessageRecord>();

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
            entity.Property(port => port.SymbolTableCharacter).HasMaxLength(1);
            entity.Property(port => port.SymbolCodeCharacter).HasMaxLength(1);
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

        modelBuilder.Entity<MessageRecord>(entity =>
        {
            entity.ToTable("Messages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Peer).IsRequired().HasMaxLength(16);
            entity.Property(message => message.Text).IsRequired().HasMaxLength(67);
            entity.HasIndex(message => message.Peer);
            entity.HasIndex(message => message.Timestamp);
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

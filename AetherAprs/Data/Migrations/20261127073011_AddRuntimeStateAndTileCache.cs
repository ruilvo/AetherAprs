// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherAprs.Data.Migrations;

/// <inheritdoc />
public partial class AddRuntimeStateAndTileCache : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BeaconOutbox",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PortId = table.Column<Guid>(type: "TEXT", nullable: true),
                Latitude = table.Column<double>(type: "REAL", nullable: false),
                Longitude = table.Column<double>(type: "REAL", nullable: false),
                Altitude = table.Column<double>(type: "REAL", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                Error = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BeaconOutbox", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "MapTiles",
            columns: table => new
            {
                ZoomLevel = table.Column<int>(type: "INTEGER", nullable: false),
                TileColumn = table.Column<int>(type: "INTEGER", nullable: false),
                TileRow = table.Column<int>(type: "INTEGER", nullable: false),
                Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MapTiles", x => new { x.ZoomLevel, x.TileColumn, x.TileRow });
            });

        migrationBuilder.CreateTable(
            name: "PortSessionStates",
            columns: table => new
            {
                PortId = table.Column<Guid>(type: "TEXT", nullable: false),
                State = table.Column<int>(type: "INTEGER", nullable: false),
                LastReceiveUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                LastTransmitUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                NextRetryUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                Error = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PortSessionStates", x => x.PortId);
            });

        migrationBuilder.CreateTable(
            name: "SessionState",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                State = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SessionState", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BeaconOutbox_ExpiresAt",
            table: "BeaconOutbox",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_BeaconOutbox_Status",
            table: "BeaconOutbox",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_BeaconOutbox_Status_CreatedAt",
            table: "BeaconOutbox",
            columns: new[] { "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_MapTiles_UpdatedAt",
            table: "MapTiles",
            column: "UpdatedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BeaconOutbox");

        migrationBuilder.DropTable(
            name: "MapTiles");

        migrationBuilder.DropTable(
            name: "PortSessionStates");

        migrationBuilder.DropTable(
            name: "SessionState");
    }
}

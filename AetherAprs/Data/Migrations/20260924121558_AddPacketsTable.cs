// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherAprs.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPacketsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Packets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PacketType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PacketTimestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PortId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RawInfo = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    Altitude = table.Column<double>(type: "REAL", nullable: true),
                    Course = table.Column<double>(type: "REAL", nullable: true),
                    Speed = table.Column<double>(type: "REAL", nullable: true),
                    SymbolTable = table.Column<string>(type: "TEXT", maxLength: 1, nullable: true),
                    SymbolCode = table.Column<string>(type: "TEXT", maxLength: 1, nullable: true),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 43, nullable: true),
                    MessageAddressee = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    MessageText = table.Column<string>(type: "TEXT", maxLength: 67, nullable: true),
                    MessageNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    StatusText = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Temperature = table.Column<double>(type: "REAL", nullable: true),
                    WindSpeed = table.Column<double>(type: "REAL", nullable: true),
                    WindDirection = table.Column<double>(type: "REAL", nullable: true),
                    Humidity = table.Column<double>(type: "REAL", nullable: true),
                    Pressure = table.Column<double>(type: "REAL", nullable: true),
                    RainLastHour = table.Column<double>(type: "REAL", nullable: true),
                    RainLast24Hours = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Packets_PacketType",
                table: "Packets",
                column: "PacketType");

            migrationBuilder.CreateIndex(
                name: "IX_Packets_ReceivedAt",
                table: "Packets",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Packets_Source",
                table: "Packets",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_Packets_Source_ReceivedAt",
                table: "Packets",
                columns: new[] { "Source", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Packets");
        }
    }
}

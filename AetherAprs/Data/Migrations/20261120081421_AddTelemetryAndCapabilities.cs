// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherAprs.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryAndCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Capabilities_Text",
                table: "Packets",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Telemetry_Analog1",
                table: "Packets",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Telemetry_Analog2",
                table: "Packets",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Telemetry_Analog3",
                table: "Packets",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Telemetry_Analog4",
                table: "Packets",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Telemetry_Analog5",
                table: "Packets",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Telemetry_DigitalValue",
                table: "Packets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Telemetry_SequenceNumber",
                table: "Packets",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Capabilities_Text",
                table: "Packets");

            migrationBuilder.DropColumn(
                name: "Telemetry_Analog1",
                table: "Packets");

            migrationBuilder.DropColumn(
                name: "Telemetry_Analog2",
                table: "Packets");

            migrationBuilder.DropColumn(
                name: "Telemetry_Analog3",
                table: "Packets");

            migrationBuilder.DropColumn(
                name: "Telemetry_Analog4",
                table: "Packets");

            migrationBuilder.DropColumn(
                name: "Telemetry_Analog5",
                table: "Packets");

            migrationBuilder.DropColumn(
                name: "Telemetry_DigitalValue",
                table: "Packets");

            migrationBuilder.DropColumn(
                name: "Telemetry_SequenceNumber",
                table: "Packets");
        }
    }
}

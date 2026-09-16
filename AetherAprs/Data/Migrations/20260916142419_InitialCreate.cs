// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherAprs.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BeaconConfigs",
                columns: table => new
                {
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SlowIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    NormalIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    FastIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    FastSpeedThresholdKmh = table.Column<double>(type: "REAL", nullable: false),
                    SlowSpeedThresholdKmh = table.Column<double>(type: "REAL", nullable: false),
                    CourseChangeThresholdDegrees = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumDistanceMeters = table.Column<int>(type: "INTEGER", nullable: false),
                    BeaconComment = table.Column<string>(type: "TEXT", maxLength: 43, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeaconConfigs", x => x.Mode);
                });

            migrationBuilder.CreateTable(
                name: "BeaconingState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActiveMode = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeaconingState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Peer = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 67, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsOutbound = table.Column<bool>(type: "INTEGER", nullable: false),
                    MessageNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    PortId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRx = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsTx = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowOnMap = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ssid = table.Column<int>(type: "INTEGER", nullable: true),
                    SymbolTableCharacter = table.Column<string>(type: "TEXT", maxLength: 1, nullable: true),
                    SymbolCodeCharacter = table.Column<string>(type: "TEXT", maxLength: 1, nullable: true),
                    DynamicBeaconMode = table.Column<int>(type: "INTEGER", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    AprsIs_Server = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    AprsIs_ServerPort = table.Column<int>(type: "INTEGER", nullable: true),
                    AprsIs_Passcode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    AprsIs_Filter = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Kiss_Transport = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Kiss_Tcp_Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Kiss_Tcp_Port = table.Column<int>(type: "INTEGER", nullable: true),
                    Kiss_BluetoothClassic_DeviceAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Kiss_BluetoothClassic_DeviceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Kiss_BluetoothLe_DeviceAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Kiss_BluetoothLe_DeviceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Kiss_BluetoothLe_ServiceUuid = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kiss_BluetoothLe_RxCharacteristicUuid = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kiss_BluetoothLe_TxCharacteristicUuid = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Peer",
                table: "Messages",
                column: "Peer");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Timestamp",
                table: "Messages",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BeaconConfigs");

            migrationBuilder.DropTable(
                name: "BeaconingState");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Ports");
        }
    }
}

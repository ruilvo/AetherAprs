// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherAprs.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateWithCoordinate : Migration
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
                name: "Packets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceBase = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    SourceSsid = table.Column<byte>(type: "INTEGER", nullable: false),
                    DestinationBase = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    DestinationSsid = table.Column<byte>(type: "INTEGER", nullable: false),
                    PacketType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PacketTimestamp = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PortId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsOutbound = table.Column<bool>(type: "INTEGER", nullable: false),
                    RawInfo = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    Position_Altitude = table.Column<double>(type: "REAL", nullable: true),
                    Position_Course = table.Column<double>(type: "REAL", nullable: true),
                    Position_Speed = table.Column<double>(type: "REAL", nullable: true),
                    Position_SymbolTable = table.Column<byte>(type: "INTEGER", nullable: true),
                    Position_SymbolCode = table.Column<byte>(type: "INTEGER", nullable: true),
                    Position_Comment = table.Column<string>(type: "TEXT", maxLength: 43, nullable: true),
                    Message_AddresseeBase = table.Column<string>(type: "TEXT", maxLength: 9, nullable: true),
                    Message_AddresseeSsid = table.Column<byte>(type: "INTEGER", nullable: true),
                    Message_Text = table.Column<string>(type: "TEXT", maxLength: 67, nullable: true),
                    Message_Number = table.Column<int>(type: "INTEGER", nullable: true),
                    Message_DeliveryStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    Message_RetryCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Message_NextRetryTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status_Text = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Weather_Temperature = table.Column<double>(type: "REAL", nullable: true),
                    Weather_WindSpeed = table.Column<double>(type: "REAL", nullable: true),
                    Weather_WindDirection = table.Column<double>(type: "REAL", nullable: true),
                    Weather_Humidity = table.Column<double>(type: "REAL", nullable: true),
                    Weather_Pressure = table.Column<double>(type: "REAL", nullable: true),
                    Weather_RainLastHour = table.Column<double>(type: "REAL", nullable: true),
                    Weather_RainLast24Hours = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packets", x => x.Id);
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
                name: "IX_Packets_IsOutbound",
                table: "Packets",
                column: "IsOutbound");

            migrationBuilder.CreateIndex(
                name: "IX_Packets_PacketType",
                table: "Packets",
                column: "PacketType");

            migrationBuilder.CreateIndex(
                name: "IX_Packets_SourceBase_SourceSsid",
                table: "Packets",
                columns: new[] { "SourceBase", "SourceSsid" });

            migrationBuilder.CreateIndex(
                name: "IX_Packets_SourceBase_SourceSsid_Timestamp",
                table: "Packets",
                columns: new[] { "SourceBase", "SourceSsid", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Packets_Timestamp",
                table: "Packets",
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
                name: "Packets");

            migrationBuilder.DropTable(
                name: "Ports");
        }
    }
}

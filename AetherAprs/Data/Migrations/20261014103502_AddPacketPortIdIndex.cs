// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherAprs.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPacketPortIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Packets_PortId",
                table: "Packets",
                column: "PortId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Packets_PortId",
                table: "Packets");
        }
    }
}

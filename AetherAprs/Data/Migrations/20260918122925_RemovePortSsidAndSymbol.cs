// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherAprs.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemovePortSsidAndSymbol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ssid",
                table: "Ports");

            migrationBuilder.DropColumn(
                name: "SymbolCodeCharacter",
                table: "Ports");

            migrationBuilder.DropColumn(
                name: "SymbolTableCharacter",
                table: "Ports");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Ssid",
                table: "Ports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SymbolCodeCharacter",
                table: "Ports",
                type: "TEXT",
                maxLength: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SymbolTableCharacter",
                table: "Ports",
                type: "TEXT",
                maxLength: 1,
                nullable: true);
        }
    }
}

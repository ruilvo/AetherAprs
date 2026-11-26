// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherAprs.Data.Migrations;

/// <inheritdoc />
public partial class AddAfskSettingsToPorts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "Afsk_AudioLevel",
            table: "Ports",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Afsk_PttMode",
            table: "Ports",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "Afsk_SquelchThreshold",
            table: "Ports",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Afsk_TxDelayMs",
            table: "Ports",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Afsk_TxTailMs",
            table: "Ports",
            type: "INTEGER",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Afsk_AudioLevel",
            table: "Ports");

        migrationBuilder.DropColumn(
            name: "Afsk_PttMode",
            table: "Ports");

        migrationBuilder.DropColumn(
            name: "Afsk_SquelchThreshold",
            table: "Ports");

        migrationBuilder.DropColumn(
            name: "Afsk_TxDelayMs",
            table: "Ports");

        migrationBuilder.DropColumn(
            name: "Afsk_TxTailMs",
            table: "Ports");
    }
}

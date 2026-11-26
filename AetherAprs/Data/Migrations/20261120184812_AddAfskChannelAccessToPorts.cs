// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherAprs.Data.Migrations;

/// <inheritdoc />
public partial class AddAfskChannelAccessToPorts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Afsk_Persist",
            table: "Ports",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Afsk_SlotTimeMs",
            table: "Ports",
            type: "INTEGER",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Afsk_Persist",
            table: "Ports");

        migrationBuilder.DropColumn(
            name: "Afsk_SlotTimeMs",
            table: "Ports");
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherAprs.Data.Migrations;

/// <inheritdoc />
public partial class AddObjectItemAndBulletin : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Bulletin_Name",
            table: "Packets",
            type: "TEXT",
            maxLength: 9,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Bulletin_Number",
            table: "Packets",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Bulletin_Text",
            table: "Packets",
            type: "TEXT",
            maxLength: 67,
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "Item_Altitude",
            table: "Packets",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Item_Comment",
            table: "Packets",
            type: "TEXT",
            maxLength: 43,
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "Item_Course",
            table: "Packets",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "Item_IsLive",
            table: "Packets",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Item_Location",
            table: "Packets",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Item_Name",
            table: "Packets",
            type: "TEXT",
            maxLength: 9,
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "Item_Speed",
            table: "Packets",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<byte>(
            name: "Item_SymbolCode",
            table: "Packets",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<byte>(
            name: "Item_SymbolTable",
            table: "Packets",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "Object_Altitude",
            table: "Packets",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Object_Comment",
            table: "Packets",
            type: "TEXT",
            maxLength: 43,
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "Object_Course",
            table: "Packets",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "Object_IsLive",
            table: "Packets",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Object_Location",
            table: "Packets",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Object_Name",
            table: "Packets",
            type: "TEXT",
            maxLength: 9,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Object_RawTimestamp",
            table: "Packets",
            type: "TEXT",
            maxLength: 7,
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "Object_Speed",
            table: "Packets",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<byte>(
            name: "Object_SymbolCode",
            table: "Packets",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<byte>(
            name: "Object_SymbolTable",
            table: "Packets",
            type: "INTEGER",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Bulletin_Name",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Bulletin_Number",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Bulletin_Text",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Item_Altitude",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Item_Comment",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Item_Course",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Item_IsLive",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Item_Location",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Item_Name",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Item_Speed",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Item_SymbolCode",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Item_SymbolTable",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Object_Altitude",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Object_Comment",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Object_Course",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Object_IsLive",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Object_Location",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Object_Name",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Object_RawTimestamp",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Object_Speed",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Object_SymbolCode",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Object_SymbolTable",
            table: "Packets");
    }
}

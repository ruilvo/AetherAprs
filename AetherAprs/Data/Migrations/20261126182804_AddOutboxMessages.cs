// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherAprs.Data.Migrations;

/// <inheritdoc />
public partial class AddOutboxMessages : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "Message_LogicalId",
            table: "Packets",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "OutboxMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AddresseeBase = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                AddresseeSsid = table.Column<byte>(type: "INTEGER", nullable: false),
                Text = table.Column<string>(type: "TEXT", maxLength: 67, nullable: false),
                MessageNumber = table.Column<int>(type: "INTEGER", nullable: true),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                NumberReleaseAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                PortId = table.Column<Guid>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutboxMessages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Packets_Message_LogicalId",
            table: "Packets",
            column: "Message_LogicalId");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_AddresseeBase_AddresseeSsid_MessageNumber",
            table: "OutboxMessages",
            columns: new[] { "AddresseeBase", "AddresseeSsid", "MessageNumber" });

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_ExpiresAt",
            table: "OutboxMessages",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_Status",
            table: "OutboxMessages",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "OutboxMessages");

        migrationBuilder.DropIndex(
            name: "IX_Packets_Message_LogicalId",
            table: "Packets");

        migrationBuilder.DropColumn(
            name: "Message_LogicalId",
            table: "Packets");
    }
}

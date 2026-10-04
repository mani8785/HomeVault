using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VaultId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    DueAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reminders", x => x.Id);
                    table.CheckConstraint("CK_Reminder_DueAt", "typeof(DueAtUtcTicks) = 'integer' AND DueAtUtcTicks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("CK_Reminder_Id", "Id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Reminder_Status", "Status IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_Reminders_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reminders_Vaults_VaultId",
                        column: x => x.VaultId,
                        principalTable: "Vaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_AssetId",
                table: "Reminders",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_VaultId",
                table: "Reminders",
                column: "VaultId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reminders");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialVaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Vaults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vaults", x => x.Id);
                    table.CheckConstraint("CK_Vault_Id", "Id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Vault_Status", "Status IN (0, 1)");
                    table.CheckConstraint("CK_Vault_Type", "Type IN (0, 1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                columns: table => new
                {
                    VaultId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => new { x.VaultId, x.ActorId });
                    table.CheckConstraint("CK_Membership_Actor", "ActorId <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Membership_Role", "Role IN (0, 1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_Memberships_Vaults_VaultId",
                        column: x => x.VaultId,
                        principalTable: "Vaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Memberships");

            migrationBuilder.DropTable(
                name: "Vaults");
        }
    }
}

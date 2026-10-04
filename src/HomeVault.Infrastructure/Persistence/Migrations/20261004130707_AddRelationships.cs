using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Relationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VaultId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relationships", x => x.Id);
                    table.CheckConstraint("CK_Relationship_DistinctEndpoints", "SourceAssetId <> TargetAssetId");
                    table.CheckConstraint("CK_Relationship_Id", "Id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Relationship_Kind", "Kind = 0");
                    table.CheckConstraint("CK_Relationship_Status", "Status IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_Relationships_Assets_SourceAssetId",
                        column: x => x.SourceAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Relationships_Assets_TargetAssetId",
                        column: x => x.TargetAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Relationships_Vaults_VaultId",
                        column: x => x.VaultId,
                        principalTable: "Vaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_SourceAssetId",
                table: "Relationships",
                column: "SourceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_TargetAssetId",
                table: "Relationships",
                column: "TargetAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_VaultId_SourceAssetId_TargetAssetId_Kind",
                table: "Relationships",
                columns: new[] { "VaultId", "SourceAssetId", "TargetAssetId", "Kind" },
                unique: true,
                filter: "Status = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Relationships");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetEvidence",
                columns: table => new
                {
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetEvidence", x => new { x.AssetId, x.Id });
                    table.CheckConstraint("CK_Evidence_Id", "Id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Evidence_Kind", "Kind IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_AssetEvidence_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetEvidence");
        }
    }
}

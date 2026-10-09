using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSensitiveAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SensitiveAssetAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                    Sensitivity = table.Column<int>(type: "INTEGER", nullable: false),
                    Envelope = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SensitiveAssetAttributes", x => x.Id);
                    table.CheckConstraint("CK_SensitiveAttribute_Classification", "Sensitivity = 1");
                    table.CheckConstraint("CK_SensitiveAttribute_Identity", "Id <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_SensitiveAssetAttributes_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SensitiveAssetAttributes_AssetId_Name",
                table: "SensitiveAssetAttributes",
                columns: new[] { "AssetId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Restore a verified pre-upgrade backup to a new location; encrypted attributes cannot be silently discarded.");
        }
    }
}

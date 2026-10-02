using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountCredentials",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Expires = table.Column<long>(type: "INTEGER", nullable: false),
                    Consumed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountCredentials", x => x.Hash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountCredentials_Login_Purpose",
                table: "AccountCredentials",
                columns: new[] { "Login", "Purpose" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountCredentials");
        }
    }
}

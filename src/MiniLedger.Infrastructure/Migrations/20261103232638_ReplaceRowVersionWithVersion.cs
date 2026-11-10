using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceRowVersionWithVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Accounts");

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Accounts");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Accounts",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHolderNameToAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HolderName",
                table: "Accounts",
                type: "TEXT",
                maxLength: 150,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HolderName",
                table: "Accounts");
        }
    }
}

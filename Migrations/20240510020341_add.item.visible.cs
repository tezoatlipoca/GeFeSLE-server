using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeFeSLE.Migrations
{
    /// <inheritdoc />
    public partial class additemvisible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Visible",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Visible",
                table: "Items");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace GeFeSLE.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(GeFeSLEDb))]
    [Migration("20260701193000_AddModerationLinks")]
    public partial class AddModerationLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ModerationItemId",
                table: "Items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModeratedItemId",
                table: "Items",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModerationItemId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ModeratedItemId",
                table: "Items");
        }
    }
}
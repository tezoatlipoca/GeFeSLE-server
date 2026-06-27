using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeFeSLE.Migrations
{
    /// <inheritdoc />
    public partial class AddUserUploadsPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UploadsPath",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_UploadsPath",
                table: "AspNetUsers",
                column: "UploadsPath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_UploadsPath",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UploadsPath",
                table: "AspNetUsers");
        }
    }
}
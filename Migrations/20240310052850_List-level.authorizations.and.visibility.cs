using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeFeSLE.Migrations
{
    /// <inheritdoc />
    public partial class Listlevelauthorizationsandvisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatorId",
                table: "Lists",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "Lists",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "JwtToken",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateTable(
                name: "GeFeSLEUserGeList",
                columns: table => new
                {
                    GeListId = table.Column<int>(type: "INTEGER", nullable: false),
                    ListOwnersId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeFeSLEUserGeList", x => new { x.GeListId, x.ListOwnersId });
                    table.ForeignKey(
                        name: "FK_GeFeSLEUserGeList_AspNetUsers_ListOwnersId",
                        column: x => x.ListOwnersId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GeFeSLEUserGeList_Lists_GeListId",
                        column: x => x.GeListId,
                        principalTable: "Lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GeFeSLEUserGeList1",
                columns: table => new
                {
                    ContributorsId = table.Column<string>(type: "TEXT", nullable: false),
                    GeList1Id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeFeSLEUserGeList1", x => new { x.ContributorsId, x.GeList1Id });
                    table.ForeignKey(
                        name: "FK_GeFeSLEUserGeList1_AspNetUsers_ContributorsId",
                        column: x => x.ContributorsId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GeFeSLEUserGeList1_Lists_GeList1Id",
                        column: x => x.GeList1Id,
                        principalTable: "Lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lists_CreatorId",
                table: "Lists",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_GeFeSLEUserGeList_ListOwnersId",
                table: "GeFeSLEUserGeList",
                column: "ListOwnersId");

            migrationBuilder.CreateIndex(
                name: "IX_GeFeSLEUserGeList1_GeList1Id",
                table: "GeFeSLEUserGeList1",
                column: "GeList1Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Lists_AspNetUsers_CreatorId",
                table: "Lists",
                column: "CreatorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Lists_AspNetUsers_CreatorId",
                table: "Lists");

            migrationBuilder.DropTable(
                name: "GeFeSLEUserGeList");

            migrationBuilder.DropTable(
                name: "GeFeSLEUserGeList1");

            migrationBuilder.DropIndex(
                name: "IX_Lists_CreatorId",
                table: "Lists");

            migrationBuilder.DropColumn(
                name: "CreatorId",
                table: "Lists");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Lists");

            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "JwtToken",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}

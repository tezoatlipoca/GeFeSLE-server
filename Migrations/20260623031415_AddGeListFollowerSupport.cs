using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeFeSLE.Migrations
{
    /// <inheritdoc />
    public partial class AddGeListFollowerSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeAPActor",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    ContextId = table.Column<string>(type: "TEXT", nullable: true),
                    PreferredUsername = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    Inbox = table.Column<string>(type: "TEXT", nullable: true),
                    Outbox = table.Column<string>(type: "TEXT", nullable: true),
                    Followers = table.Column<string>(type: "TEXT", nullable: true),
                    Icon_Type = table.Column<string>(type: "TEXT", nullable: true),
                    Icon_Url = table.Column<string>(type: "TEXT", nullable: true),
                    Icon_MediaType = table.Column<string>(type: "TEXT", nullable: true),
                    Icon_Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Icon_Height = table.Column<int>(type: "INTEGER", nullable: true),
                    Image_Type = table.Column<string>(type: "TEXT", nullable: true),
                    Image_Url = table.Column<string>(type: "TEXT", nullable: true),
                    Image_MediaType = table.Column<string>(type: "TEXT", nullable: true),
                    Image_Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Image_Height = table.Column<int>(type: "INTEGER", nullable: true),
                    Url = table.Column<string>(type: "TEXT", nullable: true),
                    Discriminator = table.Column<string>(type: "TEXT", maxLength: 21, nullable: false),
                    FollowingLists = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeAPActor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeAPActor_GeAPActor_ContextId",
                        column: x => x.ContextId,
                        principalTable: "GeAPActor",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeAPActor_ContextId",
                table: "GeAPActor",
                column: "ContextId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeAPActor");
        }
    }
}

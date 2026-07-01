using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeFeSLE.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityPubObjectLikes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityPubObjectLikes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ListId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    CommentId = table.Column<int>(type: "INTEGER", nullable: true),
                    ObjectIri = table.Column<string>(type: "TEXT", nullable: false),
                    ActorIri = table.Column<string>(type: "TEXT", nullable: false),
                    LikeActivityIri = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityPubObjectLikes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPubObjectLikes_LikeActivityIri",
                table: "ActivityPubObjectLikes",
                column: "LikeActivityIri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPubObjectLikes_ListId_CommentId_IsActive",
                table: "ActivityPubObjectLikes",
                columns: new[] { "ListId", "CommentId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPubObjectLikes_ListId_ItemId_IsActive",
                table: "ActivityPubObjectLikes",
                columns: new[] { "ListId", "ItemId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPubObjectLikes_ListId_ObjectIri_ActorIri",
                table: "ActivityPubObjectLikes",
                columns: new[] { "ListId", "ObjectIri", "ActorIri" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityPubObjectLikes");
        }
    }
}

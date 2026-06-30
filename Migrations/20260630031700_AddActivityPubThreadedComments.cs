using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeFeSLE.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityPubThreadedComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ListId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentCommentId = table.Column<int>(type: "INTEGER", nullable: true),
                    RemoteObjectIri = table.Column<string>(type: "TEXT", nullable: false),
                    InReplyToIri = table.Column<string>(type: "TEXT", nullable: true),
                    ActorIri = table.Column<string>(type: "TEXT", nullable: true),
                    AttributedToIri = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    ContentHtml = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    RawNoteJson = table.Column<string>(type: "TEXT", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemComments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemComments_ListId_ItemId",
                table: "ItemComments",
                columns: new[] { "ListId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemComments_ListId_RemoteObjectIri",
                table: "ItemComments",
                columns: new[] { "ListId", "RemoteObjectIri" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemComments_ParentCommentId",
                table: "ItemComments",
                column: "ParentCommentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemComments");
        }
    }
}

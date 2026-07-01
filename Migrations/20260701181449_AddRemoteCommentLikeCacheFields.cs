using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeFeSLE.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteCommentLikeCacheFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RemoteLikesCount",
                table: "ItemComments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemoteLikesLastCheckedAt",
                table: "ItemComments",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoteLikesCount",
                table: "ItemComments");

            migrationBuilder.DropColumn(
                name: "RemoteLikesLastCheckedAt",
                table: "ItemComments");
        }
    }
}

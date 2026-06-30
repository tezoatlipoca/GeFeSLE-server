using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeFeSLE.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(GeFeSLEDb))]
    [Migration("20260629120000_AddActivityPubSuggestionSourceFields")]
    public partial class AddActivityPubSuggestionSourceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginatorActorIri",
                table: "Items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceAttributedToIri",
                table: "Items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceObjectIri",
                table: "Items",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginatorActorIri",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SourceAttributedToIri",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SourceObjectIri",
                table: "Items");
        }
    }
}
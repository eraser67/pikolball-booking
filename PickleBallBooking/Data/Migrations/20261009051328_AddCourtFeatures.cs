using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourtFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Features",
                table: "Courts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Features",
                table: "Courts");
        }
    }
}

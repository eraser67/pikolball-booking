using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase32b_PlayerProfile_AvatarPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarPath",
                table: "PlayerProfiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarPath",
                table: "PlayerProfiles");
        }
    }
}

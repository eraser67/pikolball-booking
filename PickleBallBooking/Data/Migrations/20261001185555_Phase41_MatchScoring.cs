using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase41_MatchScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<TimeSpan>(
                name: "EstimatedStartTime",
                table: "RoundRobinMatches",
                type: "interval",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "EstimatedEndTime",
                table: "RoundRobinMatches",
                type: "interval",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalizedAt",
                table: "RoundRobinMatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalizedByUserId",
                table: "RoundRobinMatches",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFinalized",
                table: "RoundRobinMatches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MatchScoreAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    RoundRobinMatchId = table.Column<int>(type: "integer", nullable: false),
                    PreviousTeam1Score = table.Column<int>(type: "integer", nullable: true),
                    PreviousTeam2Score = table.Column<int>(type: "integer", nullable: true),
                    PreviousWinningSide = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PreviousScoresJson = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NewTeam1Score = table.Column<int>(type: "integer", nullable: false),
                    NewTeam2Score = table.Column<int>(type: "integer", nullable: false),
                    NewWinningSide = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NewScoresJson = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ChangedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ChangedByUserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchScoreAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchScoreAudits_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchScoreAudits_RoundRobinMatches_RoundRobinMatchId",
                        column: x => x.RoundRobinMatchId,
                        principalTable: "RoundRobinMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchScoreAudit_Org_Match",
                table: "MatchScoreAudits",
                columns: new[] { "OrganizationId", "RoundRobinMatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchScoreAudits_RoundRobinMatchId",
                table: "MatchScoreAudits",
                column: "RoundRobinMatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchScoreAudits");

            migrationBuilder.DropColumn(
                name: "FinalizedAt",
                table: "RoundRobinMatches");

            migrationBuilder.DropColumn(
                name: "FinalizedByUserId",
                table: "RoundRobinMatches");

            migrationBuilder.DropColumn(
                name: "IsFinalized",
                table: "RoundRobinMatches");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "EstimatedStartTime",
                table: "RoundRobinMatches",
                type: "time without time zone",
                nullable: true,
                oldClrType: typeof(TimeSpan),
                oldType: "interval",
                oldNullable: true);

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "EstimatedEndTime",
                table: "RoundRobinMatches",
                type: "time without time zone",
                nullable: true,
                oldClrType: typeof(TimeSpan),
                oldType: "interval",
                oldNullable: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase40_RoundRobin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoundRobinEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    ActivityId = table.Column<int>(type: "integer", nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    NumberOfRounds = table.Column<int>(type: "integer", nullable: false),
                    MatchDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    BreakDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    ScoringType = table.Column<int>(type: "integer", nullable: false),
                    PointsToWin = table.Column<int>(type: "integer", nullable: false),
                    WinByTwo = table.Column<bool>(type: "boolean", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundRobinEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoundRobinEvents_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoundRobinEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoundRobinByes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    RoundRobinEventId = table.Column<int>(type: "integer", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    ActivityRsvpId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    PlayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundRobinByes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoundRobinByes_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoundRobinByes_RoundRobinEvents_RoundRobinEventId",
                        column: x => x.RoundRobinEventId,
                        principalTable: "RoundRobinEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoundRobinMatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    RoundRobinEventId = table.Column<int>(type: "integer", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    CourtId = table.Column<int>(type: "integer", nullable: true),
                    CourtName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EstimatedStartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    EstimatedEndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Team1Player1RsvpId = table.Column<int>(type: "integer", nullable: true),
                    Team1Player1UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Team1Player1Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Team1Player2RsvpId = table.Column<int>(type: "integer", nullable: true),
                    Team1Player2UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Team1Player2Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Team2Player1RsvpId = table.Column<int>(type: "integer", nullable: true),
                    Team2Player1UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Team2Player1Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Team2Player2RsvpId = table.Column<int>(type: "integer", nullable: true),
                    Team2Player2UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Team2Player2Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Team1Score = table.Column<int>(type: "integer", nullable: true),
                    Team2Score = table.Column<int>(type: "integer", nullable: true),
                    ScoresJson = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WinningSide = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundRobinMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoundRobinMatches_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoundRobinMatches_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoundRobinMatches_RoundRobinEvents_RoundRobinEventId",
                        column: x => x.RoundRobinEventId,
                        principalTable: "RoundRobinEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoundRobinBye_Org_Event_Round",
                table: "RoundRobinByes",
                columns: new[] { "OrganizationId", "RoundRobinEventId", "RoundNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_RoundRobinByes_RoundRobinEventId",
                table: "RoundRobinByes",
                column: "RoundRobinEventId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundRobinEvent_Org_Activity",
                table: "RoundRobinEvents",
                columns: new[] { "OrganizationId", "ActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoundRobinEvents_ActivityId",
                table: "RoundRobinEvents",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundRobinMatch_Org_Court",
                table: "RoundRobinMatches",
                columns: new[] { "OrganizationId", "CourtId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoundRobinMatch_Org_Event_Round",
                table: "RoundRobinMatches",
                columns: new[] { "OrganizationId", "RoundRobinEventId", "RoundNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_RoundRobinMatches_CourtId",
                table: "RoundRobinMatches",
                column: "CourtId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundRobinMatches_RoundRobinEventId",
                table: "RoundRobinMatches",
                column: "RoundRobinEventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoundRobinByes");

            migrationBuilder.DropTable(
                name: "RoundRobinMatches");

            migrationBuilder.DropTable(
                name: "RoundRobinEvents");
        }
    }
}

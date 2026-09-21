using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PickleBallBooking.Data.Migrations
{
    /// <summary>
    /// Phase 20.4 - Multi-tenant database foundation.
    ///
    /// This migration is strictly ADDITIVE. It introduces the Organization tenant
    /// concept and attaches existing Pikolball data to the first organization. It
    /// MUST NOT delete or truncate any existing row.
    ///
    /// Safety notes:
    ///  - Unlike the historical "Update24HourTimeSlots" migration, this migration never
    ///    issues DELETE/TRUNCATE and never recreates existing tables destructively.
    ///  - OrganizationId columns are added as NULL, backfilled, and only then made NOT NULL.
    ///  - The Pikolball organization is resolved by its unique Slug, never by a guessed Id.
    ///  - The PostgreSQL booking exclusion constraint (EX_Bookings_NoOverlap) is rebuilt
    ///    tenant-aware while preserving the BookingPeriod column and btree_gist extension.
    /// </summary>
    public partial class AddMultiTenantFoundation : Migration
    {
        private const string PikolballSlug = "pikolball";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // -------------------------------------------------------------------
            // 1. Ensure btree_gist is present. Idempotent; preserves the extension.
            // -------------------------------------------------------------------
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // -------------------------------------------------------------------
            // 2. Create Organizations.
            // -------------------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Slug",
                table: "Organizations",
                column: "Slug",
                unique: true);

            // -------------------------------------------------------------------
            // 3. Insert the first organization (Pikolball) deterministically.
            //    Status = 0 (OrganizationStatus.Active).
            //    Resolved by unique Slug on conflict, so re-running is safe.
            // -------------------------------------------------------------------
            migrationBuilder.Sql($@"
INSERT INTO ""Organizations"" (""Name"", ""Slug"", ""Status"", ""CreatedAt"", ""UpdatedAt"")
VALUES ('Pikolball', '{PikolballSlug}', 0, NOW(), NOW())
ON CONFLICT (""Slug"") DO NOTHING;");

            // -------------------------------------------------------------------
            // 4. Create OrganizationMembers + indexes.
            //    UserId is text to match AspNetUsers.Id (text). FKs are Restrict so
            //    neither organization nor Identity user deletion cascades into
            //    membership rows.
            // -------------------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "OrganizationMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMember_OrganizationId_Role",
                table: "OrganizationMembers",
                columns: new[] { "OrganizationId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMember_OrganizationId_UserId",
                table: "OrganizationMembers",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMember_UserId",
                table: "OrganizationMembers",
                column: "UserId");

            // -------------------------------------------------------------------
            // 5. Drop the pre-existing (non-tenant) unique indexes. They are
            //    replaced below with tenant-scoped versions.
            // -------------------------------------------------------------------
            migrationBuilder.DropIndex(
                name: "IX_CourtTimeSlot_CourtId_TimeSlotId",
                table: "CourtTimeSlots");

            migrationBuilder.DropIndex(
                name: "IX_BookingTimeSlot_CourtId_BookingDate_TimeSlotId_Active",
                table: "BookingTimeSlots");

            // -------------------------------------------------------------------
            // 6. Add OrganizationId columns as NULLABLE. They are backfilled next
            //    and only afterwards made NOT NULL.
            // -------------------------------------------------------------------
            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Courts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Pricings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "CourtTimeSlots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "BookingTimeSlots",
                type: "integer",
                nullable: true);

            // -------------------------------------------------------------------
            // 7. Backfill every existing row to the Pikolball organization.
            //    The organization is resolved by its unique Slug. A defensive guard
            //    raises an exception if the organization is missing or if any NULL
            //    remains afterwards, so the migration fails loudly instead of
            //    silently writing NULLs.
            // -------------------------------------------------------------------
            migrationBuilder.Sql($@"
DO $$
DECLARE
    v_org_id integer;
BEGIN
    SELECT ""Id"" INTO v_org_id
    FROM ""Organizations""
    WHERE ""Slug"" = '{PikolballSlug}'
    LIMIT 1;

    IF v_org_id IS NULL THEN
        RAISE EXCEPTION 'Multi-tenant backfill aborted: organization with Slug = ''{PikolballSlug}'' was not found.';
    END IF;

    UPDATE ""Courts""          SET ""OrganizationId"" = v_org_id WHERE ""OrganizationId"" IS NULL;
    UPDATE ""Bookings""        SET ""OrganizationId"" = v_org_id WHERE ""OrganizationId"" IS NULL;
    UPDATE ""Pricings""        SET ""OrganizationId"" = v_org_id WHERE ""OrganizationId"" IS NULL;
    UPDATE ""CourtTimeSlots""  SET ""OrganizationId"" = v_org_id WHERE ""OrganizationId"" IS NULL;
    UPDATE ""BookingTimeSlots"" SET ""OrganizationId"" = v_org_id WHERE ""OrganizationId"" IS NULL;

    IF EXISTS (SELECT 1 FROM ""Courts""          WHERE ""OrganizationId"" IS NULL)
       OR EXISTS (SELECT 1 FROM ""Bookings""        WHERE ""OrganizationId"" IS NULL)
       OR EXISTS (SELECT 1 FROM ""Pricings""        WHERE ""OrganizationId"" IS NULL)
       OR EXISTS (SELECT 1 FROM ""CourtTimeSlots""  WHERE ""OrganizationId"" IS NULL)
       OR EXISTS (SELECT 1 FROM ""BookingTimeSlots"" WHERE ""OrganizationId"" IS NULL) THEN
        RAISE EXCEPTION 'Multi-tenant backfill aborted: NULL OrganizationId values remain after backfill.';
    END IF;
END $$;");

            // -------------------------------------------------------------------
            // 8. Make OrganizationId NOT NULL now that every row is backfilled.
            // -------------------------------------------------------------------
            migrationBuilder.AlterColumn<int>(
                name: "OrganizationId",
                table: "Courts",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "OrganizationId",
                table: "Bookings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "OrganizationId",
                table: "Pricings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "OrganizationId",
                table: "CourtTimeSlots",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "OrganizationId",
                table: "BookingTimeSlots",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            // -------------------------------------------------------------------
            // 9. Supporting indexes for the new Organization FKs.
            // -------------------------------------------------------------------
            migrationBuilder.CreateIndex(
                name: "IX_Courts_OrganizationId",
                table: "Courts",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_OrganizationId",
                table: "Bookings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Pricings_OrganizationId",
                table: "Pricings",
                column: "OrganizationId");

            // EF also creates a CourtId index for CourtTimeSlots because the previous
            // unique index no longer leads with CourtId. Kept as a supporting index.
            migrationBuilder.CreateIndex(
                name: "IX_CourtTimeSlots_CourtId",
                table: "CourtTimeSlots",
                column: "CourtId");

            // -------------------------------------------------------------------
            // 10. Tenant-scoped FKs to Organizations (Restrict - no cascade).
            // -------------------------------------------------------------------
            migrationBuilder.AddForeignKey(
                name: "FK_Courts_Organizations_OrganizationId",
                table: "Courts",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Organizations_OrganizationId",
                table: "Bookings",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pricings_Organizations_OrganizationId",
                table: "Pricings",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CourtTimeSlots_Organizations_OrganizationId",
                table: "CourtTimeSlots",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BookingTimeSlots_Organizations_OrganizationId",
                table: "BookingTimeSlots",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // -------------------------------------------------------------------
            // 11. Rebuild CRITICAL CONSTRAINT 1 - BookingTimeSlot filtered unique
            //     index, now tenant-aware. Old index was dropped in step 5.
            // -------------------------------------------------------------------
            migrationBuilder.CreateIndex(
                name: "IX_BookingTimeSlot_OrganizationId_CourtId_BookingDate_TimeSlotId_Active",
                table: "BookingTimeSlots",
                columns: new[] { "OrganizationId", "CourtId", "BookingDate", "TimeSlotId" },
                unique: true,
                filter: "\"IsActive\" = true");

            // -------------------------------------------------------------------
            // 12. Rebuild CRITICAL CONSTRAINT 2 - CourtTimeSlot unique index,
            //     now tenant-aware. Old index was dropped in step 5.
            // -------------------------------------------------------------------
            migrationBuilder.CreateIndex(
                name: "IX_CourtTimeSlot_OrganizationId_CourtId_TimeSlotId",
                table: "CourtTimeSlots",
                columns: new[] { "OrganizationId", "CourtId", "TimeSlotId" },
                unique: true);

            // -------------------------------------------------------------------
            // 13. CRITICAL CONSTRAINT 3 - Rebuild the PostgreSQL booking exclusion
            //     constraint tenant-aware.
            //
            //     EF Core cannot express a GiST exclusion constraint, so this is raw
            //     SQL. The BookingPeriod computed column (with the end-of-day /
            //     overnight fix) and the btree_gist extension are preserved; only the
            //     constraint definition gains the OrganizationId column.
            //
            //     The constraint is dropped and recreated (Postgres cannot ALTER an
            //     exclusion constraint in place). This is a metadata change only - no
            //     booking rows are modified.
            // -------------------------------------------------------------------
            migrationBuilder.Sql("ALTER TABLE \"Bookings\" DROP CONSTRAINT IF EXISTS \"EX_Bookings_NoOverlap\";");

            migrationBuilder.Sql(@"
ALTER TABLE ""Bookings""
ADD CONSTRAINT ""EX_Bookings_NoOverlap""
EXCLUDE USING GIST (
    ""OrganizationId"" WITH =,
    ""CourtId"" WITH =,
    ""BookingPeriod"" WITH &&
)
WHERE (""BookingStatus"" <> 2);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // -------------------------------------------------------------------
            // Reverse the exclusion constraint back to its pre-tenant definition.
            // -------------------------------------------------------------------
            migrationBuilder.Sql("ALTER TABLE \"Bookings\" DROP CONSTRAINT IF EXISTS \"EX_Bookings_NoOverlap\";");

            migrationBuilder.Sql(@"
ALTER TABLE ""Bookings""
ADD CONSTRAINT ""EX_Bookings_NoOverlap""
EXCLUDE USING GIST (
    ""CourtId"" WITH =,
    ""BookingPeriod"" WITH &&
)
WHERE (""BookingStatus"" <> 2);");

            // Drop tenant-scoped indexes.
            migrationBuilder.DropIndex(
                name: "IX_BookingTimeSlot_OrganizationId_CourtId_BookingDate_TimeSlotId_Active",
                table: "BookingTimeSlots");

            migrationBuilder.DropIndex(
                name: "IX_CourtTimeSlot_OrganizationId_CourtId_TimeSlotId",
                table: "CourtTimeSlots");

            // Drop FKs to Organizations.
            migrationBuilder.DropForeignKey(
                name: "FK_BookingTimeSlots_Organizations_OrganizationId",
                table: "BookingTimeSlots");

            migrationBuilder.DropForeignKey(
                name: "FK_CourtTimeSlots_Organizations_OrganizationId",
                table: "CourtTimeSlots");

            migrationBuilder.DropForeignKey(
                name: "FK_Pricings_Organizations_OrganizationId",
                table: "Pricings");

            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Organizations_OrganizationId",
                table: "Bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_Courts_Organizations_OrganizationId",
                table: "Courts");

            // Drop supporting indexes created in Up.
            migrationBuilder.DropIndex(
                name: "IX_CourtTimeSlots_CourtId",
                table: "CourtTimeSlots");

            migrationBuilder.DropIndex(
                name: "IX_Pricings_OrganizationId",
                table: "Pricings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_OrganizationId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Courts_OrganizationId",
                table: "Courts");

            // Drop the OrganizationId columns.
            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "BookingTimeSlots");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "CourtTimeSlots");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Pricings");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Courts");

            // Recreate the original (non-tenant) unique indexes.
            migrationBuilder.CreateIndex(
                name: "IX_BookingTimeSlot_CourtId_BookingDate_TimeSlotId_Active",
                table: "BookingTimeSlots",
                columns: new[] { "CourtId", "BookingDate", "TimeSlotId", "IsActive" },
                unique: true,
                filter: "\"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_CourtTimeSlot_CourtId_TimeSlotId",
                table: "CourtTimeSlots",
                columns: new[] { "CourtId", "TimeSlotId" },
                unique: true);

            // Drop OrganizationMembers then Organizations.
            migrationBuilder.DropTable(
                name: "OrganizationMembers");

            migrationBuilder.DropTable(
                name: "Organizations");

            // NOTE: btree_gist extension is intentionally NOT dropped - other
            // migrations/constraints may rely on it and dropping it would be unsafe.
        }
    }
}
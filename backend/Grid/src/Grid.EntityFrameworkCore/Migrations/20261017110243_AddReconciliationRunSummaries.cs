using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grid.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationRunSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppReconciliationRunSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TotalRecords = table.Column<int>(type: "integer", nullable: false),
                    FullMatchRecords = table.Column<int>(type: "integer", nullable: false),
                    PartialMatchRecords = table.Column<int>(type: "integer", nullable: false),
                    ExceptionRecords = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppReconciliationRunSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppReconciliationRunSummaries_AppReconciliationRuns_Id",
                        column: x => x.Id,
                        principalTable: "AppReconciliationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppReconciliationRunSummaries_CreatedAt_ProfileId",
                table: "AppReconciliationRunSummaries",
                columns: new[] { "CreatedAt", "ProfileId" });

            // Reconstruct historical record-weighted partial outcomes once. Dashboard
            // queries subsequently read only the compact, indexed summary rows.
            migrationBuilder.Sql("""
                INSERT INTO "AppReconciliationRunSummaries"
                    ("Id", "ProfileId", "CreatedAt", "TotalRecords", "FullMatchRecords", "PartialMatchRecords", "ExceptionRecords")
                SELECT r."Id", r."ProfileId", r."CreationTime", r."TotalRecords",
                       r."MatchedRecords" - COALESCE(p.records, 0)::integer,
                       COALESCE(p.records, 0)::integer, r."UnresolvedRecords"
                FROM "AppReconciliationRuns" r
                LEFT JOIN (
                    SELECT g."RunId", SUM(jsonb_array_length(record.value)) AS records
                    FROM "AppMatchGroups" g
                    CROSS JOIN LATERAL jsonb_each(g."RecordsJson"::jsonb) AS record
                    WHERE g."EvidenceJson"::jsonb ->> 'comparison' = 'partial_match'
                    GROUP BY g."RunId"
                ) p ON p."RunId" = r."Id"
                WHERE r."Status" = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppReconciliationRunSummaries");
        }
    }
}

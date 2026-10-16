using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grid.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppDatasetRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    ValuesJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDatasetRows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppDatasets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ColumnsJson = table.Column<string>(type: "text", nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDatasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppMatchGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    RuleId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    KeyJson = table.Column<string>(type: "text", nullable: false),
                    RecordsJson = table.Column<string>(type: "text", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMatchGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DefinitionJson = table.Column<string>(type: "text", nullable: false),
                    ApprovedDefinitionJson = table.Column<string>(type: "text", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppReconciliationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalRecords = table.Column<int>(type: "integer", nullable: false),
                    MatchedRecords = table.Column<int>(type: "integer", nullable: false),
                    UnresolvedRecords = table.Column<int>(type: "integer", nullable: false),
                    AmbiguousRecords = table.Column<int>(type: "integer", nullable: false),
                    MatchedGroups = table.Column<int>(type: "integer", nullable: false),
                    DefinitionSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    DatasetSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppReconciliationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppRegressionBaselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetsJson = table.Column<string>(type: "text", nullable: false),
                    ProfileJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRegressionBaselines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppRunRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CsvRow = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecordJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRunRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppDatasetRows_DatasetId_RowNumber",
                table: "AppDatasetRows",
                columns: new[] { "DatasetId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppDatasets_Code",
                table: "AppDatasets",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppMatchGroups_RunId",
                table: "AppMatchGroups",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_AppProfiles_PackageId",
                table: "AppProfiles",
                column: "PackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppReconciliationRuns_ProfileId",
                table: "AppReconciliationRuns",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AppRegressionBaselines_PackageId",
                table: "AppRegressionBaselines",
                column: "PackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppRunRecords_RunId",
                table: "AppRunRecords",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppDatasetRows");

            migrationBuilder.DropTable(
                name: "AppDatasets");

            migrationBuilder.DropTable(
                name: "AppMatchGroups");

            migrationBuilder.DropTable(
                name: "AppPackages");

            migrationBuilder.DropTable(
                name: "AppProfiles");

            migrationBuilder.DropTable(
                name: "AppReconciliationRuns");

            migrationBuilder.DropTable(
                name: "AppRegressionBaselines");

            migrationBuilder.DropTable(
                name: "AppRunRecords");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grid.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalDatasets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppCanonicalDatasets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    GcsPrefix = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SchemaColumnsJson = table.Column<string>(type: "jsonb", nullable: false),
                    PartitionColumnsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCanonicalDatasets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCanonicalDatasets_Code",
                table: "AppCanonicalDatasets",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCanonicalDatasets_GcsPrefix",
                table: "AppCanonicalDatasets",
                column: "GcsPrefix",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCanonicalDatasets");
        }
    }
}

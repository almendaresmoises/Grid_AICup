using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grid.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationOptimization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveConfigurationCommitId",
                table: "AppProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppAnalysisWorkAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalysisId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LeaseExpiry = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Heartbeat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CheckpointJson = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAnalysisWorkAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppCanonicalDatasetRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalDatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    ProvenanceAnalysisId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProvenanceCommitId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCanonicalDatasetRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppConfigurationCommits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    ApprovedProfileSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    SelectedOperationIdsJson = table.Column<string>(type: "text", nullable: false),
                    RejectedOperationIdsJson = table.Column<string>(type: "text", nullable: false),
                    ParentCommitId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaseHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RevisionBindingsJson = table.Column<string>(type: "text", nullable: false),
                    SourceManifestJson = table.Column<string>(type: "text", nullable: false),
                    ValidationReportHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConfigurationCommits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppConfigurationDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SentTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConfigurationDispatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppConfigurationExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessedInputManifestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultingRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    CompletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConfigurationExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppConfigurationRequestReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestBodyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetAnalysisId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConfigurationRequestReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppParserConfigurationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParserConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    ProvenanceAnalysisId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProvenanceCommitId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppParserConfigurationRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppProfileAnalysisSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveAnalysisId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastAnalysisId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppProfileAnalysisSlots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppReconciliationAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Context = table.Column<string>(type: "text", nullable: true),
                    BaseHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ManifestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    PromptModelProvenanceJson = table.Column<string>(type: "text", nullable: true),
                    SelectionJson = table.Column<string>(type: "text", nullable: false),
                    SelectionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DecisionRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfigurationCommitId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppReconciliationAnalyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppReconciliationAnalysisChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependencyGroupId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetObjectVersion = table.Column<int>(type: "integer", nullable: true),
                    OperationType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OperationJson = table.Column<string>(type: "text", nullable: false),
                    BeforeJson = table.Column<string>(type: "text", nullable: false),
                    AfterJson = table.Column<string>(type: "text", nullable: false),
                    Rationale = table.Column<string>(type: "text", nullable: false),
                    EvidenceIdsJson = table.Column<string>(type: "text", nullable: false),
                    SafetyClass = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsSelectable = table.Column<bool>(type: "boolean", nullable: false),
                    BlockerReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppReconciliationAnalysisChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppReconciliationAnalysisValidations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BaseHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InputManifestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EngineVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Coverage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FullMatchCount = table.Column<int>(type: "integer", nullable: false),
                    PartialMatchCount = table.Column<int>(type: "integer", nullable: false),
                    UnresolvedCount = table.Column<int>(type: "integer", nullable: false),
                    AmbiguousCount = table.Column<int>(type: "integer", nullable: false),
                    BaseFullMatchCount = table.Column<int>(type: "integer", nullable: false),
                    BasePartialMatchCount = table.Column<int>(type: "integer", nullable: false),
                    BaseUnresolvedCount = table.Column<int>(type: "integer", nullable: false),
                    BaseAmbiguousCount = table.Column<int>(type: "integer", nullable: false),
                    TotalRecords = table.Column<int>(type: "integer", nullable: false),
                    PreservedMatches = table.Column<bool>(type: "boolean", nullable: false),
                    RegressionVerdictsJson = table.Column<string>(type: "text", nullable: false),
                    DetailedReportJson = table.Column<string>(type: "text", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppReconciliationAnalysisValidations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAnalysisWorkAttempts_LeaseExpiry",
                table: "AppAnalysisWorkAttempts",
                column: "LeaseExpiry");

            migrationBuilder.CreateIndex(
                name: "IX_AppCanonicalDatasetRevisions_CanonicalDatasetId_ContentHash",
                table: "AppCanonicalDatasetRevisions",
                columns: new[] { "CanonicalDatasetId", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppConfigurationCommits_ContentHash",
                table: "AppConfigurationCommits",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_AppConfigurationCommits_ProfileId_CreationTime",
                table: "AppConfigurationCommits",
                columns: new[] { "ProfileId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppConfigurationDispatches_DeduplicationKey",
                table: "AppConfigurationDispatches",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppConfigurationDispatches_NextAttemptTime_Status",
                table: "AppConfigurationDispatches",
                columns: new[] { "NextAttemptTime", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppConfigurationExecutions_CommitId_AssessedInputManifestHa~",
                table: "AppConfigurationExecutions",
                columns: new[] { "CommitId", "AssessedInputManifestHash" });

            migrationBuilder.CreateIndex(
                name: "IX_AppConfigurationExecutions_ExecutionRequestId",
                table: "AppConfigurationExecutions",
                column: "ExecutionRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AppConfigurationRequestReceipts_ActorId_OperationKind_Reque~",
                table: "AppConfigurationRequestReceipts",
                columns: new[] { "ActorId", "OperationKind", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppParserConfigurationRevisions_ParserConfigurationId_Conte~",
                table: "AppParserConfigurationRevisions",
                columns: new[] { "ParserConfigurationId", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppReconciliationAnalyses_ProfileId_CreationTime",
                table: "AppReconciliationAnalyses",
                columns: new[] { "ProfileId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppReconciliationAnalysisChanges_AnalysisId_DependencyGroup~",
                table: "AppReconciliationAnalysisChanges",
                columns: new[] { "AnalysisId", "DependencyGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppReconciliationAnalysisValidations_AnalysisId_SelectionHa~",
                table: "AppReconciliationAnalysisValidations",
                columns: new[] { "AnalysisId", "SelectionHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAnalysisWorkAttempts");

            migrationBuilder.DropTable(
                name: "AppCanonicalDatasetRevisions");

            migrationBuilder.DropTable(
                name: "AppConfigurationCommits");

            migrationBuilder.DropTable(
                name: "AppConfigurationDispatches");

            migrationBuilder.DropTable(
                name: "AppConfigurationExecutions");

            migrationBuilder.DropTable(
                name: "AppConfigurationRequestReceipts");

            migrationBuilder.DropTable(
                name: "AppParserConfigurationRevisions");

            migrationBuilder.DropTable(
                name: "AppProfileAnalysisSlots");

            migrationBuilder.DropTable(
                name: "AppReconciliationAnalyses");

            migrationBuilder.DropTable(
                name: "AppReconciliationAnalysisChanges");

            migrationBuilder.DropTable(
                name: "AppReconciliationAnalysisValidations");

            migrationBuilder.DropColumn(
                name: "ActiveConfigurationCommitId",
                table: "AppProfiles");
        }
    }
}

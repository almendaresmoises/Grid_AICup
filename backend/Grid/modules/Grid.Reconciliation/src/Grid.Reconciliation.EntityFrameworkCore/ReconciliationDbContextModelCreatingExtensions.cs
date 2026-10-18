using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Grid.Reconciliation;

namespace Grid.Reconciliation.EntityFrameworkCore;

public static class ReconciliationDbContextModelCreatingExtensions
{
    public static void ConfigureReconciliation(this ModelBuilder builder)
    {
        builder.Entity<AiConfiguration>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "AiConfigurations", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Status).IsRequired().HasMaxLength(32);
            b.Property(x => x.ManifestJson).IsRequired();
            b.Property(x => x.ResultJson).IsRequired();
            b.HasIndex(x => new { x.CreatorId, x.CreationTime });
        });
        builder.Entity<Dataset>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "Datasets", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(40);
            b.Property(x => x.Name).IsRequired().HasMaxLength(128);
            b.Property(x => x.ColumnsJson).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<DatasetRow>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "DatasetRows", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ValuesJson).IsRequired();
            b.HasIndex(x => new { x.DatasetId, x.RowNumber }).IsUnique();
        });

        builder.Entity<Package>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "Packages", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(128);
            b.Property(x => x.Description).HasMaxLength(2000);
        });

        builder.Entity<Profile>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "Profiles", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.DefinitionJson).IsRequired();
            b.HasIndex(x => x.PackageId).IsUnique();
        });

        builder.Entity<RegressionBaseline>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "RegressionBaselines", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.DatasetsJson).IsRequired();
            b.Property(x => x.ProfileJson).IsRequired();
            b.HasIndex(x => x.PackageId).IsUnique();
        });

        builder.Entity<ReconciliationRun>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "ReconciliationRuns", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.DefinitionSnapshotJson).IsRequired();
            b.Property(x => x.DatasetSnapshotJson).IsRequired();
            b.HasIndex(x => x.ProfileId);
        });

        builder.Entity<ReconciliationRunSummary>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "ReconciliationRunSummaries", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.CreatedAt).HasColumnType("timestamp without time zone");
            b.HasIndex(x => new { x.CreatedAt, x.ProfileId });
            b.HasOne<ReconciliationRun>().WithOne().HasForeignKey<ReconciliationRunSummary>(x => x.Id).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MatchGroup>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "MatchGroups", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.RuleId).IsRequired().HasMaxLength(40);
            b.Property(x => x.KeyJson).IsRequired();
            b.Property(x => x.RecordsJson).IsRequired();
            b.Property(x => x.EvidenceJson).IsRequired();
            b.HasIndex(x => x.RunId);
        });

        builder.Entity<RunRecord>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "RunRecords", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.DatasetCode).IsRequired().HasMaxLength(40);
            b.Property(x => x.Reason).IsRequired().HasMaxLength(32);
            b.Property(x => x.RecordJson).IsRequired();
            b.HasIndex(x => x.RunId);
        });

        builder.Entity<ReconciliationAnalysis>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "ReconciliationAnalyses", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Status).IsRequired().HasMaxLength(32);
            b.Property(x => x.Stage).IsRequired().HasMaxLength(64);
            b.Property(x => x.BaseHash).HasMaxLength(64);
            b.Property(x => x.ManifestHash).HasMaxLength(64);
            b.Property(x => x.SelectionHash).HasMaxLength(64);
            b.Property(x => x.FailureCode).HasMaxLength(64);
            b.Property(x => x.FailureMessage).HasMaxLength(2000);
            b.HasIndex(x => new { x.ProfileId, x.CreationTime });
        });

        builder.Entity<ProfileAnalysisSlot>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "ProfileAnalysisSlots", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(x => x.Id);
            b.Property(x => x.ConcurrencyToken).IsRequired().HasMaxLength(40);
        });

        builder.Entity<ReconciliationAnalysisChange>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "ReconciliationAnalysisChanges", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.DependencyGroupId).IsRequired().HasMaxLength(64);
            b.Property(x => x.ChangeType).IsRequired().HasMaxLength(32);
            b.Property(x => x.TargetIdentifier).IsRequired().HasMaxLength(200);
            b.Property(x => x.BaseHash).HasMaxLength(64);
            b.Property(x => x.OperationType).IsRequired().HasMaxLength(64);
            b.Property(x => x.SafetyClass).IsRequired().HasMaxLength(32);
            b.Property(x => x.BlockerReason).HasMaxLength(2000);
            b.HasIndex(x => new { x.AnalysisId, x.DependencyGroupId });
        });

        builder.Entity<ReconciliationAnalysisValidation>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "ReconciliationAnalysisValidations", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.SelectionHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.BaseHash).HasMaxLength(64);
            b.Property(x => x.InputManifestHash).HasMaxLength(64);
            b.Property(x => x.EngineVersion).IsRequired().HasMaxLength(32);
            b.Property(x => x.State).IsRequired().HasMaxLength(32);
            b.Property(x => x.Coverage).IsRequired().HasMaxLength(32);
            b.HasIndex(x => new { x.AnalysisId, x.SelectionHash });
        });

        builder.Entity<ConfigurationCommit>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "ConfigurationCommits", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.BaseHash).HasMaxLength(64);
            b.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.ValidationReportHash).HasMaxLength(64);
            b.HasIndex(x => new { x.ProfileId, x.CreationTime });
            b.HasIndex(x => x.ContentHash);
        });

        builder.Entity<ConfigurationExecution>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "ConfigurationExecutions", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.AssessedInputManifestHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.Status).IsRequired().HasMaxLength(32);
            b.Property(x => x.Stage).IsRequired().HasMaxLength(64);
            b.Property(x => x.ErrorMessage).HasMaxLength(2000);
            b.HasIndex(x => new { x.CommitId, x.AssessedInputManifestHash });
            b.HasIndex(x => x.ExecutionRequestId);
        });

        builder.Entity<AnalysisWorkAttempt>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "AnalysisWorkAttempts", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.WorkType).IsRequired().HasMaxLength(32);
            b.Property(x => x.Stage).IsRequired().HasMaxLength(64);
            b.Property(x => x.LeaseOwner).IsRequired().HasMaxLength(100);
            b.Property(x => x.ErrorMessage).HasMaxLength(2000);
            b.HasIndex(x => x.LeaseExpiry);
        });

        builder.Entity<ConfigurationDispatch>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "ConfigurationDispatches", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Kind).IsRequired().HasMaxLength(32);
            b.Property(x => x.DeduplicationKey).IsRequired().HasMaxLength(128);
            b.Property(x => x.Status).IsRequired().HasMaxLength(32);
            b.HasIndex(x => x.DeduplicationKey).IsUnique();
            b.HasIndex(x => new { x.NextAttemptTime, x.Status });
        });

        builder.Entity<ConfigurationRequestReceipt>(b =>
        {
            b.ToTable(ReconciliationConsts.DbTablePrefix + "ConfigurationRequestReceipts", ReconciliationConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.OperationKind).IsRequired().HasMaxLength(32);
            b.Property(x => x.RequestBodyHash).IsRequired().HasMaxLength(64);
            b.HasIndex(x => new { x.ActorId, x.OperationKind, x.RequestId }).IsUnique();
        });
    }
}

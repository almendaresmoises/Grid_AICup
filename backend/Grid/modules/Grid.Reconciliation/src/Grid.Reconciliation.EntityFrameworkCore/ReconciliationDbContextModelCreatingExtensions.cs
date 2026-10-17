using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Grid.Reconciliation;

namespace Grid.Reconciliation.EntityFrameworkCore;

public static class ReconciliationDbContextModelCreatingExtensions
{
    public static void ConfigureReconciliation(this ModelBuilder builder)
    {
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
    }
}

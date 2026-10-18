using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Grid.Datasets.EntityFrameworkCore;

public static class DatasetsDbContextModelCreatingExtensions
{
    public static void ConfigureDatasets(this ModelBuilder builder)
    {
        builder.Entity<CanonicalDataset>(b =>
        {
            b.ToTable(DatasetConsts.DbTablePrefix + "CanonicalDatasets", DatasetConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(50);
            b.Property(x => x.Name).IsRequired().HasMaxLength(100);
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.GcsPrefix).IsRequired().HasMaxLength(200);
            b.Property(x => x.SchemaColumnsJson).IsRequired().HasColumnType("jsonb");
            b.Property(x => x.PartitionColumnsJson).IsRequired().HasColumnType("jsonb");
            b.HasIndex(x => x.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(x => x.GcsPrefix).IsUnique().HasFilter("\"IsDeleted\" = false");
        });

        builder.Entity<CanonicalDatasetRevision>(b =>
        {
            b.ToTable(DatasetConsts.DbTablePrefix + "CanonicalDatasetRevisions", DatasetConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.SchemaSnapshotJson).IsRequired();
            b.HasIndex(x => new { x.CanonicalDatasetId, x.ContentHash }).IsUnique();
        });
    }
}

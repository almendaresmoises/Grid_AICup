using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Grid.Parsers;

namespace Grid.Parsers.EntityFrameworkCore;

public static class ParsersDbContextModelCreatingExtensions
{
    public static void ConfigureParsers(this ModelBuilder builder)
    {
        builder.Entity<ParserConfiguration>(b =>
        {
            b.ToTable(ParserConsts.DbTablePrefix + "ParserConfigurations", ParserConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.FileFormat).IsRequired().HasMaxLength(32);
            b.Property(x => x.PipelineJson).IsRequired();
            b.Property(x => x.OutputGcsPrefix).IsRequired().HasMaxLength(500);
        });

        builder.Entity<ParserRun>(b =>
        {
            b.ToTable(ParserConsts.DbTablePrefix + "ParserRuns", ParserConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Status).IsRequired().HasMaxLength(32);
            b.Property(x => x.FileName).IsRequired().HasMaxLength(260);
            b.Property(x => x.InputObject).IsRequired().HasMaxLength(1000);
            b.Property(x => x.OutputObject).IsRequired().HasMaxLength(1000);
            b.Property(x => x.PipelineHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.Error).HasMaxLength(4000);
            b.HasIndex(x => x.ParserConfigurationId);
        });

        builder.Entity<ParserConfigurationRevision>(b =>
        {
            b.ToTable(ParserConsts.DbTablePrefix + "ParserConfigurationRevisions", ParserConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
            b.Property(x => x.SnapshotJson).IsRequired();
            b.HasIndex(x => new { x.ParserConfigurationId, x.ContentHash }).IsUnique();
        });
    }
}

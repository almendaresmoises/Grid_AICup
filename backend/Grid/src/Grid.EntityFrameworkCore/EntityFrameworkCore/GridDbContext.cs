using Grid.Reconciliation;
using Grid.Parsers;
using Grid.Datasets;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Grid.Reconciliation.EntityFrameworkCore;
using Grid.Parsers.EntityFrameworkCore;
using Grid.Datasets.EntityFrameworkCore;

namespace Grid.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ConnectionStringName("Default")]
public class GridDbContext :
    AbpDbContext<GridDbContext>,
    IIdentityDbContext
{
    public DbSet<Dataset> Datasets { get; set; } = null!;
    public DbSet<DatasetRow> DatasetRows { get; set; } = null!;
    public DbSet<Package> Packages { get; set; } = null!;
    public DbSet<Profile> Profiles { get; set; } = null!;
    public DbSet<RegressionBaseline> RegressionBaselines { get; set; } = null!;
    public DbSet<ReconciliationRun> ReconciliationRuns { get; set; } = null!;
    public DbSet<ReconciliationRunSummary> ReconciliationRunSummaries { get; set; } = null!;
    public DbSet<MatchGroup> MatchGroups { get; set; } = null!;
    public DbSet<RunRecord> RunRecords { get; set; } = null!;
    public DbSet<ParserConfiguration> ParserConfigurations { get; set; } = null!;
    public DbSet<ParserRun> ParserRuns { get; set; } = null!;
    public DbSet<CanonicalDataset> CanonicalDatasets { get; set; } = null!;


    #region Entities from the modules

    /* Notice: We only implemented IIdentityProDbContext 
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityProDbContext .
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    // Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }

    #endregion

    public GridDbContext(DbContextOptions<GridDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureFeatureManagement();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureBlobStoring();
        builder.ConfigureReconciliation();
        builder.ConfigureParsers();
        builder.ConfigureDatasets();
    }
}

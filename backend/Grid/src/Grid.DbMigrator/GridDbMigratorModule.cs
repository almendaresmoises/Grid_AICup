using Grid.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace Grid.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(GridEntityFrameworkCoreModule),
    typeof(GridApplicationContractsModule)
)]
public class GridDbMigratorModule : AbpModule
{
}

using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Grid.Reconciliation;

[DependsOn(
    typeof(Grid.Parsers.GridParsersApplicationModule),
    typeof(GridReconciliationDomainModule),
    typeof(GridReconciliationApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class GridReconciliationApplicationModule : AbpModule
{
}

using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Grid.Reconciliation;

[DependsOn(
    typeof(GridReconciliationDomainSharedModule),
    typeof(AbpDddApplicationContractsModule)
)]
public class GridReconciliationApplicationContractsModule : AbpModule
{
}

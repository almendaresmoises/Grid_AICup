using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Grid.Reconciliation;

[DependsOn(
    typeof(GridReconciliationApplicationContractsModule),
    typeof(AbpAspNetCoreMvcModule)
)]
public class GridReconciliationHttpApiModule : AbpModule
{
}

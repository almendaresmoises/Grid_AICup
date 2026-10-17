using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Grid.Reconciliation;

[DependsOn(
    typeof(GridReconciliationDomainSharedModule),
    typeof(AbpDddDomainModule)
)]
public class GridReconciliationDomainModule : AbpModule
{
}

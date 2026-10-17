using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace Grid.Reconciliation.EntityFrameworkCore;

[DependsOn(
    typeof(GridReconciliationDomainModule),
    typeof(AbpEntityFrameworkCoreModule)
)]
public class GridReconciliationEntityFrameworkCoreModule : AbpModule
{
}

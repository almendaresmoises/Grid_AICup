using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Grid.Datasets;

[DependsOn(
    typeof(GridDatasetsDomainSharedModule),
    typeof(AbpDddDomainModule)
)]
public class GridDatasetsDomainModule : AbpModule
{
}

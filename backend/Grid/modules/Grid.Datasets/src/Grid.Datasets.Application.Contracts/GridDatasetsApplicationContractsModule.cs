using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Grid.Datasets;

[DependsOn(
    typeof(GridDatasetsDomainSharedModule),
    typeof(AbpDddApplicationContractsModule)
)]
public class GridDatasetsApplicationContractsModule : AbpModule
{
}

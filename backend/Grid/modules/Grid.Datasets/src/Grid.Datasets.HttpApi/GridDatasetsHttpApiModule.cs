using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Grid.Datasets;

[DependsOn(
    typeof(GridDatasetsApplicationContractsModule),
    typeof(AbpAspNetCoreMvcModule)
)]
public class GridDatasetsHttpApiModule : AbpModule
{
}

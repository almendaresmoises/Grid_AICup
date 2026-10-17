using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace Grid.Datasets.EntityFrameworkCore;

[DependsOn(
    typeof(GridDatasetsDomainModule),
    typeof(AbpEntityFrameworkCoreModule)
)]
public class GridDatasetsEntityFrameworkCoreModule : AbpModule
{
}

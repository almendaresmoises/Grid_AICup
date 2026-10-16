using Volo.Abp.Modularity;

namespace Grid;

[DependsOn(
    typeof(GridApplicationModule),
    typeof(GridDomainTestModule)
)]
public class GridApplicationTestModule : AbpModule
{

}

using Volo.Abp.Modularity;

namespace Grid;

[DependsOn(
    typeof(GridDomainModule),
    typeof(GridTestBaseModule)
)]
public class GridDomainTestModule : AbpModule
{

}

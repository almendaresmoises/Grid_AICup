using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Grid.Parsers;

[DependsOn(
    typeof(GridParsersApplicationContractsModule),
    typeof(AbpAspNetCoreMvcModule)
)]
public class GridParsersHttpApiModule : AbpModule
{
}

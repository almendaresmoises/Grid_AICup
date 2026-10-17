using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Grid.Parsers;

[DependsOn(
    typeof(GridParsersDomainSharedModule),
    typeof(AbpDddApplicationContractsModule)
)]
public class GridParsersApplicationContractsModule : AbpModule
{
}

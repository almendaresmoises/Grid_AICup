using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Grid.Parsers;

[DependsOn(
    typeof(GridParsersDomainSharedModule),
    typeof(AbpDddDomainModule)
)]
public class GridParsersDomainModule : AbpModule
{
}

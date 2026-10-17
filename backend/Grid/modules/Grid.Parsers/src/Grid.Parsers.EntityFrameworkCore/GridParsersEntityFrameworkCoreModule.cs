using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace Grid.Parsers.EntityFrameworkCore;

[DependsOn(
    typeof(GridParsersDomainModule),
    typeof(AbpEntityFrameworkCoreModule)
)]
public class GridParsersEntityFrameworkCoreModule : AbpModule
{
}

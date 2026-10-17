using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Grid.Parsers;

[DependsOn(
    typeof(Grid.Datasets.GridDatasetsApplicationModule),
    typeof(GridParsersDomainModule),
    typeof(GridParsersApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class GridParsersApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Configure<GoogleCloudOptions>(context.Services.GetConfiguration().GetSection("GoogleCloud"));
        context.Services.AddHostedService<LocalBucketInitializer>();
    }
}

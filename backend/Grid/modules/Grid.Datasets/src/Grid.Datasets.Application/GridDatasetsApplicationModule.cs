using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Grid.Datasets;

[DependsOn(
    typeof(GridDatasetsDomainModule),
    typeof(GridDatasetsApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class GridDatasetsApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Configure<GoogleCloudOptions>(context.Services.GetConfiguration().GetSection("GoogleCloud"));
    }
}

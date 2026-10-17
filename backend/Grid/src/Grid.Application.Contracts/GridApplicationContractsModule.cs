using Volo.Abp.Account;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Grid.Reconciliation;
using Grid.Parsers;
using Grid.Datasets;

namespace Grid;

[DependsOn(
    typeof(GridDatasetsApplicationContractsModule),
    typeof(GridParsersApplicationContractsModule),
    typeof(GridReconciliationApplicationContractsModule),
    typeof(GridDomainSharedModule),
    typeof(AbpFeatureManagementApplicationContractsModule),
    typeof(AbpSettingManagementApplicationContractsModule),
    typeof(AbpIdentityApplicationContractsModule),
    typeof(AbpAccountApplicationContractsModule),
    typeof(AbpPermissionManagementApplicationContractsModule)
)]
public class GridApplicationContractsModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        GridDtoExtensions.Configure();
    }
}

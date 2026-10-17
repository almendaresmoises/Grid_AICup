using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Account;
using Volo.Abp.Identity;
using Volo.Abp.Mapperly;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Grid.Reconciliation;
using Grid.Parsers;
using Grid.Datasets;

namespace Grid;

[DependsOn(
    typeof(GridDatasetsApplicationModule),
    typeof(GridParsersApplicationModule),
    typeof(GridReconciliationApplicationModule),
    typeof(GridDomainModule),
    typeof(GridApplicationContractsModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpFeatureManagementApplicationModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpAccountApplicationModule),
    typeof(AbpSettingManagementApplicationModule)
    )]
public class GridApplicationModule : AbpModule
{

}

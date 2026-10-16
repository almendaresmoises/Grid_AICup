using Grid.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Grid.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class GridController : AbpControllerBase
{
    protected GridController()
    {
        LocalizationResource = typeof(GridResource);
    }
}

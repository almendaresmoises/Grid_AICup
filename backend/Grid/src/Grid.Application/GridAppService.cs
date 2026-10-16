using Grid.Localization;
using Volo.Abp.Application.Services;

namespace Grid;

/* Inherit your application services from this class.
 */
public abstract class GridAppService : ApplicationService
{
    protected GridAppService()
    {
        LocalizationResource = typeof(GridResource);
    }
}

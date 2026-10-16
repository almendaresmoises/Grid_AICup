using Microsoft.Extensions.Localization;
using Grid.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace Grid;

[Dependency(ReplaceServices = true)]
public class GridBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<GridResource> _localizer;

    public GridBrandingProvider(IStringLocalizer<GridResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];

    public override string? LogoUrl => "~/images/logo/leptonxlite/logo-light-thumbnail.png";

    public override string? LogoIconUrl => "~/images/logo/leptonxlite/logo-dark-thumbnail.png";
}

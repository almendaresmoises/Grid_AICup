using Volo.Abp.Settings;

namespace Grid.Settings;

public class GridSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(GridSettings.MySetting1));
    }
}

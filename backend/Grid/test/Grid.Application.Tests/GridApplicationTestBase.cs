using Volo.Abp.Modularity;

namespace Grid;

public abstract class GridApplicationTestBase<TStartupModule> : GridTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}

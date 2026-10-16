using Volo.Abp.Modularity;

namespace Grid;

/* Inherit from this class for your domain layer tests. */
public abstract class GridDomainTestBase<TStartupModule> : GridTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}

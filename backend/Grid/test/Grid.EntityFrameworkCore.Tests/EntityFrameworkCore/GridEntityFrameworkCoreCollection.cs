using Xunit;

namespace Grid.EntityFrameworkCore;

[CollectionDefinition(GridTestConsts.CollectionDefinitionName)]
public class GridEntityFrameworkCoreCollection : ICollectionFixture<GridEntityFrameworkCoreFixture>
{

}

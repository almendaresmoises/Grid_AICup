using Grid.Samples;
using Xunit;

namespace Grid.EntityFrameworkCore.Applications;

[Collection(GridTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<GridEntityFrameworkCoreTestModule>
{

}

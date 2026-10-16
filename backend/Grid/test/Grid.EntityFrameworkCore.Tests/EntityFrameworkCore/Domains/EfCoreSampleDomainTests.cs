using Grid.Samples;
using Xunit;

namespace Grid.EntityFrameworkCore.Domains;

[Collection(GridTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<GridEntityFrameworkCoreTestModule>
{

}

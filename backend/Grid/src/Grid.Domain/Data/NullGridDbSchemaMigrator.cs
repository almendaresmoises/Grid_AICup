using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Grid.Data;

/* This is used if database provider does't define
 * IGridDbSchemaMigrator implementation.
 */
public class NullGridDbSchemaMigrator : IGridDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}

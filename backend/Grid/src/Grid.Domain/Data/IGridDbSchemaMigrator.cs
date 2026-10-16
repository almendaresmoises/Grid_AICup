using System.Threading.Tasks;

namespace Grid.Data;

public interface IGridDbSchemaMigrator
{
    Task MigrateAsync();
}

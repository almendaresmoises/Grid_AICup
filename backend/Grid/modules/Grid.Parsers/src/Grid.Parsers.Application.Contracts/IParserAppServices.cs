using Volo.Abp.Application.Services;

namespace Grid.Parsers;

public interface IParserAppService : IApplicationService
{
    Task<List<ParserConfigurationDto>> GetListAsync();

    Task<ParserConfigurationDto> GetAsync(Guid id);

    Task<ParserConfigurationDto> CreateAsync(CreateParserConfigurationDto input);

    Task<ParserConfigurationDto> UpdateAsync(Guid id, UpdateParserConfigurationDto input);

    Task DeleteAsync(Guid id);
}

public interface IParserRunAppService : IApplicationService
{
    Task<List<ParserRunDto>> GetListAsync(Guid parserId);

    Task<ParserRunDto> StartAsync(Guid parserId, string fileName, byte[] content);

    Task CompleteAsync(Guid runId, CompleteParserRunDto input, string? workerSecret);
}

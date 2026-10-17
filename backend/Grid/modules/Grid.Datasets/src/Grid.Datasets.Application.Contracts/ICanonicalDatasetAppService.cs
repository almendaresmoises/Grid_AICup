using Volo.Abp.Application.Services;

namespace Grid.Datasets;

public interface ICanonicalDatasetAppService : IApplicationService
{
    Task<List<CanonicalDatasetDto>> GetListAsync();

    Task<CanonicalDatasetDto> GetAsync(Guid id);

    Task<CanonicalDatasetDto> CreateAsync(CreateCanonicalDatasetDto input);

    Task<CanonicalDatasetDto> UpdateAsync(Guid id, UpdateCanonicalDatasetDto input);

    Task DeleteAsync(Guid id);

    Task<DatasetOutputLocationDto> GetOutputLocationAsync(Guid id, string? workerSecret, bool authenticated);

    Task<DatasetOutputLocationDto> GetOutputLocationByCodeAsync(string code, string? workerSecret, bool authenticated);
}

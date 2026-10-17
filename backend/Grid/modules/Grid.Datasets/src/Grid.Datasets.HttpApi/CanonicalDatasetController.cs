using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace Grid.Datasets;

[Authorize]
[Route("api/datasets")]
public class CanonicalDatasetController : AbpController
{
    private readonly ICanonicalDatasetAppService _datasets;

    public CanonicalDatasetController(ICanonicalDatasetAppService datasets)
    {
        _datasets = datasets;
    }

    [AllowAnonymous]
    [HttpGet("{id}/output-location")]
    public Task<DatasetOutputLocationDto> GetOutputLocationAsync(Guid id)
    {
        return _datasets.GetOutputLocationAsync(id, WorkerSecret(), CurrentUser.IsAuthenticated);
    }

    [AllowAnonymous]
    [HttpGet("by-code/{code}/output-location")]
    public Task<DatasetOutputLocationDto> GetOutputLocationByCodeAsync(string code)
    {
        return _datasets.GetOutputLocationByCodeAsync(code, WorkerSecret(), CurrentUser.IsAuthenticated);
    }

    private string WorkerSecret() => Request.Headers["X-Grid-Worker-Secret"].ToString();
}

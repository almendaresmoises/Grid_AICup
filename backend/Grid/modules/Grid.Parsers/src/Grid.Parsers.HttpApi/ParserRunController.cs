using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace Grid.Parsers;

[Authorize]
[Route("api/parsers")]
public class ParserRunController : AbpController
{
    private readonly IParserRunAppService _runs;

    public ParserRunController(IParserRunAppService runs)
    {
        _runs = runs;
    }

    [HttpGet("{id}/runs")]
    public Task<List<ParserRunDto>> GetListAsync(Guid id)
    {
        return _runs.GetListAsync(id);
    }

    [HttpPost("{id}/runs")]
    [RequestSizeLimit(52_428_800)]
    public async Task<ParserRunDto> UploadAsync(Guid id, IFormFile file)
    {
        if (file == null || file.Length == 0) throw new UserFriendlyException("Choose a file to parse.");
        using var memory = new MemoryStream();
        await file.CopyToAsync(memory);
        return await _runs.StartAsync(id, file.FileName, memory.ToArray());
    }

    [AllowAnonymous]
    [HttpPost("runs/{runId}/complete")]
    public Task CompleteAsync(Guid runId, [FromBody] CompleteParserRunDto input)
    {
        var secret = Request.Headers["X-Grid-Worker-Secret"].ToString();
        return _runs.CompleteAsync(runId, input, secret);
    }
}

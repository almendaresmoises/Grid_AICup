using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grid.Parsers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Grid.Reconciliation;

[Authorize]
public class AiConfigurationAppService : ApplicationService, IAiConfigurationAppService
{
    private readonly IRepository<AiConfiguration, Guid> _jobs;
    private readonly IRepository<ParserRun, Guid> _parserRuns;
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly IBackgroundJobManager _background;
    private readonly ReconciliationData _data;
    private readonly IProfileAppService _profileService;
    private readonly IRunAppService _runService;

    public AiConfigurationAppService(IRepository<AiConfiguration, Guid> jobs, IRepository<ParserRun, Guid> parserRuns,
        IRepository<Profile, Guid> profiles, IBackgroundJobManager background, ReconciliationData data,
        IProfileAppService profileService, IRunAppService runService)
    {
        _jobs = jobs; _parserRuns = parserRuns; _profiles = profiles; _background = background;
        _data = data; _profileService = profileService; _runService = runService;
    }

    public async Task<AiConfigurationDto> CreateAsync(StartAiConfigurationDto input)
    {
        input.Context = (input.Context ?? "").Trim();
        if (input.RequestId == Guid.Empty || input.Files == null || input.Files.Count is < 2 or > 100 || input.Context.Length > 1200)
            throw new UserFriendlyException("Supply a request id, 2–100 files and at most 1,200 context characters.");
        var codes = input.Files.Select(file => file.DatasetCode).Distinct().ToList();
        if (codes.Count is < 2 or > 3 || input.Files.Select(file => file.RunId).Distinct().Count() != input.Files.Count)
            throw new UserFriendlyException("Select two or three datasets with distinct uploaded files. Each dataset may contain multiple files.");
        foreach (var file in input.Files)
        {
            var run = await _parserRuns.GetAsync(file.RunId);
            if (run.CreatorId != CurrentUser.GetId() || run.ParserConfigurationId != file.ParserId ||
                run.ExtraProperties.GetValueOrDefault("DatasetCode")?.ToString() != file.DatasetCode)
                throw new UserFriendlyException("Every file must be an upload from this configuration's dataset and parser.");
            file.FileName = run.FileName;
        }
        input.Files = input.Files.OrderBy(file => file.DatasetCode).ThenBy(file => file.RunId).ToList();
        var json = JsonSerializer.Serialize(input, ProfileJson.Storage);
        var existing = await _jobs.FindAsync(input.RequestId);
        if (existing != null)
        {
            EnsureOwner(existing);
            if (existing.ManifestJson != json) throw new UserFriendlyException("This request id already belongs to a different upload set.");
            return Map(existing);
        }
        var job = new AiConfiguration(input.RequestId) { ManifestJson = json, CreatorId = CurrentUser.GetId() };
        await _jobs.InsertAsync(job, autoSave: true);
        await _background.EnqueueAsync(new AiConfigurationJobArgs { Id = job.Id });
        return Map(job);
    }

    public async Task<AiConfigurationDto> GetAsync(Guid id)
    {
        var job = await _jobs.GetAsync(id);
        EnsureOwner(job);
        return Map(job);
    }

    public async Task<List<AiConfigurationDto>> GetListAsync()
    {
        var owner = CurrentUser.GetId();
        var query = (await _jobs.GetQueryableAsync()).Where(job => job.CreatorId == owner).OrderByDescending(job => job.CreationTime).Take(10);
        return (await AsyncExecuter.ToListAsync(query)).Select(Map).ToList();
    }

    public async Task<ConfigurationValidationDto> GetValidationAsync(Guid id)
    {
        var job = await _jobs.GetAsync(id);
        EnsureOwner(job);
        if (job.Status != "Completed" || !job.ProfileId.HasValue)
            throw new UserFriendlyException("Wait for configuration validation to complete.");
        var manifest = Manifest(job);
        var profile = await _profiles.GetAsync(job.ProfileId.Value);
        var definition = ProfileJson.Parse(profile.DefinitionJson);
        if (!definition.Datasets.ToHashSet().SetEquals(manifest.Files.Select(file => file.DatasetCode)))
            return new ConfigurationValidationDto { Errors = ["The profile must include every uploaded dataset. Start a new setup to change its input groups."] };
        var rows = await _data.LoadAsync(definition.Datasets, multiParserRunIds: RunIds(manifest), requireParserRuns: true);
        var report = AiConfigurationValidator.Assess(definition, rows);
        var original = Map(job).Validation;
        report.Warnings.AddRange(original?.Warnings.Where(warning => warning.StartsWith("Parsing:", StringComparison.Ordinal)) ?? []);
        report.Revision = Revision(profile.DefinitionJson, job.ManifestJson);
        return report;
    }

    public async Task<RunDetailDto> RunAsync(Guid id, RunValidatedConfigurationDto input)
    {
        var job = await _jobs.GetAsync(id);
        EnsureOwner(job);
        var report = await GetValidationAsync(id);
        if (!report.Passed) throw new UserFriendlyException(string.Join(" ", report.Errors));
        if (input.Revision != report.Revision)
            throw new UserFriendlyException("Configuration changed. Refresh its validation preview before running.");
        if (job.RunId.HasValue && input.Revision == job.ExecutedRevision)
            return await _runService.GetAsync(job.RunId.Value);
        var profile = await _profiles.GetAsync(job.ProfileId!.Value);
        if (profile.Status != ProfileStatus.Approved || profile.ApprovedDefinitionJson != profile.DefinitionJson)
            await _profileService.ApproveAsync(profile.Id, new SaveProfileDto { DefinitionJson = profile.DefinitionJson, CreatedWithAi = true });
        var run = await _runService.CreateAsync(new CreateRunDto { ProfileId = profile.Id, MultiParserRunIds = RunIds(Manifest(job)) });
        job.RunId = run.Id;
        job.ExecutedRevision = input.Revision;
        await _jobs.UpdateAsync(job, autoSave: true);
        return run;
    }

    private void EnsureOwner(AiConfiguration job)
    {
        if (job.CreatorId != CurrentUser.GetId()) throw new Volo.Abp.Authorization.AbpAuthorizationException();
    }

    internal static StartAiConfigurationDto Manifest(AiConfiguration job) => JsonSerializer.Deserialize<StartAiConfigurationDto>(job.ManifestJson, ProfileJson.Storage)!;
    internal static Dictionary<string, List<Guid>> RunIds(StartAiConfigurationDto manifest) => manifest.Files.GroupBy(file => file.DatasetCode).ToDictionary(group => group.Key, group => group.Select(file => file.RunId).ToList());
    internal static string Revision(string definition, string manifest)
    {
        var canonical = ProfileJson.Parse(definition);
        canonical.Version = 0; // Approval increments a version, but does not change the reviewed rules.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("validated-v1\n" + ProfileJson.Write(canonical) + "\n" + manifest)));
    }
    internal static AiConfigurationDto Map(AiConfiguration job)
    {
        var result = JsonSerializer.Deserialize<AiConfigurationDto>(job.ResultJson, ProfileJson.Storage) ?? new();
        result.Id = job.Id; result.Status = job.Status; result.Error = job.Error; result.Files = Manifest(job).Files;
        return result;
    }
}

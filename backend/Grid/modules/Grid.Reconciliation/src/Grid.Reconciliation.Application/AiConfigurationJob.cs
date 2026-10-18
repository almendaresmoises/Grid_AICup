using System.Text.Json;
using Grid.Parsers;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Grid.Reconciliation;

public class AiConfigurationJobArgs { public Guid Id { get; set; } }
public sealed class ParserRunsPendingException : InvalidOperationException
{
    public ParserRunsPendingException() : base("Waiting for parser runs; the durable queue will retry.") { }
}

/// <summary>The DB-backed queue retries after API restarts and while parsers are queued.</summary>
public class AiConfigurationJob : AsyncBackgroundJob<AiConfigurationJobArgs>, ITransientDependency
{
    private readonly IRepository<AiConfiguration, Guid> _jobs;
    private readonly IRepository<ParserRun, Guid> _parsers;
    private readonly IRepository<Package, Guid> _packages;
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly ICanonicalProfileDraftGenerator _assistant;
    private readonly ReconciliationData _data;
    private readonly IUnitOfWorkManager _units;
    private readonly IClock _clock;

    public AiConfigurationJob(IRepository<AiConfiguration, Guid> jobs, IRepository<ParserRun, Guid> parsers,
        IRepository<Package, Guid> packages, IRepository<Profile, Guid> profiles,
        ICanonicalProfileDraftGenerator assistant, ReconciliationData data, IUnitOfWorkManager units, IClock clock)
    { _jobs = jobs; _parsers = parsers; _packages = packages; _profiles = profiles; _assistant = assistant; _data = data; _units = units; _clock = clock; }

    public override async Task ExecuteAsync(AiConfigurationJobArgs args)
    {
        using var unit = _units.Begin(requiresNew: true, isTransactional: true);
        var job = await _jobs.FindAsync(args.Id);
        if (job == null || job.Status is "Completed" or "Failed") return;
        var manifest = AiConfigurationAppService.Manifest(job);
        var runs = new List<ParserRun>();
        try
        {
            foreach (var file in manifest.Files) runs.Add(await _parsers.GetAsync(file.RunId));
            if (runs.Any(run => run.Status == "Queued") && _clock.Now - job.CreationTime < TimeSpan.FromHours(1))
                throw new ParserRunsPendingException();
            var parsingWarnings = new List<string>();
            if (runs.Any(run => run.Status != "Succeeded"))
                throw new UserFriendlyException("One or more uploaded files failed to parse or timed out. Review the parser errors; transformations were not removed.");
            foreach (var run in runs)
            {
                using var report = JsonDocument.Parse(run.ReportJson ?? "{}");
                if (!report.RootElement.TryGetProperty("output_rows", out var count) || count.GetInt32() == 0)
                    throw new UserFriendlyException($"{run.FileName} produced no usable rows.");
                if (report.RootElement.TryGetProperty("quarantined_rows", out var quarantined) && quarantined.GetInt32() > 0)
                    throw new UserFriendlyException($"{run.FileName} quarantined records. Correct its parser before completing configuration.");
                if (report.RootElement.TryGetProperty("input_rows", out var input))
                {
                    var excluded = input.GetInt32() - count.GetInt32();
                    if (excluded < 0) throw new UserFriendlyException($"{run.FileName} has inconsistent parser row counts.");
                    if (excluded > 0) parsingWarnings.Add($"Parsing: {run.FileName} excluded {excluded} rows; review its footer/filter rules.");
                }
            }
            var codes = manifest.Files.Select(file => file.DatasetCode).Distinct().ToList();
            var rows = await _data.LoadAsync(codes, multiParserRunIds: AiConfigurationAppService.RunIds(manifest), requireParserRuns: true);
            foreach (var code in codes)
            {
                var expected = manifest.Files.Where(file => file.DatasetCode == code).Sum(file =>
                {
                    using var report = JsonDocument.Parse(runs.Single(run => run.Id == file.RunId).ReportJson!);
                    return report.RootElement.GetProperty("output_rows").GetInt32();
                });
                if (rows[code].Count != expected)
                    throw new UserFriendlyException($"{code}: loaded canonical rows do not match parser output counts. Reprocess these files.");
            }
            var proposal = await _assistant.DraftAsync(codes, rows, manifest.Context);
            var definition = ProfileJson.Parse(proposal.DefinitionJson);
            var validation = AiConfigurationValidator.Assess(definition, rows);
            validation.Warnings.AddRange(proposal.Warnings);
            validation.Warnings.AddRange(parsingWarnings);
            if (!validation.Passed) throw new UserFriendlyException(string.Join(" ", validation.Errors));
            var package = new Package(Guid.NewGuid()) { Name = definition.Name[..Math.Min(definition.Name.Length, 128)], Description = "Configured from validated canonical parser outputs." };
            var profile = new Profile(Guid.NewGuid()) { PackageId = package.Id, Name = definition.Name, DefinitionJson = proposal.DefinitionJson, CreatorId = job.CreatorId };
            profile.ExtraProperties["CreatedWithAi"] = true;
            profile.ExtraProperties["AiConfigurationId"] = job.Id.ToString();
            profile.ExtraProperties["GenerationSource"] = proposal.Source;
            package.CreatorId = job.CreatorId;
            await _packages.InsertAsync(package, autoSave: true);
            await _profiles.InsertAsync(profile, autoSave: true);
            job.ProfileId = profile.Id;
            validation.Revision = AiConfigurationAppService.Revision(profile.DefinitionJson, job.ManifestJson);
            job.ResultJson = JsonSerializer.Serialize(new AiConfigurationDto
            {
                Source = proposal.Source, Validation = validation,
                Package = new PackageDto { Id = package.Id, Name = package.Name, ProfileId = profile.Id, ProfileName = profile.Name, ProfileStatus = "Draft", CreatedWithAi = true },
            }, ProfileJson.Storage);
            job.Status = "Completed";
        }
        catch (Exception ex) when (ex is not ParserRunsPendingException)
        {
            Logger.LogError(ex, "Configuration {ConfigurationId} failed validation", job.Id);
            job.Status = "Failed";
            job.Error = ex is UserFriendlyException or ReconciliationException ? ex.Message : "Configuration could not be validated. Review parser outputs and retry setup.";
        }
        await _jobs.UpdateAsync(job, autoSave: true);
        await unit.CompleteAsync();
    }
}

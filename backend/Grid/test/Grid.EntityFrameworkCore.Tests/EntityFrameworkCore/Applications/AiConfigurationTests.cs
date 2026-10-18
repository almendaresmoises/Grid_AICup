using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Grid.Datasets;
using Grid.Parsers;
using Grid.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace Grid.EntityFrameworkCore.Applications;

[DependsOn(typeof(GridEntityFrameworkCoreTestModule))]
public class AiConfigurationTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Replace(ServiceDescriptor.Singleton<ICanonicalOutputReader>(new ConfigurationTestOutput()));
        context.Services.Replace(ServiceDescriptor.Singleton<ICanonicalProfileDraftGenerator>(new ConfigurationTestDraft()));
    }
}

public class ConfigurationTestOutput : ICanonicalOutputReader
{
    public Dictionary<string, List<Dictionary<string, string>>> Files { get; } = [];
    public Task<List<Dictionary<string, string>>> ReadAsync(string uri) => Task.FromResult(Files[uri]);
}

public class ConfigurationTestDraft : ICanonicalProfileDraftGenerator
{
    public Task<ProposalDto> DraftAsync(List<string> codes, Dictionary<string, List<Dictionary<string, string>>> rows, string context)
    {
        var profile = new ProfileDefinition { Name = "Validated uploads", Datasets = codes };
        profile.Rules.Add(new RuleDefinition
        {
            Id = "FULL", Type = "exact", Datasets = codes,
            Keys = [
                new() { Type = "text", Fields = codes.ToDictionary(code => code, _ => "id") },
                new() { Type = "amount", Fields = codes.ToDictionary(code => code, _ => "amount") },
                new() { Type = "text", Fields = codes.ToDictionary(code => code, _ => "company_code") },
            ],
        });
        return Task.FromResult(new ProposalDto { DefinitionJson = ProfileJson.Write(profile), Source = "test" });
    }
}

[Collection(GridTestConsts.CollectionDefinitionName)]
public class AiConfigurationTests : GridTestBase<AiConfigurationTestModule>
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly ICurrentPrincipalAccessor _principal;
    private readonly IAiConfigurationAppService _service;
    private readonly AiConfigurationJob _worker;
    private readonly IRepository<AiConfiguration, Guid> _jobs;
    private readonly IRepository<ParserRun, Guid> _runs;
    private readonly IRepository<Profile, Guid> _profiles;

    public AiConfigurationTests()
    {
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
        _service = GetRequiredService<IAiConfigurationAppService>();
        _worker = GetRequiredService<AiConfigurationJob>();
        _jobs = GetRequiredService<IRepository<AiConfiguration, Guid>>();
        _runs = GetRequiredService<IRepository<ParserRun, Guid>>();
        _profiles = GetRequiredService<IRepository<Profile, Guid>>();
    }

    private IDisposable As(Guid user) => _principal.Change(new ClaimsPrincipal(new ClaimsIdentity([
        new Claim(AbpClaimTypes.UserId, user.ToString()),
    ], "test")));

    private async Task<StartAiConfigurationDto> UploadsAsync(bool queued = false, bool corruptCompany = false)
    {
        var manifest = new StartAiConfigurationDto { RequestId = Guid.NewGuid() };
        var output = (ConfigurationTestOutput)GetRequiredService<ICanonicalOutputReader>();
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var code in new[] { "A", "B" })
            {
                var dataset = new CanonicalDataset
                {
                    Code = code, Name = code, GcsPrefix = code,
                    SchemaColumnsJson = """[{"name":"id","polarsType":"pl.String","isRequired":true},{"name":"amount","polarsType":"pl.Decimal(18,2)","isRequired":true},{"name":"company_code","polarsType":"pl.String","isRequired":false}]""",
                };
                await GetRequiredService<IRepository<CanonicalDataset, Guid>>().InsertAsync(dataset, autoSave: true);
                var parser = new ParserConfiguration { Name = code + Guid.NewGuid().ToString("N") };
                await GetRequiredService<IRepository<ParserConfiguration, Guid>>().InsertAsync(parser, autoSave: true);
                var numberOfFiles = code == "A" ? 2 : 1;
                for (var i = 0; i < numberOfFiles; i++)
                {
                    var id = Guid.NewGuid();
                    var run = new ParserRun(id)
                    {
                        CreatorId = _owner, ParserConfigurationId = parser.Id,
                        Status = queued ? "Queued" : "Succeeded", FileName = $"{code}-{i}.csv",
                        OutputObject = $"gs://test/{id}/data.parquet",
                        ReportJson = $"{{\"input_rows\":{(code == "B" ? 2 : 1)},\"output_rows\":{(code == "B" ? 2 : 1)},\"quarantined_rows\":0}}",
                    };
                    run.ExtraProperties["DatasetId"] = dataset.Id.ToString();
                    run.ExtraProperties["DatasetCode"] = code;
                    run.ExtraProperties["SchemaColumnsJson"] = dataset.SchemaColumnsJson;
                    await _runs.InsertAsync(run, autoSave: true);
                    output.Files[run.OutputObject + ".csv"] = (code == "B" ? new[] { 0, 1 } : new[] { i }).Select(index => new Dictionary<string, string>
                    {
                        ["id"] = $"00{index}", ["amount"] = "100.00", ["company_code"] = corruptCompany ? "" : "101",
                    }).ToList();
                    manifest.Files.Add(new ConfigurationFileDto { DatasetCode = code, ParserId = parser.Id, ParserCode = parser.Name, RunId = run.Id });
                }
            }
        });
        return manifest;
    }

    [Fact]
    public async Task Multiple_files_are_combined_and_current_revision_is_required_for_idempotent_execution()
    {
        using var user = As(_owner);
        var manifest = await UploadsAsync();
        var created = await _service.CreateAsync(manifest);
        var retry = await _service.CreateAsync(manifest);
        Assert.Equal(created.Id, retry.Id);
        await _worker.ExecuteAsync(new AiConfigurationJobArgs { Id = created.Id });
        var completed = await _service.GetAsync(created.Id);
        Assert.Equal("Completed", completed.Status);
        Assert.Equal(3, completed.Files.Count);
        Assert.Equal(4, completed.Validation!.TotalRecords);
        Assert.Equal(4, completed.Validation.FullMatchRecords);
        Assert.Equal("Draft", completed.Package!.ProfileStatus);

        var report = await _service.GetValidationAsync(created.Id);
        var first = await _service.RunAsync(created.Id, new RunValidatedConfigurationDto { Revision = report.Revision });
        var repeated = await _service.RunAsync(created.Id, new RunValidatedConfigurationDto { Revision = report.Revision });
        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal(2, first.Datasets["A"].Count);
        Assert.Equal(2, first.Datasets["B"].Count);

        var profileService = GetRequiredService<IProfileAppService>();
        var saved = await profileService.GetAsync(completed.Package.ProfileId);
        var definition = ProfileJson.Parse(saved.DefinitionJson);
        definition.Name = "Reviewed configuration";
        await profileService.UpdateAsync(saved.Id, new SaveProfileDto { DefinitionJson = ProfileJson.Write(definition) });
        await Assert.ThrowsAsync<UserFriendlyException>(() => _service.RunAsync(created.Id, new RunValidatedConfigurationDto { Revision = report.Revision }));
        var refreshed = await _service.GetValidationAsync(created.Id);
        Assert.NotEqual(report.Revision, refreshed.Revision);
        var reviewed = await _service.RunAsync(created.Id, new RunValidatedConfigurationDto { Revision = refreshed.Revision });
        Assert.NotEqual(first.Id, reviewed.Id);
        Assert.Equal("Reviewed configuration", reviewed.ProfileName);
    }

    [Fact]
    public async Task Queued_work_is_retried_from_durable_manifest_and_other_users_cannot_access_it()
    {
        using var user = As(_owner);
        var manifest = await UploadsAsync(queued: true);
        var created = await _service.CreateAsync(manifest);
        await Assert.ThrowsAsync<ParserRunsPendingException>(() => _worker.ExecuteAsync(new AiConfigurationJobArgs { Id = created.Id }));
        Assert.Equal("WaitingForParsers", (await _service.GetAsync(created.Id)).Status);
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var file in manifest.Files)
            {
                var run = await _runs.GetAsync(file.RunId);
                run.Status = "Succeeded";
                await _runs.UpdateAsync(run, autoSave: true);
            }
        });
        await _worker.ExecuteAsync(new AiConfigurationJobArgs { Id = created.Id });
        Assert.Equal("Completed", (await _service.GetAsync(created.Id)).Status);
        using var stranger = As(Guid.NewGuid());
        await Assert.ThrowsAsync<AbpAuthorizationException>(() => _service.GetAsync(created.Id));
        Assert.Empty(await _service.GetListAsync());
    }

    [Fact]
    public async Task Corrupted_output_cannot_publish_a_profile_even_when_parser_status_is_succeeded()
    {
        using var user = As(_owner);
        var created = await _service.CreateAsync(await UploadsAsync(corruptCompany: true));
        await _worker.ExecuteAsync(new AiConfigurationJobArgs { Id = created.Id });
        var failed = await _service.GetAsync(created.Id);
        Assert.Equal("Failed", failed.Status);
        Assert.Contains("empty in every parsed record", failed.Error);
        Assert.Null(failed.Package);
        await WithUnitOfWorkAsync(async () => Assert.False(await _profiles.AnyAsync(profile => profile.Name == "Validated uploads")));
    }
}

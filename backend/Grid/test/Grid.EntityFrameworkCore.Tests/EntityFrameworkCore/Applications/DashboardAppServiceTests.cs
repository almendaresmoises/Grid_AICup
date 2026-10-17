using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grid.Reconciliation;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Validation;
using Xunit;

namespace Grid.EntityFrameworkCore.Applications;

[Collection(GridTestConsts.CollectionDefinitionName)]
public class DashboardAppServiceTests : GridEntityFrameworkCoreTestBase
{
    private readonly IDashboardAppService _dashboard;
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly IRepository<ReconciliationRun, Guid> _runs;
    private readonly IRepository<ReconciliationRunSummary, Guid> _summaries;
    private static DateTime Today => DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);

    public DashboardAppServiceTests()
    {
        _dashboard = GetRequiredService<IDashboardAppService>();
        _profiles = GetRequiredService<IRepository<Profile, Guid>>();
        _runs = GetRequiredService<IRepository<ReconciliationRun, Guid>>();
        _summaries = GetRequiredService<IRepository<ReconciliationRunSummary, Guid>>();
    }

    private async Task<Profile> ProfileAsync(string name)
    {
        var profile = new Profile(Guid.NewGuid()) { Name = name, PackageId = Guid.NewGuid() };
        await _profiles.InsertAsync(profile, autoSave: true);
        return profile;
    }

    private async Task<ReconciliationRun> RunAsync(Profile profile, DateTime date, int full, int partial, int exceptions)
    {
        var run = new ReconciliationRun(Guid.NewGuid()) { ProfileId = profile.Id, PackageId = profile.PackageId, CreationTime = date, TotalRecords = full + partial + exceptions, MatchedRecords = full + partial, UnresolvedRecords = exceptions };
        await _runs.InsertAsync(run, autoSave: true);
        var result = new ReconcileResult
        {
            Statistics = new ReconcileStatistics { TotalRecords = run.TotalRecords, MatchedRecords = run.MatchedRecords, UnresolvedRecords = exceptions },
            MatchedGroups = partial == 0 ? [] : [new MatchGroupResult { RuleType = "partial", Records = new Dictionary<string, List<int>> { ["A"] = Enumerable.Range(0, partial).ToList() } }],
        };
        await _summaries.InsertAsync(ReconciliationRunSummary.From(run, result), autoSave: true);
        return run;
    }

    [Fact]
    public async Task Aggregates_outcomes_in_database_fills_empty_days_and_ranks_weighted_rates()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var low = await ProfileAsync("Low");
            var medium = await ProfileAsync("Medium");
            var high = await ProfileAsync("High");
            await RunAsync(low, Today.AddDays(-1).AddHours(12), 1, 0, 0);
            await RunAsync(low, Today.AddHours(12), 0, 0, 99);
            await RunAsync(medium, Today.AddDays(-1).AddHours(13), 5, 5, 10);
            await RunAsync(high, Today.AddHours(13), 3, 0, 0);

            var result = await _dashboard.GetAsync(new DashboardInput { Days = 3 });
            Assert.Equal(3, result.Days.Count);
            Assert.Equal(0, result.Days[0].CompletedRuns);
            Assert.Equal(4, result.CompletedRuns);
            Assert.Equal(123, result.TotalRecords);
            Assert.Equal(9, result.FullMatchRecords);
            Assert.Equal(5, result.PartialMatchRecords);
            Assert.Equal(109, result.ExceptionRecords);
            Assert.Equal(new[] { low.Id, medium.Id, high.Id }, result.LowestMatchProfiles.Select(row => row.ProfileId));
            Assert.Equal(1m, result.LowestMatchProfiles[0].MatchRate); // Not a misleading average of 100% and 0%.
            Assert.Equal(50m, result.LowestMatchProfiles[1].MatchRate);
        });
    }

    [Fact]
    public async Task Includes_only_selected_period_and_at_most_five_profiles_with_records()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var profiles = new List<Profile>();
            for (var index = 0; index < 6; index++)
            {
                var profile = await ProfileAsync($"Profile {index}");
                profiles.Add(profile);
                await RunAsync(profile, Today.AddHours(1), index * 10, 0, 100 - index * 10);
            }
            var excluded = await ProfileAsync("Excluded");
            await RunAsync(excluded, Today.AddTicks(-1), 0, 0, 1000);
            await RunAsync(excluded, Today.AddDays(1), 0, 0, 1000);
            await RunAsync(excluded, Today.AddHours(2), 0, 0, 0);
            await ProfileAsync("Never run");

            var result = await _dashboard.GetAsync(new DashboardInput { Days = 1 });
            Assert.Equal(600, result.TotalRecords);
            Assert.Equal(7, result.CompletedRuns);
            Assert.Equal(5, result.LowestMatchProfiles.Count);
            Assert.Equal(profiles.Take(5).Select(profile => profile.Id), result.LowestMatchProfiles.Select(profile => profile.ProfileId));
        });
    }

    [Fact]
    public async Task Deleted_runs_remove_their_summary_and_empty_period_has_no_invented_rate()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var profile = await ProfileAsync("Deleted run");
            var run = await RunAsync(profile, Today.AddHours(1), 2, 1, 3);
            await _runs.DeleteAsync(run.Id, autoSave: true);
            Assert.False(await _summaries.AnyAsync(row => row.Id == run.Id));
            var result = await _dashboard.GetAsync(new DashboardInput { Days = 7 });
            Assert.Equal(0, result.CompletedRuns);
            Assert.Null(result.MatchRate);
            Assert.Empty(result.LowestMatchProfiles);
            Assert.All(result.Days, day => Assert.Equal(0, day.TotalRecords));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task Rejects_unbounded_periods(int days)
    {
        var error = await Record.ExceptionAsync(() => _dashboard.GetAsync(new DashboardInput { Days = days }));
        Assert.True(error is UserFriendlyException or AbpValidationException);
    }
}

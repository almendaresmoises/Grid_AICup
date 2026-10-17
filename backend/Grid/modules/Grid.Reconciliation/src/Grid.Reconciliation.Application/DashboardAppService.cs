using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace Grid.Reconciliation;

[Authorize]
public class DashboardAppService : ApplicationService, IDashboardAppService
{
    private readonly IRepository<ReconciliationRunSummary, Guid> _summaries;
    private readonly IRepository<Profile, Guid> _profiles;

    public DashboardAppService(IRepository<ReconciliationRunSummary, Guid> summaries, IRepository<Profile, Guid> profiles)
    {
        _summaries = summaries;
        _profiles = profiles;
    }

    public async Task<DashboardDto> GetAsync(DashboardInput input)
    {
        if (input.Days is < 1 or > 90) throw new UserFriendlyException("Select a period between 1 and 90 days.");
        var now = DateTime.UtcNow;
        var through = DateTime.SpecifyKind(Clock.Now.Date.AddDays(1), DateTimeKind.Unspecified);
        var from = through.AddDays(-input.Days);
        var source = (await _summaries.GetQueryableAsync()).Where(row => row.CreatedAt >= from && row.CreatedAt < through);

        // Only database aggregates are materialized. No run entities, input snapshots,
        // match group records, or evidence JSON are read by a dashboard request.
        var daily = await AsyncExecuter.ToListAsync(source.GroupBy(row => row.CreatedAt.Date).Select(group => new
        {
            Date = group.Key,
            Runs = group.LongCount(),
            Total = group.Sum(row => (long)row.TotalRecords),
            Full = group.Sum(row => (long)row.FullMatchRecords),
            Partial = group.Sum(row => (long)row.PartialMatchRecords),
            Exceptions = group.Sum(row => (long)row.ExceptionRecords),
        }));

        var profileQuery = await _profiles.GetQueryableAsync();
        var ranked = source.GroupBy(row => row.ProfileId).Select(group => new
        {
            ProfileId = group.Key,
            Runs = group.LongCount(),
            Total = group.Sum(row => (long)row.TotalRecords),
            Full = group.Sum(row => (long)row.FullMatchRecords),
            Partial = group.Sum(row => (long)row.PartialMatchRecords),
            Exceptions = group.Sum(row => (long)row.ExceptionRecords),
        }).Where(row => row.Total > 0);
        var profiles = await AsyncExecuter.ToListAsync(
            (from row in ranked
             join profile in profileQuery on row.ProfileId equals profile.Id
             orderby (decimal)(row.Full + row.Partial) / row.Total, row.Total descending, row.ProfileId
             select new DashboardProfileDto
             {
                 ProfileId = row.ProfileId,
                 ProfileName = profile.Name,
                 CompletedRuns = row.Runs,
                 TotalRecords = row.Total,
                 FullMatchRecords = row.Full,
                 PartialMatchRecords = row.Partial,
                 ExceptionRecords = row.Exceptions,
                 MatchRate = (decimal)(row.Full + row.Partial) / row.Total * 100m,
             }).Take(5));

        var days = daily.ToDictionary(row => row.Date);
        var result = new DashboardDto
        {
            FromDate = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ThroughDate = through.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            GeneratedAtUtc = now,
            TimeZone = Clock.Kind == DateTimeKind.Utc ? "UTC" : TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var zone) ? zone : TimeZoneInfo.Local.Id,
            LowestMatchProfiles = profiles,
        };
        for (var date = from; date < through; date = date.AddDays(1))
        {
            days.TryGetValue(date, out var counts);
            result.Days.Add(new DashboardDayDto
            {
                Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                CompletedRuns = counts?.Runs ?? 0,
                TotalRecords = counts?.Total ?? 0,
                FullMatchRecords = counts?.Full ?? 0,
                PartialMatchRecords = counts?.Partial ?? 0,
                ExceptionRecords = counts?.Exceptions ?? 0,
            });
        }
        result.CompletedRuns = result.Days.Sum(day => day.CompletedRuns);
        result.TotalRecords = result.Days.Sum(day => day.TotalRecords);
        result.FullMatchRecords = result.Days.Sum(day => day.FullMatchRecords);
        result.PartialMatchRecords = result.Days.Sum(day => day.PartialMatchRecords);
        result.ExceptionRecords = result.Days.Sum(day => day.ExceptionRecords);
        result.MatchRate = result.TotalRecords == 0 ? null : (decimal)(result.FullMatchRecords + result.PartialMatchRecords) / result.TotalRecords * 100m;
        return result;
    }
}

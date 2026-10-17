using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Grid.Reconciliation;

public interface IDashboardAppService : IApplicationService
{
    Task<DashboardDto> GetAsync(DashboardInput input);
}

public class DashboardInput
{
    [Range(1, 90)]
    public int Days { get; set; } = 30;
}

public class DashboardDto
{
    public string FromDate { get; set; } = "";
    public string ThroughDate { get; set; } = "";
    public string TimeZone { get; set; } = "UTC";
    public DateTime GeneratedAtUtc { get; set; }
    public long CompletedRuns { get; set; }
    public long TotalRecords { get; set; }
    public long FullMatchRecords { get; set; }
    public long PartialMatchRecords { get; set; }
    public long ExceptionRecords { get; set; }
    public decimal? MatchRate { get; set; }
    public List<DashboardDayDto> Days { get; set; } = [];
    public List<DashboardProfileDto> LowestMatchProfiles { get; set; } = [];
}

public class DashboardDayDto
{
    public string Date { get; set; } = "";
    public long CompletedRuns { get; set; }
    public long TotalRecords { get; set; }
    public long FullMatchRecords { get; set; }
    public long PartialMatchRecords { get; set; }
    public long ExceptionRecords { get; set; }
}

public class DashboardProfileDto
{
    public Guid ProfileId { get; set; }
    public string ProfileName { get; set; } = "";
    public long CompletedRuns { get; set; }
    public long TotalRecords { get; set; }
    public long FullMatchRecords { get; set; }
    public long PartialMatchRecords { get; set; }
    public long ExceptionRecords { get; set; }
    public decimal MatchRate { get; set; }
}

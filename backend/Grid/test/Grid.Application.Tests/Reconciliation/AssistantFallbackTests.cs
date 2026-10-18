using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grid.Reconciliation;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Grid;

public class AssistantFallbackTests
{
    private readonly IConfiguration _configuration = Substitute.For<IConfiguration>();
    private readonly ReconciliationData _data;
    private readonly IRepository<ReconciliationRun, Guid> _runs = Substitute.For<IRepository<ReconciliationRun, Guid>>();

    public AssistantFallbackTests()
    {
        var legacyDatasets = Substitute.For<IRepository<Dataset, Guid>>();
        var legacyRows = Substitute.For<IRepository<DatasetRow, Guid>>();
        var baselines = Substitute.For<IRepository<RegressionBaseline, Guid>>();
        var datasets = Substitute.For<IRepository<Grid.Datasets.CanonicalDataset, Guid>>();
        var parserRuns = Substitute.For<IRepository<Grid.Parsers.ParserRun, Guid>>();
        var output = Substitute.For<ICanonicalOutputReader>();
        var canonical = new CanonicalDataSource(datasets, parserRuns, output);
        _data = new ReconciliationData(legacyDatasets, legacyRows, baselines, canonical);
    }

    private AssistantAppService CreateService() => new(_configuration, _data, _runs);

    [Fact]
    public async Task DecideFallbackAsync_Bypasses_Empty_Or_ZeroByte_Files()
    {
        var service = CreateService();
        var request = new AiFallbackRequestDto
        {
            StepIndex = 1,
            StepName = "Inspect Samples",
            ErrorMessage = "PBDO000042950001081326DB1.txt needs a header and at least one data row",
            AnomalousFiles =
            [
                new FileAnomalyDto
                {
                    FileId = "f-empty-1",
                    FileName = "PBDO000042950001081326DB1.txt",
                    DatasetCode = "BANK",
                    ParserCode = "BANK_PBDO000042950001081326DB1",
                    FileSize = 0,
                    RawSnippet = "",
                    ErrorDetail = "needs a header and at least one data row"
                }
            ]
        };

        var decision = await service.DecideFallbackAsync(request);

        Assert.True(decision.CanRecover);
        Assert.Equal("skip_empty_files", decision.ActionTaken);
        Assert.Contains("PBDO000042950001081326DB1.txt", decision.FilesToSkip);
        Assert.Contains("bypassed", decision.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DecideFallbackAsync_Recovers_Headerless_Clearing_Files()
    {
        var service = CreateService();
        var rawSnippet = "08122026,08132026,08122026,08122026,120512,1,0006,0001,0002,1,544830******0002,DB,000000000000,00000000429500,000000,000000,00000000429500,1\r\n" +
                         "08122026,08132026,08122026,08122026,120513,1,0006,0001,0002,1,544830******0003,DB,000000000000,00000000150000,000000,000000,00000000150000,1";

        var request = new AiFallbackRequestDto
        {
            StepIndex = 1,
            StepName = "Inspect Samples",
            ErrorMessage = "File has no header row",
            AnomalousFiles =
            [
                new FileAnomalyDto
                {
                    FileId = "f-bdo-1",
                    FileName = "PBDO000000010002081326DB1.txt",
                    DatasetCode = "BANK",
                    ParserCode = "BANK_PBDO000000010002081326DB1",
                    FileSize = 24000,
                    RawSnippet = rawSnippet,
                    ErrorDetail = "No headers detected"
                }
            ]
        };

        var decision = await service.DecideFallbackAsync(request);

        Assert.True(decision.CanRecover);
        Assert.NotEmpty(decision.AdjustedParsers);
        var parser = decision.AdjustedParsers[0];
        Assert.Equal("BANK_PBDO000000010002081326DB1", parser.Code);
        Assert.Equal(",", parser.Delimiter);
        Assert.Equal("none", parser.HeaderMode);
        Assert.Equal(0, parser.SkipRows);
    }

    [Fact]
    public async Task DecideFallbackAsync_Handles_Mixed_Empty_And_Headerless_Files()
    {
        var service = CreateService();
        var rawSnippet = "08122026,08132026,08122026,08122026,120512,1,0006,0001,0002,1,544830******0002,DB,000000000000,00000000429500,000000,000000,00000000429500,1";

        var request = new AiFallbackRequestDto
        {
            StepIndex = 1,
            StepName = "Inspect Samples",
            ErrorMessage = "Mixed data errors",
            AnomalousFiles =
            [
                new FileAnomalyDto
                {
                    FileId = "f-empty-1",
                    FileName = "EmptyFile.txt",
                    DatasetCode = "BANK",
                    ParserCode = "BANK_EMPTY",
                    FileSize = 0,
                    RawSnippet = ""
                },
                new FileAnomalyDto
                {
                    FileId = "f-data-1",
                    FileName = "DataFile.txt",
                    DatasetCode = "BANK",
                    ParserCode = "BANK_DATA",
                    FileSize = 500,
                    RawSnippet = rawSnippet
                }
            ]
        };

        var decision = await service.DecideFallbackAsync(request);

        Assert.True(decision.CanRecover);
        Assert.Contains("EmptyFile.txt", decision.FilesToSkip);
        Assert.Contains(decision.AdjustedParsers, p => p.Code == "BANK_DATA" && p.HeaderMode == "none");
    }

    [Fact]
    public async Task DecideFallbackAsync_Does_Not_Skip_Files_On_Network_Timeout()
    {
        var service = CreateService();
        var request = new AiFallbackRequestDto
        {
            StepIndex = 2,
            StepName = "AI Rule Drafting",
            ErrorMessage = "The assistant could not be reached: The request was canceled due to the configured HttpClient.Timeout of 90 seconds elapsing.",
            AnomalousFiles =
            [
                new FileAnomalyDto
                {
                    FileId = "f-pos-1",
                    FileName = "POS_file.csv",
                    DatasetCode = "POS",
                    ParserCode = "POS_PARSER",
                    FileSize = 10000,
                    RawSnippet = ""
                },
                new FileAnomalyDto
                {
                    FileId = "f-bank-1",
                    FileName = "BANK_file.csv",
                    DatasetCode = "BANK",
                    ParserCode = "BANK_PARSER",
                    FileSize = 20000,
                    RawSnippet = ""
                }
            ]
        };

        var decision = await service.DecideFallbackAsync(request);

        Assert.True(decision.CanRecover);
        Assert.Equal("switch_to_fast_heuristic", decision.ActionTaken);
        Assert.Empty(decision.FilesToSkip); // MUST NOT skip any files!
        Assert.Contains("heuristic", decision.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProposeAsync_Falls_Back_To_Heuristic_When_AI_Unavailable()
    {
        var service = CreateService();
        var input = new ProposeProfileDto
        {
            DatasetCodes = ["POS", "GATEWAY"],
            CsvSamples = new Dictionary<string, string>
            {
                ["POS"] = "order_id,gross_amount,sale_date,card_pan\r\nORD-101,150.00,2026-08-12,544830******0002\r\nORD-102,250.00,2026-08-12,544830******0003",
                ["GATEWAY"] = "ext_ref,charge_amount,settlement_date,card_pan\r\nORD-101,150.00,2026-08-12,544830******0002\r\nORD-102,250.00,2026-08-12,544830******0003"
            }
        };

        var proposal = await service.ProposeAsync(input);

        Assert.NotNull(proposal);
        Assert.NotEmpty(proposal.DefinitionJson);

        var profile = ProfileJson.Parse(proposal.DefinitionJson);
        Assert.Equal(2, profile.Datasets.Count);
        Assert.Single(profile.Rules); // Extra passes require observed differences.
        Assert.Contains(profile.Rules, r => r.Id == "EXACT_01_FULL");
    }

    [Fact]
    public async Task DecideFallbackAsync_Does_Not_Skip_NonEmpty_Files_Even_With_Empty_Snippet()
    {
        var service = CreateService();
        var request = new AiFallbackRequestDto
        {
            StepIndex = 1,
            StepName = "Inspect Samples",
            ErrorMessage = "General parse issue",
            AnomalousFiles =
            [
                new FileAnomalyDto
                {
                    FileId = "f-real-1",
                    FileName = "ValidDataFile.csv",
                    DatasetCode = "POS",
                    ParserCode = "POS_PARSER",
                    FileSize = 5000,
                    RawSnippet = ""
                }
            ]
        };

        var decision = await service.DecideFallbackAsync(request);

        Assert.True(decision.CanRecover);
        Assert.Empty(decision.FilesToSkip);
        Assert.Contains(decision.AdjustedParsers, p => p.Code == "POS_PARSER");
    }

    [Fact]
    public async Task ProposeParsersAsync_Infers_Headerless_Dates_Cards_And_Centavos()
    {
        var service = CreateService();
        var snippet = "08122026,08132026,08122026,0001,0005125710000008999,0001288215,0001258586,985221,000812260560,0001,0002,1,0006,0001,0000029629\r\n" +
                      "08122026,08132026,08122026,0001,0005125710000008999,0000119840,0000117084,985222,000812260561,0001,0002,1,0006,0001,0000002756\r\n";

        var input = new ProposeParsersDto
        {
            Files =
            [
                new FileSnippetDto { Code = "GATEWAY_PBDO", FileName = "PBDO.txt", RawSnippet = snippet },
                new FileSnippetDto { Code = "GATEWAY_PBDO2", FileName = "PBDO2.txt", RawSnippet = snippet },
                new FileSnippetDto { Code = "GATEWAY_PBDO3", FileName = "PBDO3.txt", RawSnippet = snippet },
                new FileSnippetDto { Code = "GATEWAY_PBDO4", FileName = "PBDO4.txt", RawSnippet = snippet },
            ]
        };

        var result = await service.ProposeParsersAsync(input);

        Assert.NotEmpty(result.Parsers);
        var parser = result.Parsers[0];
        Assert.Equal("none", parser.HeaderMode);
        Assert.Contains(parser.Steps, s => s.Kind == "parseDate" && s.Source == "column_1" && s.Params["format"] == "%m%d%Y");
        Assert.Contains(parser.Steps, s => s.Kind == "scaleCents" && s.Source == "column_6");
        Assert.Contains(parser.Steps, s => s.Kind == "maskCard" && s.Source == "column_5");
    }

    [Fact]
    public async Task Company_code_is_never_inferred_as_a_card_by_substring()
    {
        var result = await CreateService().ProposeParsersAsync(new ProposeParsersDto
        {
            Files = [
                new() { Code = "A", RawSnippet = "company_code,card_number,amount\n101,5448301234560002,20.00" },
                new() { Code = "B", RawSnippet = "company_code,card_number,amount\n101,5448301234560002,20.00" },
                new() { Code = "C", RawSnippet = "company_code,card_number,amount\n101,5448301234560002,20.00" },
                new() { Code = "D", RawSnippet = "company_code,card_number,amount\n101,5448301234560002,20.00" },
            ],
        });
        Assert.Equal("heuristic", result.Source);
        Assert.All(result.Parsers, parser =>
        {
            Assert.DoesNotContain(parser.Steps, step => step.Kind == "maskCard" && step.Source == "company_code");
            Assert.Contains(parser.Steps, step => step.Kind == "maskCard" && step.Source == "card_number");
        });
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Grid.Datasets;
using Grid.Parsers;
using Grid.Reconciliation;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Grid;

public class CanonicalDataSourceTests
{
    private readonly IRepository<CanonicalDataset, Guid> _datasets = Substitute.For<IRepository<CanonicalDataset, Guid>>();
    private readonly IRepository<ParserRun, Guid> _runs = Substitute.For<IRepository<ParserRun, Guid>>();
    private readonly ICanonicalOutputReader _output = Substitute.For<ICanonicalOutputReader>();
    private readonly CanonicalDataset _dataset = new() { Code = "POS", Name = "Point of sale", SchemaColumnsJson = """[{"name":"receipt_id","polarsType":"pl.String"},{"name":"amount","polarsType":"pl.Decimal(18,2)"}]""" };

    private CanonicalDataSource Source() => new(_datasets, _runs, _output);

    private void ConfigureDataset()
    {
        _datasets.FindAsync(Arg.Any<Expression<Func<CanonicalDataset, bool>>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_dataset);
    }

    private ParserRun Run(string status = "Succeeded")
    {
        var run = new ParserRun(Guid.NewGuid()) { Status = status, OutputObject = "gs://bucket/runs/selected/data.parquet" };
        run.ExtraProperties["DatasetId"] = _dataset.Id.ToString();
        _runs.GetAsync(run.Id).Returns(run);
        return run;
    }


    [Fact]
    public async Task Selected_successful_parser_output_is_used_instead_of_latest_upload()
    {
        ConfigureDataset();
        var selected = Run();
        var canonicalRows = ReconciliationEngine.ReadCsv("receipt_id,amount\n001,90071992547409.93\n");
        _output.ReadAsync(selected.OutputObject + ".csv").Returns(canonicalRows);
        var rows = await Source().LoadAsync("POS", selected.Id, requireExplicitRun: true);
        Assert.Equal("001", rows![0]["receipt_id"]);
        Assert.Equal("90071992547409.93", rows[0]["amount"]);
        await _output.Received(1).ReadAsync(selected.OutputObject + ".csv");
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Failed")]
    public async Task Unfinished_or_failed_parser_output_is_rejected(string status)
    {
        ConfigureDataset();
        var run = Run(status);
        await Assert.ThrowsAsync<UserFriendlyException>(() => Source().LoadAsync("POS", run.Id));
        await _output.DidNotReceiveWithAnyArgs().ReadAsync(default!);
    }

    [Fact]
    public async Task Output_from_a_different_dataset_is_rejected()
    {
        ConfigureDataset();
        var run = Run();
        run.ExtraProperties["DatasetId"] = Guid.NewGuid().ToString();
        await Assert.ThrowsAsync<UserFriendlyException>(() => Source().LoadAsync("POS", run.Id));
        await _output.DidNotReceiveWithAnyArgs().ReadAsync(default!);
    }

    [Fact]
    public async Task Run_requires_explicit_file_selection()
    {
        ConfigureDataset();
        await Assert.ThrowsAsync<UserFriendlyException>(() => Source().LoadAsync("POS", null, requireExplicitRun: true));
    }

    [Fact]
    public async Task Schema_can_be_loaded_before_uploading_data()
    {
        ConfigureDataset();
        var schema = await Source().GetSchemaAsync("POS");
        Assert.Equal(new[] { "receipt_id", "amount" }, schema!.Keys);
        await _output.DidNotReceiveWithAnyArgs().ReadAsync(default!);
    }

    [Fact]
    public async Task Changed_output_schema_is_rejected()
    {
        ConfigureDataset();
        var run = Run();
        _output.ReadAsync(run.OutputObject + ".csv").Returns(ReconciliationEngine.ReadCsv("old_id,amount\n001,10\n"));
        await Assert.ThrowsAsync<UserFriendlyException>(() => Source().LoadAsync("POS", run.Id));
    }

    [Fact]
    public async Task Changed_contract_type_is_rejected_before_reading_output()
    {
        ConfigureDataset();
        var run = Run();
        run.ExtraProperties["SchemaColumnsJson"] = """[{"name":"receipt_id","polarsType":"pl.Int64"},{"name":"amount","polarsType":"pl.Decimal(18,2)"}]""";
        await Assert.ThrowsAsync<UserFriendlyException>(() => Source().LoadAsync("POS", run.Id));
        await _output.DidNotReceiveWithAnyArgs().ReadAsync(default!);
    }

    [Fact]
    public async Task Empty_output_is_rejected()
    {
        ConfigureDataset();
        var run = Run();
        _output.ReadAsync(run.OutputObject + ".csv").Returns(new List<Dictionary<string, string>>());
        await Assert.ThrowsAsync<UserFriendlyException>(() => Source().LoadAsync("POS", run.Id));
    }

    [Fact]
    public async Task Missing_sidecar_propagates_exception()
    {
        ConfigureDataset();
        var run = Run();
        _output.ReadAsync(run.OutputObject + ".csv").Returns(Task.FromException<List<Dictionary<string, string>>>(new UserFriendlyException("Canonical parser output is missing. Reprocess the source file.")));
        var ex = await Assert.ThrowsAsync<UserFriendlyException>(() => Source().LoadAsync("POS", run.Id));
        Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

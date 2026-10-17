using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Grid.Parsers;
using Grid.Reconciliation;
using NSubstitute;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Grid;

public class DeletionAppServiceTests
{
    [Fact]
    public async Task Package_delete_cascades_runs_records_groups_baselines_and_profile()
    {
        var packages = Substitute.For<IRepository<Package, Guid>>();
        var profiles = Substitute.For<IRepository<Profile, Guid>>();
        var baselines = Substitute.For<IRepository<RegressionBaseline, Guid>>();
        var runs = Substitute.For<IRepository<ReconciliationRun, Guid>>();
        var groups = Substitute.For<IRepository<MatchGroup, Guid>>();
        var records = Substitute.For<IRepository<RunRecord, Guid>>();

        var packageId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        profiles.FirstOrDefaultAsync(Arg.Any<Expression<Func<Profile, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(new Profile(profileId) { PackageId = packageId, Name = "Test Profile" });

        runs.GetListAsync(Arg.Any<Expression<Func<ReconciliationRun, bool>>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<ReconciliationRun>
            {
                new(runId) { PackageId = packageId, ProfileId = profileId }
            });

        var packageAppService = new PackageAppService(packages, profiles, baselines, runs, groups, records);
        await packageAppService.DeleteAsync(packageId);

        await records.Received(1).DeleteAsync(Arg.Any<Expression<Func<RunRecord, bool>>>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
        await groups.Received(1).DeleteAsync(Arg.Any<Expression<Func<MatchGroup, bool>>>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
        await runs.Received(1).DeleteAsync(Arg.Any<Expression<Func<ReconciliationRun, bool>>>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
        await baselines.Received(1).DeleteAsync(Arg.Any<Expression<Func<RegressionBaseline, bool>>>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
        await profiles.Received(1).DeleteAsync(Arg.Any<Expression<Func<Profile, bool>>>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
        await packages.Received(1).DeleteAsync(Arg.Any<Expression<Func<Package, bool>>>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Profile_delete_finds_package_and_cascades_all_entities()
    {
        var packages = Substitute.For<IRepository<Package, Guid>>();
        var profiles = Substitute.For<IRepository<Profile, Guid>>();
        var baselines = Substitute.For<IRepository<RegressionBaseline, Guid>>();
        var runs = Substitute.For<IRepository<ReconciliationRun, Guid>>();
        var groups = Substitute.For<IRepository<MatchGroup, Guid>>();
        var records = Substitute.For<IRepository<RunRecord, Guid>>();
        var data = Substitute.For<ReconciliationData>(
            Substitute.For<IRepository<Dataset, Guid>>(),
            Substitute.For<IRepository<DatasetRow, Guid>>(),
            Substitute.For<IRepository<RegressionBaseline, Guid>>(),
            Substitute.For<CanonicalDataSource>(
                Substitute.For<IRepository<Datasets.CanonicalDataset, Guid>>(),
                Substitute.For<IRepository<ParserRun, Guid>>(),
                Substitute.For<ICanonicalOutputReader>())
        );

        var packageId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        profiles.FindAsync(profileId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Profile(profileId) { PackageId = packageId, Name = "Test Profile" });

        runs.GetListAsync(Arg.Any<Expression<Func<ReconciliationRun, bool>>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<ReconciliationRun>());

        var profileAppService = new ProfileAppService(profiles, packages, baselines, runs, groups, records, data);
        await profileAppService.DeleteAsync(profileId);

        await baselines.Received(1).DeleteAsync(Arg.Any<Expression<Func<RegressionBaseline, bool>>>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
        await profiles.Received(1).DeleteAsync(Arg.Any<Expression<Func<Profile, bool>>>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
        await packages.Received(1).DeleteAsync(Arg.Any<Expression<Func<Package, bool>>>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Parser_delete_cascades_parser_runs_and_parser_configuration()
    {
        var parsers = Substitute.For<IRepository<ParserConfiguration, Guid>>();
        var runs = Substitute.For<IRepository<ParserRun, Guid>>();
        var parserId = Guid.NewGuid();

        var parserAppService = new ParserAppService(parsers, runs);
        await parserAppService.DeleteAsync(parserId);

        await runs.Received(1).DeleteAsync(Arg.Any<Expression<Func<ParserRun, bool>>>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
        await parsers.Received(1).DeleteAsync(parserId, autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
    }
}

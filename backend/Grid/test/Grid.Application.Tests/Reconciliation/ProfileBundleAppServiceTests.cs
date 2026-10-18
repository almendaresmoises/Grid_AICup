using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Grid.Datasets;
using Grid.Parsers;
using Grid.Reconciliation;
using NSubstitute;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Grid;

public class ProfileBundleAppServiceTests
{
    [Fact]
    public async Task GetBundleAsync_bundles_profile_referenced_datasets_and_parsers()
    {
        var profiles = Substitute.For<IRepository<Profile, Guid>>();
        var packages = Substitute.For<IRepository<Package, Guid>>();
        var baselines = Substitute.For<IRepository<RegressionBaseline, Guid>>();
        var runs = Substitute.For<IRepository<ReconciliationRun, Guid>>();
        var groups = Substitute.For<IRepository<MatchGroup, Guid>>();
        var records = Substitute.For<IRepository<RunRecord, Guid>>();
        var datasets = Substitute.For<IRepository<CanonicalDataset, Guid>>();
        var parsers = Substitute.For<IRepository<ParserConfiguration, Guid>>();
        var data = Substitute.For<ReconciliationData>(
            Substitute.For<IRepository<Dataset, Guid>>(),
            Substitute.For<IRepository<DatasetRow, Guid>>(),
            Substitute.For<IRepository<RegressionBaseline, Guid>>(),
            Substitute.For<CanonicalDataSource>(
                Substitute.For<IRepository<CanonicalDataset, Guid>>(),
                Substitute.For<IRepository<ParserRun, Guid>>(),
                Substitute.For<ICanonicalOutputReader>())
        );

        var profileId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        var definitionJson = """
        {
          "name": "SAP vs Bank Profile",
          "version": 1,
          "datasets": ["SAP_GL", "BANK_STMT"],
          "rules": [
            {
              "id": "RULE1",
              "type": "exact",
              "datasets": ["SAP_GL", "BANK_STMT"],
              "keys": []
            }
          ]
        }
        """;

        var profile = new Profile(profileId)
        {
            PackageId = packageId,
            Name = "SAP vs Bank Profile",
            Version = 1,
            Status = ProfileStatus.Approved,
            DefinitionJson = definitionJson,
            ApprovedDefinitionJson = definitionJson
        };

        var package = new Package(packageId)
        {
            Name = "SAP vs Bank Package",
            Description = "Demo reconciliation package"
        };

        profiles.GetAsync(profileId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(profile);
        packages.GetAsync(packageId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(package);

        datasets.GetListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<CanonicalDataset>
            {
                new() { Code = "SAP_GL", Name = "SAP General Ledger", GcsPrefix = "canonical/sap_gl", SchemaColumnsJson = "[{\"name\":\"doc_no\"}]" },
                new() { Code = "BANK_STMT", Name = "Bank Statement", GcsPrefix = "canonical/bank_stmt", SchemaColumnsJson = "[{\"name\":\"stmt_id\"}]" },
                new() { Code = "OTHER_DS", Name = "Unrelated", GcsPrefix = "canonical/other", SchemaColumnsJson = "[]" }
            });

        parsers.GetListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<ParserConfiguration>
            {
                new() { Name = "SAP GL CSV Parser", FileFormat = "csv", OutputGcsPrefix = "canonical/sap_gl", PipelineJson = "{}" },
                new() { Name = "Bank Statement MT940 Parser", FileFormat = "mt940", OutputGcsPrefix = "canonical/bank_stmt/", PipelineJson = "{}" },
                new() { Name = "Other Parser", FileFormat = "csv", OutputGcsPrefix = "canonical/other", PipelineJson = "{}" }
            });

        var appService = new ProfileAppService(profiles, packages, baselines, runs, groups, records, data, datasets, parsers);

        var bundle = await appService.GetBundleAsync(profileId);

        Assert.NotNull(bundle);
        Assert.Equal(1, bundle.FormatVersion);
        Assert.Equal("SAP vs Bank Package", bundle.Package?.Name);
        Assert.Equal("SAP vs Bank Profile", bundle.Profile.Name);
        Assert.Equal(2, bundle.Datasets.Count);
        Assert.Contains(bundle.Datasets, d => d.Code == "SAP_GL");
        Assert.Contains(bundle.Datasets, d => d.Code == "BANK_STMT");
        Assert.DoesNotContain(bundle.Datasets, d => d.Code == "OTHER_DS");

        Assert.Equal(2, bundle.Parsers.Count);
        Assert.Contains(bundle.Parsers, p => p.Name == "SAP GL CSV Parser");
        Assert.Contains(bundle.Parsers, p => p.Name == "Bank Statement MT940 Parser");
        Assert.DoesNotContain(bundle.Parsers, p => p.Name == "Other Parser");
    }

    [Fact]
    public async Task ImportBundleAsync_creates_datasets_parsers_package_and_profile()
    {
        var profiles = Substitute.For<IRepository<Profile, Guid>>();
        var packages = Substitute.For<IRepository<Package, Guid>>();
        var baselines = Substitute.For<IRepository<RegressionBaseline, Guid>>();
        var runs = Substitute.For<IRepository<ReconciliationRun, Guid>>();
        var groups = Substitute.For<IRepository<MatchGroup, Guid>>();
        var records = Substitute.For<IRepository<RunRecord, Guid>>();
        var datasets = Substitute.For<IRepository<CanonicalDataset, Guid>>();
        var parsers = Substitute.For<IRepository<ParserConfiguration, Guid>>();
        var data = Substitute.For<ReconciliationData>(
            Substitute.For<IRepository<Dataset, Guid>>(),
            Substitute.For<IRepository<DatasetRow, Guid>>(),
            Substitute.For<IRepository<RegressionBaseline, Guid>>(),
            Substitute.For<CanonicalDataSource>(
                Substitute.For<IRepository<CanonicalDataset, Guid>>(),
                Substitute.For<IRepository<ParserRun, Guid>>(),
                Substitute.For<ICanonicalOutputReader>())
        );

        datasets.GetListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<CanonicalDataset>());
        parsers.GetListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<ParserConfiguration>());

        var bundle = new ProfileBundleDto
        {
            FormatVersion = 1,
            Package = new BundlePackageDto
            {
                Name = "Imported Package",
                Description = "Package imported from JSON bundle"
            },
            Profile = new BundleProfileDto
            {
                Name = "Imported Profile",
                Status = "Approved",
                Version = 1,
                DefinitionJson = """
                {
                  "name": "Imported Profile",
                  "version": 1,
                  "datasets": ["DEMO_A", "DEMO_B"],
                  "rules": []
                }
                """
            },
            Datasets = new List<BundleDatasetDto>
            {
                new() { Code = "DEMO_A", Name = "Demo A", GcsPrefix = "canonical/demo_a", SchemaColumnsJson = "[{\"name\":\"id\"}]" },
                new() { Code = "DEMO_B", Name = "Demo B", GcsPrefix = "canonical/demo_b", SchemaColumnsJson = "[{\"name\":\"id\"}]" }
            },
            Parsers = new List<BundleParserDto>
            {
                new() { Name = "Demo A Parser", FileFormat = "csv", OutputGcsPrefix = "canonical/demo_a", PipelineJson = "{}" }
            }
        };

        var appService = new ProfileAppService(profiles, packages, baselines, runs, groups, records, data, datasets, parsers);

        var result = await appService.ImportBundleAsync(bundle);

        Assert.NotNull(result);
        Assert.Equal("Imported Profile", result.Name);
        Assert.Equal("Imported Package", result.PackageName);
        Assert.Equal("Approved", result.Status);

        // Verify dataset inserts
        await datasets.Received(2).InsertAsync(Arg.Any<CanonicalDataset>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
        // Verify parser inserts and studio sync
        await parsers.Received(1).InsertAsync(Arg.Is<ParserConfiguration>(p =>
            p.Name == "Demo A Parser" &&
            p.OutputGcsPrefix == "canonical/demo_a" &&
            p.PipelineJson.Contains("__studio")), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
        // Verify package and profile inserts
        await packages.Received(1).InsertAsync(Arg.Any<Package>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
        await profiles.Received(1).InsertAsync(Arg.Any<Profile>(), autoSave: true, cancellationToken: Arg.Any<CancellationToken>());
    }
}

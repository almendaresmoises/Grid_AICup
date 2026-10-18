using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Grid.Reconciliation;

public interface IDatasetAppService : IApplicationService
{
    Task<List<DatasetDto>> GetListAsync();

    Task<DatasetDto> GetAsync(Guid id);

    Task<DatasetDto> CreateAsync(CreateDatasetDto input);

    Task<DatasetDto> UpdateAsync(Guid id, UpdateDatasetDto input);

    Task DeleteAsync(Guid id);
}

public interface IPackageAppService : IApplicationService
{
    Task<List<PackageDto>> GetListAsync();

    Task<PackageDto> GetAsync(Guid id);

    Task<PackageDto> CreateAsync(CreatePackageDto input);

    Task DeleteAsync(Guid id);
}

public interface IProfileAppService : IApplicationService
{
    Task<ProfileDto> GetAsync(Guid id);

    Task<ProfileDto> UpdateAsync(Guid id, SaveProfileDto input);

    Task<ProfileDto> ApproveAsync(Guid id, SaveProfileDto input);

    Task DeleteAsync(Guid id);

    Task<ProfileBundleDto> GetBundleAsync(Guid id);

    Task<ProfileDto> ImportBundleAsync(ProfileBundleDto input);
}

public interface IRunAppService : IApplicationService
{
    Task<List<RunDto>> GetListAsync(RunListInput input);

    Task<RunDetailDto> GetAsync(Guid id);

    Task<RunDetailDto> CreateAsync(CreateRunDto input);
}

public interface IAssistantAppService : IApplicationService
{
    Task<ProposalDto> ProposeAsync(ProposeProfileDto input);

    Task<ParsersProposalDto> ProposeParsersAsync(ProposeParsersDto input);

    Task<InvestigationDto> InvestigateAsync(InvestigateInput input);

    Task<RegressionPreviewDto> PreviewRegressionAsync(RegressionPreviewInput input);

    Task<bool> GetConnectionTestAsync();
 
     Task<AiFallbackDecisionDto> DecideFallbackAsync(AiFallbackRequestDto input);
 }

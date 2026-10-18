using Volo.Abp.DependencyInjection;

namespace Grid.Reconciliation;

public interface ICanonicalProfileDraftGenerator
{
    Task<ProposalDto> DraftAsync(List<string> codes, Dictionary<string, List<Dictionary<string, string>>> rows, string context);
}

public class CanonicalProfileDraftGenerator : ICanonicalProfileDraftGenerator, ITransientDependency
{
    private readonly AssistantAppService _assistant;
    public CanonicalProfileDraftGenerator(AssistantAppService assistant) { _assistant = assistant; }
    public Task<ProposalDto> DraftAsync(List<string> codes, Dictionary<string, List<Dictionary<string, string>>> rows, string context)
        => _assistant.DraftFromRowsAsync(codes, rows, context);
}

namespace Orchestration.Application.Agents.Legal;

public sealed class LegalAgentOptions
{
    public const string SectionName = "LegalAgent";

    public bool AiReviewEnabled { get; init; } = false;
}

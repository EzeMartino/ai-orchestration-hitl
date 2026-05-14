namespace Orchestration.Infrastructure.Agents.Planner.Reasoning;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public bool Enabled { get; init; }

    public string Provider { get; init; } = "OpenAI";

    public string Model { get; init; } = "";

    public string ApiKey { get; init; } = "";

    public string ServiceId { get; init; } = "planner-reasoning";
}

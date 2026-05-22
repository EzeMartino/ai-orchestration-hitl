namespace Orchestration.Infrastructure.Agents.Legal.Regulations;

public sealed class CnvRegulationMcpOptions
{
    public const string SectionName = "Mcp:CnvRegulation";

    public bool Enabled { get; init; }

    public string Command { get; init; } = "dotnet";

    public string[] Args { get; init; } = [];

    public int DefaultLimit { get; init; } = 5;

    public int ConnectionTimeoutSeconds { get; init; } = 15;

    public int ToolCallTimeoutSeconds { get; init; } = 30;
}

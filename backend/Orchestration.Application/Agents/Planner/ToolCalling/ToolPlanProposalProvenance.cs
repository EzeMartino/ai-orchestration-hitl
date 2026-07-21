using System.Text.Json.Serialization;

namespace Orchestration.Application.Agents.Planner.ToolCalling;

[JsonConverter(typeof(JsonStringEnumConverter<ToolPlanProposalSource>))]
public enum ToolPlanProposalSource
{
    [JsonStringEnumMemberName("llm")]
    Llm,

    [JsonStringEnumMemberName("deterministic")]
    Deterministic,

    [JsonStringEnumMemberName("deterministic_fallback")]
    DeterministicFallback
}

[JsonConverter(typeof(JsonStringEnumConverter<ToolPlanProposalFallbackReason>))]
public enum ToolPlanProposalFallbackReason
{
    [JsonStringEnumMemberName("llm_response_invalid")]
    LlmResponseInvalid,

    [JsonStringEnumMemberName("llm_request_failed")]
    LlmRequestFailed,

    [JsonStringEnumMemberName("llm_configuration_failed")]
    LlmConfigurationFailed
}

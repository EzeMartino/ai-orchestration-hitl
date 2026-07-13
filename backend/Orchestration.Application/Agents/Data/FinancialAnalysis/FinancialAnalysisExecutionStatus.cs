using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

[JsonConverter(typeof(FinancialAnalysisExecutionStatusJsonConverter))]
public enum FinancialAnalysisExecutionStatus
{
    LegacyUnknown = 0,
    Succeeded,
    Degraded,
    Failed
}

public sealed class FinancialAnalysisExecutionStatusJsonConverter
    : JsonConverter<FinancialAnalysisExecutionStatus>
{
    public override FinancialAnalysisExecutionStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() switch
            {
                "succeeded" => FinancialAnalysisExecutionStatus.Succeeded,
                "degraded" => FinancialAnalysisExecutionStatus.Degraded,
                "failed" => FinancialAnalysisExecutionStatus.Failed,
                _ => FinancialAnalysisExecutionStatus.LegacyUnknown
            };
        }

        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            using var ignored = JsonDocument.ParseValue(ref reader);
        }

        return FinancialAnalysisExecutionStatus.LegacyUnknown;
    }

    public override void Write(
        Utf8JsonWriter writer,
        FinancialAnalysisExecutionStatus value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            FinancialAnalysisExecutionStatus.Succeeded => "succeeded",
            FinancialAnalysisExecutionStatus.Degraded => "degraded",
            FinancialAnalysisExecutionStatus.Failed => "failed",
            _ => "legacy_unknown"
        });
    }
}

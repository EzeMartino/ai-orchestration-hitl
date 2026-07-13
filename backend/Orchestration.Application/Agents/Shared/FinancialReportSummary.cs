using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestration.Application.Agents.Shared;

public sealed record FinancialReportSummaryInput(
    string? ReportName,
    decimal? TotalAmount,
    int? TransactionCount,
    [property: JsonConverter(typeof(FinancialReportSubmittedAtJsonConverter))]
    DateTimeOffset? SubmittedAt);

public sealed record FinancialReportSummary(
    string ReportName,
    decimal TotalAmount,
    int TransactionCount,
    DateTimeOffset SubmittedAt);

public sealed class FinancialReportSubmittedAtJsonConverter
    : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();

            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var submittedAt)
                ? submittedAt
                : DateTimeOffset.MinValue;
        }

        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            using var ignored = JsonDocument.ParseValue(ref reader);
        }

        return DateTimeOffset.MinValue;
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString(
            "O",
            CultureInfo.InvariantCulture));
    }
}

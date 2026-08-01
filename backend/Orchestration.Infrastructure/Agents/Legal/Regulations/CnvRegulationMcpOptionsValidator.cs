using Microsoft.Extensions.Options;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations;

public sealed class CnvRegulationMcpOptionsValidator : IValidateOptions<CnvRegulationMcpOptions>
{
    public ValidateOptionsResult Validate(string? name, CnvRegulationMcpOptions options)
    {
        var failures = new List<string>();

        if (options.Required && !options.Enabled)
        {
            failures.Add("Required cannot be true when Enabled is false.");
        }

        if (options.Required && options.Enabled &&
            !HasExactlyOnePostgresStoragePair(options.Args))
        {
            failures.Add(
                "Required CNV MCP must configure exactly one '--storage postgres' argument pair.");
        }

        if (options.Enabled || options.Required)
        {
            if (string.IsNullOrWhiteSpace(options.Command))
            {
                failures.Add("Command must be configured when Enabled is true.");
            }

            if (options.Args is null || options.Args.Length == 0 ||
                options.Args.Any(string.IsNullOrWhiteSpace))
            {
                failures.Add("Args must contain at least one value when Enabled is true.");
            }

            if (options.ConnectionTimeoutSeconds <= 0)
            {
                failures.Add("ConnectionTimeoutSeconds must be greater than zero when Enabled is true.");
            }

            if (options.ToolCallTimeoutSeconds <= 0)
            {
                failures.Add("ToolCallTimeoutSeconds must be greater than zero when Enabled is true.");
            }
        }

        if (options.MaxEnrichedHits is < 0 or > 2)
        {
            failures.Add("MaxEnrichedHits must be between 0 and 2 (inclusive).");
        }

        if (options.MaxDocumentContextCharacters is < 0 or > 12_000)
        {
            failures.Add("MaxDocumentContextCharacters must be between 0 and 12000 (inclusive).");
        }

        if (options.MaxArticleContextCharacters is < 0 or > 6_000)
        {
            failures.Add("MaxArticleContextCharacters must be between 0 and 6000 (inclusive).");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool HasExactlyOnePostgresStoragePair(string[]? args)
    {
        if (args is null ||
            args.Any(argument =>
                string.Equals(argument, "--use-postgres", StringComparison.OrdinalIgnoreCase) ||
                argument.StartsWith("--storage=", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var storageOptionIndexes = args
            .Select((argument, index) => (argument, index))
            .Where(item =>
                string.Equals(item.argument, "--storage", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index)
            .ToArray();

        return storageOptionIndexes.Length == 1 &&
               storageOptionIndexes[0] + 1 < args.Length &&
               string.Equals(
                   args[storageOptionIndexes[0] + 1],
                   "postgres",
                   StringComparison.OrdinalIgnoreCase);
    }
}

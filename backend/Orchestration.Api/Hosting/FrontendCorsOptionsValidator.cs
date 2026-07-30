using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Orchestration.Api.Hosting;

public sealed class FrontendCorsOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<FrontendCorsOptions>
{
    private static readonly string[] DevelopmentFallbackOrigins =
    [
        "http://localhost:5173",
        "https://localhost:5173"
    ];

    public ValidateOptionsResult Validate(string? name, FrontendCorsOptions options)
    {
        return TryResolveAllowedOrigins(options, environment.IsDevelopment(), out _, out var failure)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failure!);
    }

    public static string[] ResolveAllowedOrigins(FrontendCorsOptions options, bool isDevelopment)
    {
        if (TryResolveAllowedOrigins(options, isDevelopment, out var origins, out var failure))
        {
            return origins;
        }

        throw new OptionsValidationException(
            Options.DefaultName,
            typeof(FrontendCorsOptions),
            [failure!]);
    }

    private static bool TryResolveAllowedOrigins(
        FrontendCorsOptions options,
        bool isDevelopment,
        out string[] origins,
        out string? failure)
    {
        var configuredOrigins = options.AllowedOrigins ?? [];
        if (configuredOrigins.Length == 0)
        {
            if (isDevelopment)
            {
                origins = DevelopmentFallbackOrigins;
                failure = null;
                return true;
            }

            origins = [];
            failure = "Cors:AllowedOrigins must contain at least one HTTPS origin outside Development.";
            return false;
        }

        var normalizedOrigins = new List<string>(configuredOrigins.Length);
        var seenOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuredOrigin in configuredOrigins)
        {
            if (!TryNormalizeOrigin(configuredOrigin, isDevelopment, out var normalizedOrigin, out failure))
            {
                origins = [];
                return false;
            }

            if (!seenOrigins.Add(normalizedOrigin!))
            {
                origins = [];
                failure = $"Cors:AllowedOrigins contains duplicate origin '{normalizedOrigin}'.";
                return false;
            }

            normalizedOrigins.Add(normalizedOrigin!);
        }

        origins = normalizedOrigins.ToArray();
        failure = null;
        return true;
    }

    private static bool TryNormalizeOrigin(
        string? configuredOrigin,
        bool isDevelopment,
        out string? normalizedOrigin,
        out string? failure)
    {
        normalizedOrigin = null;
        failure = "Cors:AllowedOrigins entries must be absolute origins without wildcard, path, query, fragment, or user info.";

        if (string.IsNullOrWhiteSpace(configuredOrigin) || configuredOrigin.Contains('*') ||
            !Uri.TryCreate(configuredOrigin, UriKind.Absolute, out var uri) ||
            (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !(isDevelopment && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
               uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        normalizedOrigin = uri.GetLeftPart(UriPartial.Authority);
        if (!string.Equals(
                normalizedOrigin,
                configuredOrigin.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase))
        {
            normalizedOrigin = null;
            return false;
        }

        return true;
    }
}

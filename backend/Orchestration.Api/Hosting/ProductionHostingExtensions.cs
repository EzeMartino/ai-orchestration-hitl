using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Orchestration.Api.Hosting;

public static class ProductionHostingExtensions
{
    public static IServiceCollection AddProductionHosting(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var corsOptions = configuration.GetSection(FrontendCorsOptions.SectionName)
            .Get<FrontendCorsOptions>() ?? new FrontendCorsOptions();
        var allowedOrigins = FrontendCorsOptionsValidator.ResolveAllowedOrigins(
            corsOptions,
            environment.IsDevelopment());
        var renderProxyOptions = configuration.GetSection(RenderProxyOptions.SectionName)
            .Get<RenderProxyOptions>() ?? new RenderProxyOptions();

        services.AddOptions<FrontendCorsOptions>()
            .Bind(configuration.GetSection(FrontendCorsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FrontendCorsOptions>>(
            new FrontendCorsOptionsValidator(environment));
        services.AddOptions<RenderProxyOptions>()
            .Bind(configuration.GetSection(RenderProxyOptions.SectionName))
            .ValidateOnStart();

        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy => policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });
        services.Configure<WebSocketOptions>(options =>
        {
            foreach (var origin in allowedOrigins)
            {
                options.AllowedOrigins.Add(origin);
            }
        });
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.None;
            if (!renderProxyOptions.Enabled)
            {
                return;
            }

            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto |
                ForwardedHeaders.XForwardedHost;
            // Render's managed proxy addresses change; only enable this when RenderProxy is explicit.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }

    public static WebApplication UseProductionHosting(this WebApplication app)
    {
        var renderProxyEnabled = app.Services.GetRequiredService<IOptions<RenderProxyOptions>>().Value.Enabled;
        if (renderProxyEnabled)
        {
            app.UseForwardedHeaders();
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseWebSockets();
        app.UseCors("Frontend");
        return app;
    }
}

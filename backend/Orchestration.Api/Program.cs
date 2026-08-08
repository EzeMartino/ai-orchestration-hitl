using CSnakes.Runtime;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Orchestration.Api.Hosting;
using Orchestration.Api.Hubs;
using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;
using Orchestration.Application.FinancialAnalysis.Thresholds;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Infrastructure.Agents.Legal.AiReview;

using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Planner.ToolCalling.Mapping;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Application.Persistence;
using Orchestration.Infrastructure.Agents.Data;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;
using Orchestration.Infrastructure.Agents.Legal;
using Orchestration.Infrastructure.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Persistence;


var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<AnalysisSessionStateMachine>();
builder.Services.AddScoped<IFinancialReportContextResolver, FinancialReportContextResolver>();
builder.Services.AddScoped<AnalysisSessionWorkflowService>();
builder.Services.AddSignalR();
builder.Services.AddScoped<IActivityEventPublisher, SignalRActivityEventPublisher>();
builder.Services.AddScoped<IAnalysisSessionStartPreflightValidator, AnalysisSessionStartPreflightValidator>();
builder.Services.AddScoped<AnalysisOrchestratorService>();

// Planner agent configuration
builder.Services.AddPlannerReasoning(builder.Configuration);
builder.Services.AddToolPlanProposal(builder.Configuration);
builder.Services.AddScoped<IToolPlanValidator>(provider =>
    new ToolPlanValidator(
        provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptionsSnapshot<ToolCallingOptions>>().Value
    )
);
builder.Services.AddScoped(provider =>
    provider.GetRequiredService<
        Microsoft.Extensions.Options.IOptionsSnapshot<ToolCallingOptions>>().Value
);
builder.Services.AddScoped<IToolPlanNormalizer, ToolPlanNormalizer>();
builder.Services.AddScoped<IToolExecutionPolicy, ToolExecutionPolicy>();
builder.Services.AddScoped<IToolCallingDiagnosticService, ToolCallingDiagnosticService>();
builder.Services.AddScoped<IControlledToolExecutor, ControlledToolExecutor>();
builder.Services.AddScoped<IToolExecutionResultMapper, ToolExecutionResultMapper>();
builder.Services.AddScoped<IPlannerAgent, PlannerAgent>();

// Data agent and plugins configuration
builder.Services.Configure<DataAgentOptions>(
    builder.Configuration.GetSection(DataAgentOptions.SectionName)
);
builder.Services.Configure<StructuredFinancialMetricsFileUploadOptions>(
    builder.Configuration.GetSection(StructuredFinancialMetricsFileUploadOptions.SectionName)
);
builder.Services.Configure<StructuredFinancialMetricsPdfExtractionOptions>(
    builder.Configuration.GetSection(StructuredFinancialMetricsPdfExtractionOptions.SectionName)
);
builder.Services.Configure<FinancialMetricsExtractionOptions>(
    builder.Configuration.GetSection(FinancialMetricsExtractionOptions.SectionName)
);
builder.Services.AddScoped<CSnakesDataAgent>();
builder.Services.AddSingleton<IFinancialRiskThresholdProfileProvider, InMemoryFinancialRiskThresholdProfileProvider>();
builder.Services.AddPythonFinancialAnalysis();
builder.Services.AddDataAgentAiReview(builder.Configuration);
builder.Services.AddScoped<SemanticKernelDataAgent>();
builder.Services.AddScoped<ILegacyDataAgent>(provider =>
    provider.GetRequiredService<SemanticKernelDataAgent>());
builder.Services.AddScoped<IStructuredFinancialMetricsValidator, StructuredFinancialMetricsValidator>();
builder.Services.AddScoped<IFinancialMetricInputMapper, FinancialMetricInputMapper>();
builder.Services.AddScoped<IStructuredFinancialMetricsCsvParser, StructuredFinancialMetricsCsvParser>();
builder.Services.AddScoped<IStructuredFinancialMetricsTextParser, StructuredFinancialMetricsTextParser>();
builder.Services.AddScoped<IPdfTextExtractor, PdfPigTextExtractor>();
builder.Services.AddScoped<IOcrTextExtractor, LocalOcrTextExtractor>();
builder.Services.AddScoped<IStructuredFinancialMetricsPdfExtractor, StructuredFinancialMetricsPdfExtractor>();
builder.Services.AddHostedService<LocalPdfTemporaryDirectorySweeper>();
builder.Services.AddScoped<
    IFinancialMetricsExtractionCompletenessEvaluator,
    FinancialMetricsExtractionCompletenessEvaluator>();
builder.Services.AddScoped<IFinancialMetricCandidateReconciler, FinancialMetricCandidateReconciler>();
builder.Services.AddScoped<IFinancialMetricsExtractionDraftService, FinancialMetricsExtractionDraftService>();
builder.Services.AddScoped<
    IStructuredFinancialMetricsPdfIngestionService,
    StructuredFinancialMetricsPdfIngestionService>();
builder.Services.AddFinancialDocumentExtraction(builder.Configuration);
builder.Services.AddScoped<IStructuredFinancialMetricsSessionService, StructuredFinancialMetricsSessionService>();
builder.Services.AddScoped<SessionStructuredFinancialMetricsProvider>();
builder.Services.AddScoped<FixtureStructuredFinancialMetricsProvider>();
builder.Services.AddScoped<IStructuredFinancialMetricsProvider>(provider =>
    new CompositeStructuredFinancialMetricsProvider(
        provider.GetRequiredService<SessionStructuredFinancialMetricsProvider>(),
        provider.GetRequiredService<FixtureStructuredFinancialMetricsProvider>(),
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DataAgentOptions>>(),
        provider.GetRequiredService<ILogger<CompositeStructuredFinancialMetricsProvider>>()
    )
);
builder.Services.AddScoped<IDataAgentFinancialAnalysisWorkflow, DataAgentFinancialAnalysisWorkflow>();
builder.Services.AddScoped<IDataAgent, ConfigurableDataAgent>();
builder.Services.AddScoped<PythonAnomalyDetectionPlugin>();

// Python environment configuration for CSnakes
var defaultPythonHome = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "..", "..", "python-agents", "data_agent"));
var pythonHome = ResolvePythonHome(
    builder.Configuration["Python:Home"] ?? defaultPythonHome);

if (string.IsNullOrWhiteSpace(pythonHome) || !Directory.Exists(pythonHome))
{
    throw new InvalidOperationException(
        $"Python:Home configuration is missing or points to a directory that does not exist. Resolved value: '{pythonHome}'.");
}

var pythonVirtualEnvironment = Path.Combine(pythonHome, ".venv");
var pythonLockFile = Path.Combine(pythonHome, "requirements.lock");

if (!File.Exists(pythonLockFile))
{
    throw new InvalidOperationException(
        $"Python dependency lock file was not found at '{pythonLockFile}'. "
        + "Generate requirements.lock from requirements.txt before starting the API.");
}

var financialMetricsExtractionWorkerOptions = builder.Configuration
    .GetSection(FinancialMetricsExtractionOptions.SectionName)
    .Get<FinancialMetricsExtractionOptions>() ?? new FinancialMetricsExtractionOptions();
builder.Services.AddSingleton(new FinancialDocumentConversionGate(
    financialMetricsExtractionWorkerOptions.MaxConcurrentConversions));
builder.Services.AddSingleton<IFinancialDocumentProcessingGate>(provider =>
    provider.GetRequiredService<FinancialDocumentConversionGate>());
builder.Services.AddScoped<ISearchablePdfOcrService>(_ =>
    new LocalSearchablePdfOcrService(
        pythonHome,
        financialMetricsExtractionWorkerOptions.MaxWorkerMemoryBytes));
builder.Services.AddScoped<IFinancialDocumentMarkdownConverter>(provider =>
{
    var fileOptions = provider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<
            StructuredFinancialMetricsFileUploadOptions>>()
        .Value;
    var pdfOptions = provider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<
            StructuredFinancialMetricsPdfExtractionOptions>>()
        .Value;
    var extractionOptions = provider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<
            FinancialMetricsExtractionOptions>>()
        .Value;

    return new IsolatedFinancialDocumentMarkdownConverter(
        pythonHome,
        Math.Max(fileOptions.MaxFileSizeBytes, pdfOptions.MaxSearchablePdfBytes),
        extractionOptions.MaxWorkerMemoryBytes);
});

builder.Services
    .WithPython()
    .WithHome(pythonHome)
    .FromRedistributable()
    .WithVirtualEnvironment(pythonVirtualEnvironment)
    .WithPipInstaller(pythonLockFile);

// Legal agent and regulatory knowledge source configuration.
// CNV MCP capability selection is bootstrap-scoped; restart after config changes.
builder.Services.AddRegulatoryKnowledgeSource(builder.Configuration, builder.Environment);

builder.Services.AddScoped<LegalCompliancePlugin>();
builder.Services.AddScoped<ILegalAgent, SemanticKernelLegalAgent>();
builder.Services.AddLegalAgentAiReview(builder.Configuration);


// Persistence configuration
var orchestrationConnectionString = builder.Configuration.GetConnectionString("orchestrationdb");
if (string.IsNullOrWhiteSpace(orchestrationConnectionString))
{
    throw new InvalidOperationException("The orchestration database connection is not configured.");
}

builder.Configuration["ConnectionStrings:orchestrationdb"] =
    PostgresConnectionStringNormalizer.Normalize(orchestrationConnectionString);
builder.AddNpgsqlDbContext<OrchestrationDbContext>("orchestrationdb");
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrchestrationDbContext>(
        "orchestration_db",
        tags: ["ready"]);
builder.Services.AddScoped<IOrchestrationDbContext>(provider =>
    provider.GetRequiredService<OrchestrationDbContext>());
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddIdentityApiEndpoints<IdentityUser<Guid>>()
    .AddEntityFrameworkStores<OrchestrationDbContext>();
builder.Services.AddActivityHubAuthentication();
builder.Services
    .AddOptions<IdentityBootstrapOptions>()
    .Bind(builder.Configuration.GetSection(IdentityBootstrapOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<IdentityBootstrapOptions>,
    IdentityBootstrapOptionsValidator>();
builder.Services.AddSingleton<
    IOrchestrationDatabaseMigrator,
    OrchestrationDatabaseMigrator>();
builder.Services.AddHostedService<DatabaseMigrationHostedService>();
builder.Services.AddHostedService<CnvRegulationMcpStartupService>();
builder.Services.AddPersistentDataProtection();
builder.Services.AddHostedService<IdentityBootstrapHostedService>();
builder.Services.AddProductionHosting(builder.Configuration, builder.Environment);

var app = builder.Build();

if (args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase))
{
    await app.Services.GetRequiredService<IOrchestrationDatabaseMigrator>()
        .MigrateAsync(CancellationToken.None);
    return;
}

app.MapDefaultEndpoints();

app.UseProductionHosting();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGroup("/api/auth")
    .MapIdentityApi<IdentityUser<Guid>>()
    .WithTags("Authentication");

app.MapControllers();

app.MapHub<ActivityHub>("/hubs/activity");

app.Run();

static string ResolvePythonHome(string pythonHome)
{
    var resolvedPath = Path.GetFullPath(pythonHome);

    if (File.Exists(Path.Combine(resolvedPath, "anomaly_detection.py")))
    {
        return resolvedPath;
    }

    var dataAgentPath = Path.Combine(resolvedPath, "data_agent");

    if (File.Exists(Path.Combine(dataAgentPath, "anomaly_detection.py")))
    {
        return dataAgentPath;
    }

    return resolvedPath;
}

public partial class Program;

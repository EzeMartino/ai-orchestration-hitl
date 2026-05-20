using CSnakes.Runtime;
using Orchestration.Api.Hubs;
using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Planner.ToolCalling.Mapping;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Application.Persistence;
using Orchestration.Infrastructure.Agents.Data;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;
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
builder.Services.AddScoped<AnalysisSessionWorkflowService>();
builder.Services.AddSignalR();
builder.Services.AddScoped<IActivityEventPublisher, SignalRActivityEventPublisher>();
builder.Services.AddScoped<AnalysisOrchestratorService>();

// Planner agent configuration
builder.Services.AddPlannerReasoning(builder.Configuration);
builder.Services.AddToolPlanProposal(builder.Configuration);
builder.Services.AddScoped<IToolPlanValidator>(provider =>
    new ToolPlanValidator(
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ToolCallingOptions>>().Value
    )
);
builder.Services.AddScoped(provider =>
    provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ToolCallingOptions>>().Value
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
builder.Services.AddScoped<CSnakesDataAgent>();
builder.Services.AddScoped<IPythonFinancialAnalysisService, CSnakesFinancialAnalysisService>();
builder.Services.AddScoped<SemanticKernelDataAgent>();
builder.Services.AddScoped<ILegacyDataAgent>(provider =>
    provider.GetRequiredService<SemanticKernelDataAgent>());
builder.Services.AddScoped<IStructuredFinancialMetricsValidator, StructuredFinancialMetricsValidator>();
builder.Services.AddScoped<IFinancialMetricInputMapper, FinancialMetricInputMapper>();
builder.Services.AddScoped<IStructuredFinancialMetricsCsvParser, StructuredFinancialMetricsCsvParser>();
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
builder.Services.AddScoped<FinancialAnalysisPlugin>();

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

builder.Services
    .WithPython()
    .WithHome(pythonHome)
    .FromRedistributable();

// Legal agent and regulatory knowledge source configuration
builder.Services.Configure<CnvRegulationMcpOptions>(
    builder.Configuration.GetSection(CnvRegulationMcpOptions.SectionName)
);
builder.Services.AddScoped<ICnvRegulationMcpClient, CnvRegulationStdioMcpClient>();

var cnvMcpOptions = builder.Configuration
    .GetSection(CnvRegulationMcpOptions.SectionName)
    .Get<CnvRegulationMcpOptions>() ?? new CnvRegulationMcpOptions();

if (cnvMcpOptions.Enabled)
{
    builder.Services.AddScoped<IRegulatoryKnowledgeSource, McpRegulatoryKnowledgeSource>();
}
else
{
    builder.Services.AddScoped<IRegulatoryKnowledgeSource, MockRegulatoryKnowledgeSource>();
}

builder.Services.AddScoped<LegalCompliancePlugin>();
builder.Services.AddScoped<ILegalAgent, SemanticKernelLegalAgent>();

// Persistence configuration
builder.AddNpgsqlDbContext<OrchestrationDbContext>("orchestrationdb");
builder.Services.AddScoped<IOrchestrationDbContext>(provider =>
    provider.GetRequiredService<OrchestrationDbContext>());


builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseSwagger();
app.UseSwaggerUI();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");

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

var builder = DistributedApplication.CreateBuilder(args);

var llmEnabled = builder.Configuration["Llm:Enabled"];
var llmProvider = builder.Configuration["Llm:Provider"];
var llmModel = builder.Configuration["Llm:Model"];
var llmApiKey = builder.Configuration["Llm:ApiKey"];
var llmServiceId = builder.Configuration["Llm:ServiceId"];

var toolCallingEnabled = builder.Configuration["ToolCalling:Enabled"];
var toolCallingExecutionMode = builder.Configuration["ToolCalling:ExecutionMode"];
var toolCallingFinancialAnalysisToolsEnabled =
    builder.Configuration["ToolCalling:FinancialAnalysisToolsEnabled"];

var dataAgentFinancialAnalysisToolsEnabled =
    builder.Configuration["DataAgent:FinancialAnalysisToolsEnabled"];
var dataAgentUsePythonFinancialAnalysis =
    builder.Configuration["DataAgent:UsePythonFinancialAnalysis"];
var dataAgentUseLegacyAnomalyDetectionFallback =
    builder.Configuration["DataAgent:UseLegacyAnomalyDetectionFallback"];
var dataAgentUseFixtureMetricsFallback =
    builder.Configuration["DataAgent:UseFixtureMetricsFallback"];
var dataAgentRequireSessionFinancialMetrics =
    builder.Configuration["DataAgent:RequireSessionFinancialMetrics"];
var dataAgentAiReviewEnabled =
    builder.Configuration["DataAgent:AiReviewEnabled"];
var dataAgentRiskThresholdProfile =
    builder.Configuration["DataAgent:RiskThresholdProfile"];
var dataAgentStructuredMetricsFixturePath =
    builder.Configuration["DataAgent:StructuredMetricsFixturePath"];
var legalAgentAiReviewEnabled =
    builder.Configuration["LegalAgent:AiReviewEnabled"];

var postgres = builder
    .AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume();

var orchestrationDb = postgres.AddDatabase("orchestrationdb");
var cnvRegulationDb = postgres.AddDatabase("cnvregulationdb", "cnv_regulation");
var cnvRegulationMcpProject = Path.GetFullPath(
    Path.Combine(
        builder.AppHostDirectory,
        "..",
        "..",
        "tools",
        "CnvRegulation.McpServer",
        "src",
        "CnvRegulation.McpServer"));
var cnvRegulationSourcesDirectory = Path.GetFullPath(
    Path.Combine(
        builder.AppHostDirectory,
        "..",
        "..",
        "tools",
        "CnvRegulation.McpServer",
        "data",
        "sources"));

var cnvRegulationDbMigration = builder
    .AddExecutable(
        "cnv-regulation-db-migration",
        "dotnet",
        builder.AppHostDirectory,
        "run",
        "--project",
        cnvRegulationMcpProject,
        "--",
        "migrate-db")
    .WithEnvironment("CNV_REGULATION_DB_CONNECTION_STRING", cnvRegulationDb)
    .WaitFor(postgres);

var cnvRegulationDbIngestion = builder
    .AddExecutable(
        "cnv-regulation-db-ingestion",
        "dotnet",
        builder.AppHostDirectory,
        "run",
        "--project",
        cnvRegulationMcpProject,
        "--",
        "ingest",
        "--source-directory",
        cnvRegulationSourcesDirectory,
        "--storage",
        "postgres")
    .WithEnvironment("CNV_REGULATION_DB_CONNECTION_STRING", cnvRegulationDb)
    .WaitForCompletion(cnvRegulationDbMigration)
    .WithExplicitStart();

var defaultPythonHome = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", "..", "python-agents", "data_agent"));
var pythonHome = ResolvePythonHome(
    builder.Configuration["Python:Home"] ?? defaultPythonHome);

var api = builder
    .AddProject<Projects.Orchestration_Api>("orchestration-api")
    .WithEnvironment("Python__Home", pythonHome)
    .WithEnvironment("CNV_REGULATION_DB_CONNECTION_STRING", cnvRegulationDb)
    .WithEnvironment("Llm__Enabled", llmEnabled ?? "false")
    .WithEnvironment("Llm__Provider", llmProvider ?? "OpenAI")
    .WithEnvironment("Llm__Model", llmModel ?? "")
    .WithEnvironment("Llm__ApiKey", llmApiKey ?? "")
    .WithEnvironment("Llm__ServiceId", llmServiceId ?? "planner-reasoning")
    .WithEnvironment("ToolCalling__Enabled", toolCallingEnabled ?? "false")
    .WithEnvironment("ToolCalling__ExecutionMode", toolCallingExecutionMode ?? "Shadow")
    .WithEnvironment("ToolCalling__FinancialAnalysisToolsEnabled", toolCallingFinancialAnalysisToolsEnabled ?? "false")
    .WithEnvironment("DataAgent__FinancialAnalysisToolsEnabled", dataAgentFinancialAnalysisToolsEnabled ?? "false")
    .WithEnvironment("DataAgent__UsePythonFinancialAnalysis", dataAgentUsePythonFinancialAnalysis ?? "true")
    .WithEnvironment("DataAgent__UseLegacyAnomalyDetectionFallback", dataAgentUseLegacyAnomalyDetectionFallback ?? "true")
    .WithEnvironment("DataAgent__UseFixtureMetricsFallback", dataAgentUseFixtureMetricsFallback ?? "false")
    .WithEnvironment("DataAgent__RequireSessionFinancialMetrics", dataAgentRequireSessionFinancialMetrics ?? "false")
    .WithEnvironment("DataAgent__AiReviewEnabled", dataAgentAiReviewEnabled ?? "false")
    .WithEnvironment("DataAgent__RiskThresholdProfile", dataAgentRiskThresholdProfile ?? "default_oil_and_gas_equity_research")
    .WithEnvironment("DataAgent__StructuredMetricsFixturePath", dataAgentStructuredMetricsFixturePath ?? "")
    .WithEnvironment("LegalAgent__AiReviewEnabled", legalAgentAiReviewEnabled ?? "false")
    .WithReference(orchestrationDb)
    .WaitFor(orchestrationDb)
    .WaitFor(cnvRegulationDb)
    .WaitForCompletion(cnvRegulationDbMigration);

var frontendPath = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", "..", "frontend"));
var npmCommand = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";

builder
    .AddExecutable(
        "frontend",
        npmCommand,
        frontendPath,
        "run",
        "dev",
        "--",
        "--host",
        "127.0.0.1",
        "--port",
        "5173")
    .WithEnvironment("VITE_API_URL", api.GetEndpoint("http"))
    .WithHttpEndpoint(targetPort: 5173, port: 5173, isProxied: false)
    .WithExternalHttpEndpoints()
    .WaitFor(api);

builder.Build().Run();

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

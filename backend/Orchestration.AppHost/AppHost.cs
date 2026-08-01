var builder = DistributedApplication.CreateBuilder(args);

var llmEnabled = builder.Configuration["Llm:Enabled"];
var llmProvider = builder.Configuration["Llm:Provider"];
var llmModel = builder.Configuration["Llm:Model"];
var llmApiKey = builder.Configuration["Llm:ApiKey"];
var llmServiceId = builder.Configuration["Llm:ServiceId"];

var toolCallingEnabled = builder.Configuration["ToolCalling:Enabled"];
var toolCallingExecutionMode = builder.Configuration["ToolCalling:ExecutionMode"];

var cnvRegulationMcpEnabled = builder.Configuration["Mcp:CnvRegulation:Enabled"];
var cnvRegulationMcpRequired = builder.Configuration["Mcp:CnvRegulation:Required"];
var cnvRegulationMcpCommand = builder.Configuration["Mcp:CnvRegulation:Command"];
var cnvRegulationMcpConnectionTimeoutSeconds =
    builder.Configuration["Mcp:CnvRegulation:ConnectionTimeoutSeconds"];
var cnvRegulationMcpToolCallTimeoutSeconds =
    builder.Configuration["Mcp:CnvRegulation:ToolCallTimeoutSeconds"];

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
var financialMetricsExtractionSemanticEnrichmentEnabled =
    builder.Configuration["FinancialMetricsExtraction:SemanticEnrichmentEnabled"];
var financialMetricsExtractionMode =
    builder.Configuration["FinancialMetricsExtraction:Mode"];
var financialMetricsExtractionDeterministicCoverageThreshold =
    builder.Configuration["FinancialMetricsExtraction:DeterministicCoverageThreshold"];
var financialMetricsExtractionAutomaticAcceptanceConfidence =
    builder.Configuration["FinancialMetricsExtraction:AutomaticAcceptanceConfidence"];
var financialMetricsExtractionMaxMarkdownCharacters =
    builder.Configuration["FinancialMetricsExtraction:MaxMarkdownCharacters"];
var financialMetricsExtractionMaxMarkdownChunks =
    builder.Configuration["FinancialMetricsExtraction:MaxMarkdownChunks"];
var financialMetricsExtractionConversionTimeoutSeconds =
    builder.Configuration["FinancialMetricsExtraction:ConversionTimeoutSeconds"];
var financialMetricsExtractionMaxWorkerMemoryBytes =
    builder.Configuration["FinancialMetricsExtraction:MaxWorkerMemoryBytes"];
var financialMetricsExtractionMaxConcurrentConversions =
    builder.Configuration["FinancialMetricsExtraction:MaxConcurrentConversions"];
var financialMetricsExtractionSemanticExtractionTimeoutSeconds =
    builder.Configuration["FinancialMetricsExtraction:SemanticExtractionTimeoutSeconds"];
var financialMetricsExtractionMaxEvidenceExcerptCharacters =
    builder.Configuration["FinancialMetricsExtraction:MaxEvidenceExcerptCharacters"];

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
var cnvRegulationMcpArgs = builder.Configuration
    .GetSection("Mcp:CnvRegulation:Args")
    .GetChildren()
    .Select(section => (
        Index: section.Key,
        Value: section.Value ?? string.Empty))
    .OrderBy(argument =>
        int.TryParse(argument.Index, out var index) ? index : int.MaxValue)
    .ThenBy(argument => argument.Index, StringComparer.Ordinal)
    .ToArray();

if (cnvRegulationMcpArgs.Length == 0)
{
    cnvRegulationMcpArgs =
    [
        ("0", "run"),
        ("1", "--project"),
        ("2", cnvRegulationMcpProject),
        ("3", "--"),
        ("4", "--storage"),
        ("5", "postgres")
    ];
}

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
    .WithEnvironment("Mcp__CnvRegulation__Enabled", cnvRegulationMcpEnabled ?? "false")
    .WithEnvironment("Mcp__CnvRegulation__Required", cnvRegulationMcpRequired ?? "false")
    .WithEnvironment("Mcp__CnvRegulation__Command", cnvRegulationMcpCommand ?? "dotnet")
    .WithEnvironment(
        "Mcp__CnvRegulation__ConnectionTimeoutSeconds",
        cnvRegulationMcpConnectionTimeoutSeconds ?? "15")
    .WithEnvironment(
        "Mcp__CnvRegulation__ToolCallTimeoutSeconds",
        cnvRegulationMcpToolCallTimeoutSeconds ?? "30")
    .WithEnvironment("DataAgent__FinancialAnalysisToolsEnabled", dataAgentFinancialAnalysisToolsEnabled ?? "false")
    .WithEnvironment("DataAgent__UsePythonFinancialAnalysis", dataAgentUsePythonFinancialAnalysis ?? "true")
    .WithEnvironment("DataAgent__UseLegacyAnomalyDetectionFallback", dataAgentUseLegacyAnomalyDetectionFallback ?? "true")
    .WithEnvironment("DataAgent__UseFixtureMetricsFallback", dataAgentUseFixtureMetricsFallback ?? "false")
    .WithEnvironment("DataAgent__RequireSessionFinancialMetrics", dataAgentRequireSessionFinancialMetrics ?? "false")
    .WithEnvironment("DataAgent__AiReviewEnabled", dataAgentAiReviewEnabled ?? "false")
    .WithEnvironment("DataAgent__RiskThresholdProfile", dataAgentRiskThresholdProfile ?? "default_oil_and_gas_equity_research")
    .WithEnvironment("DataAgent__StructuredMetricsFixturePath", dataAgentStructuredMetricsFixturePath ?? "")
    .WithEnvironment("LegalAgent__AiReviewEnabled", legalAgentAiReviewEnabled ?? "false")
    .WithEnvironment("FinancialMetricsExtraction__SemanticEnrichmentEnabled", financialMetricsExtractionSemanticEnrichmentEnabled ?? "false")
    .WithEnvironment("FinancialMetricsExtraction__Mode", financialMetricsExtractionMode ?? "ReviewOnly")
    .WithEnvironment("FinancialMetricsExtraction__DeterministicCoverageThreshold", financialMetricsExtractionDeterministicCoverageThreshold ?? "0.7")
    .WithEnvironment("FinancialMetricsExtraction__AutomaticAcceptanceConfidence", financialMetricsExtractionAutomaticAcceptanceConfidence ?? "0.9")
    .WithEnvironment("FinancialMetricsExtraction__MaxMarkdownCharacters", financialMetricsExtractionMaxMarkdownCharacters ?? "200000")
    .WithEnvironment("FinancialMetricsExtraction__MaxMarkdownChunks", financialMetricsExtractionMaxMarkdownChunks ?? "12")
    .WithEnvironment("FinancialMetricsExtraction__ConversionTimeoutSeconds", financialMetricsExtractionConversionTimeoutSeconds ?? "60")
    .WithEnvironment("FinancialMetricsExtraction__MaxWorkerMemoryBytes", financialMetricsExtractionMaxWorkerMemoryBytes ?? "1073741824")
    .WithEnvironment("FinancialMetricsExtraction__MaxConcurrentConversions", financialMetricsExtractionMaxConcurrentConversions ?? "2")
    .WithEnvironment("FinancialMetricsExtraction__SemanticExtractionTimeoutSeconds", financialMetricsExtractionSemanticExtractionTimeoutSeconds ?? "90")
    .WithEnvironment("FinancialMetricsExtraction__MaxEvidenceExcerptCharacters", financialMetricsExtractionMaxEvidenceExcerptCharacters ?? "500")
    .WithReference(orchestrationDb)
    .WaitFor(orchestrationDb)
    .WaitFor(cnvRegulationDb)
    .WaitForCompletion(cnvRegulationDbMigration);

foreach (var argument in cnvRegulationMcpArgs)
{
    api.WithEnvironment(
        $"Mcp__CnvRegulation__Args__{argument.Index}",
        argument.Value);
}

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

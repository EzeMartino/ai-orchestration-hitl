using Orchestration.Infrastructure.Persistence;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Api.Hubs;
using Orchestration.Application.Activity;
using Orchestration.Application.Persistence;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner;
using Orchestration.Infrastructure.Agents.Data;
using CSnakes.Runtime;

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
builder.Services.AddScoped<IPlannerAgent, PlannerAgent>();
builder.Services.AddScoped<IDataAgent, CSnakesDataAgent>();
builder.Services.AddScoped<ILegalAgent, MockLegalAgent>();

builder.AddNpgsqlDbContext<OrchestrationDbContext>("orchestrationdb");
builder.Services.AddScoped<IOrchestrationDbContext>(provider =>
    provider.GetRequiredService<OrchestrationDbContext>());

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

app.UseHttpsRedirection();

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

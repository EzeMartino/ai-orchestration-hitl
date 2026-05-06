using Orchestration.Infrastructure.Persistence;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Api.Hubs;
using Orchestration.Application.Activity;
using Orchestration.Application.Persistence;

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

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.MapControllers();

app.MapHub<ActivityHub>("/hubs/activity");

app.Run();

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithDataVolume();

var orchestrationDb = postgres.AddDatabase("orchestrationdb");

var api = builder
    .AddProject<Projects.Orchestration_Api>("orchestration-api")
    .WithReference(orchestrationDb)
    .WaitFor(orchestrationDb);

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
    .WithHttpEndpoint(targetPort: 5173, port: 5173, isProxied: false)
    .WithExternalHttpEndpoints()
    .WaitFor(api);

builder.Build().Run();

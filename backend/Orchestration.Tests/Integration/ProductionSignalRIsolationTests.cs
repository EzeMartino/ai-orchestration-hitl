using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orchestration.Application.Activity;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace Orchestration.Tests.Integration;

[Collection(ProductionIntegrationCollection.Name)]
public sealed class ProductionSignalRIsolationTests(ITestOutputHelper output)
{
    private static readonly TimeSpan ContainerStartTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ContainerDisposeTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    [Trait("Category", "ProductionIntegration")]
    public async Task ActivityHub_Production_IsolatesAuthenticatedUsersAndRejectsAnonymousClients()
    {
        var identity = Guid.NewGuid().ToString("N");
        var databaseName = $"signalr_{identity[..16]}";
        var database = CreatePostgresContainer(
            $"aihitl-task11-app-signalr-{identity}",
            databaseName);
        ExceptionDispatchInfo? primaryFailure = null;
        var cleanupFailures = new List<Exception>();

        try
        {
            await StartContainerAsync(
                database,
                "Task 11 SignalR container start");
            WriteDisposableIdentity("signalr-app", database, databaseName);

            await using var factory = new SignalRWebApplicationFactory(
                database.GetConnectionString());
            using var userAClient = factory.CreateClient(CreateClientOptions());
            using var userBClient = factory.CreateClient(CreateClientOptions());

            var suffix = Guid.NewGuid().ToString("N");
            var password = $"Task11!{suffix}aA1";
            var userAToken = await RegisterAndLoginAsync(
                userAClient,
                $"task11-a-{suffix}@example.test",
                password);
            var userBToken = await RegisterAndLoginAsync(
                userBClient,
                $"task11-b-{suffix}@example.test",
                password);

            userAClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", userAToken);
            userBClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", userBToken);

            await using var userAHub = CreateHubConnection(factory, userAClient, userAToken);
            await using var userBHub = CreateHubConnection(factory, userBClient, userBToken);
            await using var anonymousHub = CreateHubConnection(factory, userAClient, accessToken: null);

            var userAEvents = new ConcurrentQueue<ActivityEvent>();
            var userBEvents = new ConcurrentQueue<ActivityEvent>();
            var userAReceived = NewEventCompletionSource();
            var userBReceived = NewEventCompletionSource();

            using var userASubscription = userAHub.On<ActivityEvent>(
                "activityEventReceived",
                activityEvent =>
                {
                    userAEvents.Enqueue(activityEvent);
                    userAReceived.TrySetResult(activityEvent);
                });
            using var userBSubscription = userBHub.On<ActivityEvent>(
                "activityEventReceived",
                activityEvent =>
                {
                    userBEvents.Enqueue(activityEvent);
                    userBReceived.TrySetResult(activityEvent);
                });

            await userAHub.StartAsync();
            await userBHub.StartAsync();

            var anonymousFailure = await Assert.ThrowsAnyAsync<Exception>(
                () => anonymousHub.StartAsync());
            var unauthorizedFailure = FindException<HttpRequestException>(anonymousFailure);
            Assert.NotNull(unauthorizedFailure);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedFailure.StatusCode);

            var sessionResponse = await userAClient.PostAsync(
                "/api/analysis-sessions",
                content: null);
            Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
            var sessionId = await ReadRequiredGuidAsync(sessionResponse, "id");

            var expected = new ActivityEvent(
                sessionId,
                "task11_isolation_probe",
                "Task11",
                "Synthetic production integration event.",
                DateTimeOffset.UtcNow);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var publisher = scope.ServiceProvider
                    .GetRequiredService<IActivityEventPublisher>();
                await publisher.PublishAsync(expected);
            }

            var receivedByA = await userAReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(expected, receivedByA);

            var userBWindow = await Task.WhenAny(
                userBReceived.Task,
                Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.NotSame(userBReceived.Task, userBWindow);
            Assert.Single(userAEvents);
            Assert.Empty(userBEvents);

            var persistedResponse = await userAClient.GetAsync(
                $"/api/analysis-sessions/{sessionId}/events");
            Assert.Equal(HttpStatusCode.OK, persistedResponse.StatusCode);
            var persistedEvents = await persistedResponse.Content
                .ReadFromJsonAsync<ActivityEvent[]>();
            var persisted = Assert.Single(Assert.IsType<ActivityEvent[]>(persistedEvents));
            Assert.Equal(expected.SessionId, persisted.SessionId);
            Assert.Equal(expected.Type, persisted.Type);

            var otherUserResponse = await userBClient.GetAsync(
                $"/api/analysis-sessions/{sessionId}/events");
            Assert.Equal(HttpStatusCode.NotFound, otherUserResponse.StatusCode);
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            await CaptureCleanupFailureAsync(
                () => DisposeContainerAsync(
                    database,
                    "Task 11 SignalR container dispose"),
                "Task 11 SignalR container dispose",
                cleanupFailures);
        }

        ThrowPrimaryOrCleanupFailures(primaryFailure, cleanupFailures);
    }

    private static WebApplicationFactoryClientOptions CreateClientOptions() => new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("http://localhost")
    };

    private static PostgreSqlContainer CreatePostgresContainer(
        string containerName,
        string databaseName) =>
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithName(containerName)
            .WithDatabase(databaseName)
            .WithUsername("postgres")
            .WithPassword(CreateContainerPassword())
            .WithCleanUp(true)
            .Build();

    private static string CreateContainerPassword() =>
        $"T11-{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}-aA1!";

    private static async Task StartContainerAsync(
        PostgreSqlContainer container,
        string operation)
    {
        using var deadline = new CancellationTokenSource(ContainerStartTimeout);
        var start = container.StartAsync(deadline.Token);

        try
        {
            await AwaitTaskWithDeadlineAsync(start, deadline.Token, operation);
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CreateSafeLifecycleFailure(operation, exception);
        }
    }

    private static async Task DisposeContainerAsync(
        PostgreSqlContainer container,
        string operation)
    {
        var dispose = container.DisposeAsync().AsTask();

        try
        {
            await AwaitTaskWithDeadlineAsync(
                dispose,
                ContainerDisposeTimeout,
                operation);
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CreateSafeLifecycleFailure(operation, exception);
        }
    }

    private static async Task AwaitTaskWithDeadlineAsync(
        Task task,
        TimeSpan timeout,
        string operation)
    {
        using var deadline = new CancellationTokenSource(timeout);
        await AwaitTaskWithDeadlineAsync(task, deadline.Token, operation);
    }

    private static async Task AwaitTaskWithDeadlineAsync(
        Task task,
        CancellationToken deadline,
        string operation)
    {
        ObserveTask(task);

        try
        {
            await task.WaitAsync(deadline);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"{operation} timed out.");
        }
    }

    private static void ObserveTask(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously
            | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static async Task CaptureCleanupFailureAsync(
        Func<Task> cleanup,
        string operation,
        ICollection<Exception> cleanupFailures)
    {
        try
        {
            await cleanup();
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(CreateSafeLifecycleFailure(operation, exception));
        }
    }

    private static Exception CreateSafeLifecycleFailure(
        string operation,
        Exception failure) =>
        failure is TimeoutException
            ? new TimeoutException($"{operation} timed out.")
            : new InvalidOperationException(
                $"{operation} failed with {failure.GetType().Name}.");

    private static void ThrowPrimaryOrCleanupFailures(
        ExceptionDispatchInfo? primaryFailure,
        IReadOnlyCollection<Exception> cleanupFailures)
    {
        if (primaryFailure is null)
        {
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "Task 11 cleanup failed.",
                    cleanupFailures);
            }

            return;
        }

        if (cleanupFailures.Count == 0)
        {
            primaryFailure.Throw();
            return;
        }

        throw new AggregateException(
            "Task 11 primary operation and cleanup failed.",
            new[] { primaryFailure.SourceException }.Concat(cleanupFailures));
    }

    private static async Task<string> RegisterAndLoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=false&useSessionCookies=false",
            new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var payload = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.True(payload.RootElement.TryGetProperty("accessToken", out var token));
        Assert.Equal(JsonValueKind.String, token.ValueKind);
        return Assert.IsType<string>(token.GetString());
    }

    private static HubConnection CreateHubConnection(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(client.BaseAddress!, "/hubs/activity"),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    if (accessToken is not null)
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                    }
                })
            .Build();

    private static TaskCompletionSource<ActivityEvent> NewEventCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<Guid> ReadRequiredGuidAsync(
        HttpResponseMessage response,
        string propertyName)
    {
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(payload.RootElement.TryGetProperty(propertyName, out var value));
        return value.GetGuid();
    }

    private void WriteDisposableIdentity(
        string role,
        PostgreSqlContainer container,
        string databaseName)
    {
        var fingerprintMaterial = $"{container.Id}|{container.Name}|{databaseName}";
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintMaterial)))
            .ToLowerInvariant();
        output.WriteLine(
            "Disposable {0} container: id={1}; name={2}; run identity fingerprint={3}",
            role,
            container.Id,
            container.Name,
            fingerprint);
    }

    private static TException? FindException<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException typed)
            {
                return typed;
            }
        }

        return null;
    }

    private sealed class SignalRWebApplicationFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("ConnectionStrings:orchestrationdb", connectionString);
            builder.UseSetting("Cors:AllowedOrigins:0", "https://frontend.example.test");
            builder.UseSetting("Mcp:CnvRegulation:Enabled", "false");
            builder.UseSetting("Mcp:CnvRegulation:Required", "false");
            builder.UseSetting("Llm:Enabled", "false");
            builder.UseSetting("DataAgent:AiReviewEnabled", "false");
            builder.UseSetting("DataAgent:UseFixtureMetricsFallback", "false");
            builder.UseSetting("LegalAgent:AiReviewEnabled", "false");
            builder.UseSetting("ToolCalling:Enabled", "false");
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProductionIntegrationCollection
{
    public const string Name = "Production integration";
}

# Production Readiness and Render Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve the six approved production blockers so the application can run consistently on Render with safe identity bootstrap, user-isolated realtime events, validated origins, reproducible deployment artifacts, production-safe HTTP behavior, and mandatory real CNV MCP retrieval.

**Architecture:** Keep the existing ASP.NET Core API, Vite frontend, Python financial-analysis stack, and stdio CNV MCP boundary. Package API, Python/OCR, and a self-contained Linux MCP executable in one non-root Docker image; use separate Render PostgreSQL databases for application state and CNV data; persist Data Protection keys in the application database; make proxy, health, CORS, SignalR, and MCP behavior explicit and fail closed.

**Tech Stack:** .NET 10, ASP.NET Core Identity API endpoints, SignalR, EF Core 10, Npgsql, .NET 8 self-contained MCP server, Model Context Protocol, Python 3.12, CSnakes 1.2.1, React 19, TypeScript 6, Vite 8, Docker, Render Blueprints, PostgreSQL.

---

## Approved Blocker Coverage

| Approved blocker | Plan tasks | Acceptance proof |
|---|---|---|
| 1. Committed seeded users/password | 2 | bootstrap disabled by default; no account values in source; migration remains independent |
| 2. Anonymous/global SignalR | 3-4 | authorized hub, opaque token transport, owner-only delivery, two-user isolation |
| 3. Hard-coded localhost CORS | 5 | typed validated origins for HTTP negotiation and WebSockets |
| 4. No Docker/Render packaging | 8-9 | image contains API, Python/OCR, MCP; Blueprint validates and declares all resources |
| 5. Development-only health and unsafe proxy/Swagger behavior | 5 and 7 | forwarded headers first, public minimal health, Development-only Swagger, authenticated diagnostics |
| 6. Disabled MCP silently selects mock | 6 | explicit environment matrix, required startup/readiness probe, no simulated production findings |

## File and Responsibility Map

New backend deployment and persistence units:

- `backend/Orchestration.Infrastructure/Persistence/PostgresConnectionStringNormalizer.cs` — convert Render PostgreSQL URI values into Npgsql keyword/value connection strings.
- `backend/Orchestration.Infrastructure/Persistence/IOrchestrationDatabaseMigrator.cs` — migration contract shared by startup and command mode.
- `backend/Orchestration.Infrastructure/Persistence/OrchestrationDatabaseMigrator.cs` — EF Core migration implementation.
- `backend/Orchestration.Infrastructure/Persistence/DatabaseMigrationHostedService.cs` — idempotent startup migration fallback.
- `backend/Orchestration.Infrastructure/Persistence/IdentityBootstrapOptions.cs` — optional account configuration with no defaults.
- `backend/Orchestration.Infrastructure/Persistence/IdentityBootstrapOptionsValidator.cs` — all-or-nothing bootstrap validation.
- `backend/Orchestration.Infrastructure/Persistence/IdentityBootstrapHostedService.cs` — create only explicitly configured missing users.
- `backend/Orchestration.Infrastructure/Persistence/PersistentDataProtectionExtensions.cs` — stable Data Protection application name and database key storage.

New API hosting units:

- `backend/Orchestration.Api/Hosting/FrontendCorsOptions.cs` — typed allowed-origin configuration.
- `backend/Orchestration.Api/Hosting/FrontendCorsOptionsValidator.cs` — environment-aware origin validation.
- `backend/Orchestration.Api/Hosting/RenderProxyOptions.cs` — explicit proxy-header trust switch.
- `backend/Orchestration.Api/Hosting/ProductionHostingExtensions.cs` — CORS, WebSocket origins, forwarded headers, and safe health responses.
- `backend/Orchestration.Api/Hubs/ActivityHubAuthenticationExtensions.cs` — Identity bearer query-token handling restricted to the hub path.

New CNV MCP integration units:

- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/UnavailableRegulatoryKnowledgeSource.cs` — explicit non-simulated disabled production state.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/RegulatoryKnowledgeServiceCollectionExtensions.cs` — registration matrix for real, mock, unavailable, and invalid required modes.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/ICnvRegulationMcpProbe.cs` — bounded readiness contract.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulationMcpProbe.cs` — read-only full-text MCP query.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulationMcpHealthCheck.cs` — readiness integration without sensitive output.
- `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulationMcpStartupService.cs` — fail startup when required MCP cannot execute.

New MCP persistence unit:

- `tools/CnvRegulation.McpServer/src/CnvRegulation.Infrastructure/Persistence/PostgresConnectionStringNormalizer.cs` — same Render URI contract inside the .NET 8 MCP process.

New frontend unit:

- `frontend/src/services/signalR.ts` — testable authenticated connection factory.

Deployment artifacts:

- `Dockerfile` — multi-stage production image.
- `.dockerignore` — deterministic, secret-free build context.
- `render.yaml` — API, static frontend, two PostgreSQL databases, flags, and pre-deploy migrations.
- `docs/deployment/render.md` — first deploy, corpus gate, verification, rollback, and flag runbook.
- `progress.md` — containerization verification log required by the container workflow.

## Task 1: Normalize Render PostgreSQL URIs in Both Processes

**Files:**

- Create: `backend/Orchestration.Infrastructure/Persistence/PostgresConnectionStringNormalizer.cs`
- Modify: `backend/Orchestration.Api/Program.cs`
- Create: `backend/Orchestration.Tests/Persistence/PostgresConnectionStringNormalizerTests.cs`
- Create: `tools/CnvRegulation.McpServer/src/CnvRegulation.Infrastructure/Persistence/PostgresConnectionStringNormalizer.cs`
- Modify: `tools/CnvRegulation.McpServer/src/CnvRegulation.Infrastructure/Persistence/RegulationDbOptions.cs`
- Create: `tools/CnvRegulation.McpServer/tests/CnvRegulation.Application.Tests/PostgresConnectionStringNormalizerTests.cs`

- [ ] **Step 1: Write failing API-side normalization tests**

Cover keyword/value pass-through, percent-decoded URI credentials, default port, explicit port, database path, unsupported scheme, missing database, and malformed user info:

```csharp
[Fact]
public void Normalize_Should_convert_Render_Postgres_uri()
{
    var result = PostgresConnectionStringNormalizer.Normalize(
        "postgresql://render%40user:p%2Fa%3Ass@dpg.internal:5433/orchestration");

    var parsed = new NpgsqlConnectionStringBuilder(result);
    parsed.Host.Should().Be("dpg.internal");
    parsed.Port.Should().Be(5433);
    parsed.Database.Should().Be("orchestration");
    parsed.Username.Should().Be("render@user");
    parsed.Password.Should().Be("p/a:ss");
}

[Fact]
public void Normalize_Should_preserve_Npgsql_connection_string()
{
    const string source =
        "Host=localhost;Port=5432;Database=orchestration;Username=postgres;Password=test";

    PostgresConnectionStringNormalizer.Normalize(source).Should().Be(source);
}
```

- [ ] **Step 2: Run the API-side tests and verify the helper is absent**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~PostgresConnectionStringNormalizerTests" --verbosity minimal
```

Expected: FAIL to compile because `PostgresConnectionStringNormalizer` does not exist.

- [ ] **Step 3: Implement a strict normalizer**

Use `NpgsqlConnectionStringBuilder` for both validation and final serialization. Accept only `postgres://` and `postgresql://` URI schemes; URI-decode user, password, and database; reject missing host, credentials, or database; never include the source value in exception messages.

Core shape:

```csharp
public static string Normalize(string value)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(value);

    if (!value.Contains("://", StringComparison.Ordinal))
    {
        _ = new NpgsqlConnectionStringBuilder(value);
        return value;
    }

    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
        || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
    {
        throw new InvalidOperationException(
            "PostgreSQL connection value must be an Npgsql connection string or PostgreSQL URI.");
    }

    var credentials = uri.UserInfo.Split(':', 2);
    var database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
    if (credentials.Length != 2
        || string.IsNullOrWhiteSpace(uri.Host)
        || string.IsNullOrWhiteSpace(database))
    {
        throw new InvalidOperationException("PostgreSQL URI is missing required components.");
    }

    return new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = database,
        Username = Uri.UnescapeDataString(credentials[0]),
        Password = Uri.UnescapeDataString(credentials[1])
    }.ConnectionString;
}
```

In `Program.cs`, resolve `ConnectionStrings:orchestrationdb`, normalize it, and register `OrchestrationDbContext` with `UseNpgsql(normalizedValue)` instead of passing the Render URI directly to `AddNpgsqlDbContext`. Keep Aspire-compatible named configuration for local development.

- [ ] **Step 4: Add the equivalent MCP tests and implementation**

Use the same behavioral test matrix under the .NET 8 test project. Call the MCP helper from `RegulationDbOptions.Create` after environment-variable precedence is resolved:

```csharp
var resolvedConnectionString = string.IsNullOrWhiteSpace(environmentConnectionString)
    ? connectionString
    : environmentConnectionString;

ConnectionString = string.IsNullOrWhiteSpace(resolvedConnectionString)
    ? null
    : PostgresConnectionStringNormalizer.Normalize(resolvedConnectionString);
```

Do not log normalized connection strings.

- [ ] **Step 5: Run both test suites**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~PostgresConnectionStringNormalizerTests" --verbosity minimal
dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.Application.Tests/CnvRegulation.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~PostgresConnectionStringNormalizerTests" --verbosity minimal
```

Expected: PASS; both processes accept the exact Render URI form and reject malformed input without echoing secrets.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.Api/Program.cs backend/Orchestration.Infrastructure/Persistence/PostgresConnectionStringNormalizer.cs backend/Orchestration.Tests/Persistence/PostgresConnectionStringNormalizerTests.cs tools/CnvRegulation.McpServer/src/CnvRegulation.Infrastructure/Persistence tools/CnvRegulation.McpServer/tests/CnvRegulation.Application.Tests/PostgresConnectionStringNormalizerTests.cs
git commit -m "fix: normalize Render PostgreSQL connections"
```

## Task 2: Separate Migration, Remove Committed Accounts, and Persist Data Protection Keys

**Files:**

- Delete: `backend/Orchestration.Infrastructure/Persistence/IdentityDataSeeder.cs`
- Create: `backend/Orchestration.Infrastructure/Persistence/IOrchestrationDatabaseMigrator.cs`
- Create: `backend/Orchestration.Infrastructure/Persistence/OrchestrationDatabaseMigrator.cs`
- Create: `backend/Orchestration.Infrastructure/Persistence/DatabaseMigrationHostedService.cs`
- Create: `backend/Orchestration.Infrastructure/Persistence/IdentityBootstrapOptions.cs`
- Create: `backend/Orchestration.Infrastructure/Persistence/IdentityBootstrapOptionsValidator.cs`
- Create: `backend/Orchestration.Infrastructure/Persistence/IdentityBootstrapHostedService.cs`
- Create: `backend/Orchestration.Infrastructure/Persistence/PersistentDataProtectionExtensions.cs`
- Modify: `backend/Orchestration.Infrastructure/Persistence/OrchestrationDbContext.cs`
- Modify: `backend/Orchestration.Infrastructure/Orchestration.Infrastructure.csproj`
- Modify: `backend/Orchestration.Api/Program.cs`
- Modify: `backend/Orchestration.Api/appsettings.json`
- Modify: `backend/Orchestration.Api/appsettings.Development.example.json`
- Create: `backend/Orchestration.Tests/Persistence/IdentityBootstrapOptionsValidatorTests.cs`
- Create: `backend/Orchestration.Tests/Persistence/IdentityBootstrapHostedServiceTests.cs`
- Create: `backend/Orchestration.Tests/Persistence/DatabaseMigrationHostedServiceTests.cs`
- Create: `backend/Orchestration.Tests/Persistence/PersistentDataProtectionTests.cs`
- Create through EF tooling: migration source/designer pair named `PersistDataProtectionKeys` under `backend/Orchestration.Infrastructure/Persistence/Migrations/`
- Modify: `backend/Orchestration.Infrastructure/Persistence/Migrations/OrchestrationDbContextModelSnapshot.cs`

- [ ] **Step 1: Write failing option-validation tests**

Required cases:

```csharp
[Fact]
public void Validate_Should_succeed_when_bootstrap_is_disabled()
{
    var result = validator.Validate(null, new IdentityBootstrapOptions());

    result.Succeeded.Should().BeTrue();
}

[Fact]
public void Validate_Should_reject_partial_user_before_startup()
{
    var options = new IdentityBootstrapOptions
    {
        Enabled = true,
        Users = [new() { Email = "admin@example.com" }]
    };

    var result = validator.Validate(null, options);

    result.Failed.Should().BeTrue();
    result.Failures.Should().Contain(message => message.Contains("Password"));
}
```

Also reject an enabled empty list, duplicate normalized emails, invalid email format, weak/blank password, and invalid explicit ID. Assert validation output never contains the password.

- [ ] **Step 2: Define configuration with no account defaults**

Use:

```csharp
public sealed class IdentityBootstrapOptions
{
    public const string SectionName = "IdentityBootstrap";
    public bool Enabled { get; init; }
    public IReadOnlyList<IdentityBootstrapUserOptions> Users { get; init; } = [];
}

public sealed class IdentityBootstrapUserOptions
{
    public Guid? Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool EmailConfirmed { get; init; }
}
```

Add only this safe default to `appsettings.json`:

```json
"IdentityBootstrap": {
  "Enabled": false,
  "Users": []
}
```

The Development example may document key names with empty strings, but must not contain a usable email/password pair.

- [ ] **Step 3: Write failing hosted-service tests**

Verify:

- disabled bootstrap never resolves `UserManager`;
- enabled valid bootstrap creates a missing user with configured values;
- a missing optional ID uses a generated GUID;
- an existing user is left unchanged;
- create failure stops startup;
- no branch removes or replaces an existing password;
- migration succeeds when bootstrap is disabled.

Use a recording `IOrchestrationDatabaseMigrator` and a test Identity store. Do not assert or print raw passwords.

- [ ] **Step 4: Implement independent migration and bootstrap**

Migration contract:

```csharp
public interface IOrchestrationDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken);
}
```

Implementation creates a scope, resolves `OrchestrationDbContext`, and calls `Database.MigrateAsync`. `DatabaseMigrationHostedService` depends only on this contract. Register it before `IdentityBootstrapHostedService`, so startup migration precedes optional user creation.

Bootstrap behavior:

```csharp
if (!options.Value.Enabled)
{
    return;
}

foreach (var configuredUser in options.Value.Users)
{
    var existing = await userManager.FindByEmailAsync(configuredUser.Email);
    if (existing is not null)
    {
        continue;
    }

    var user = new IdentityUser<Guid>
    {
        Id = configuredUser.Id ?? Guid.NewGuid(),
        UserName = configuredUser.Email,
        Email = configuredUser.Email,
        EmailConfirmed = configuredUser.EmailConfirmed
    };

    var result = await userManager.CreateAsync(user, configuredUser.Password);
    if (!result.Succeeded)
    {
        throw new InvalidOperationException(
            $"Identity bootstrap failed for configured user index {index}: "
            + string.Join(", ", result.Errors.Select(error => error.Code)));
    }
}
```

Never log email/password values. Delete all fixed IDs, `@ezemartino.com` emails, password hashes, and `Password1!`.

- [ ] **Step 5: Add database-backed Data Protection**

Add package version `10.0.7`:

```xml
<PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="10.0.7" />
```

Implement `IDataProtectionKeyContext`:

```csharp
public class OrchestrationDbContext :
    IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>,
    IOrchestrationDbContext,
    IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys =>
        Set<DataProtectionKey>();
}
```

Register:

```csharp
services.AddDataProtection()
    .SetApplicationName("ai-orchestration-hitl")
    .PersistKeysToDbContext<OrchestrationDbContext>();
```

Generate the real migration; do not hand-author designer/snapshot files:

```powershell
dotnet ef migrations add PersistDataProtectionKeys --project backend/Orchestration.Infrastructure/Orchestration.Infrastructure.csproj --startup-project backend/Orchestration.Api/Orchestration.Api.csproj --output-dir Persistence/Migrations
```

- [ ] **Step 6: Add bounded migration-only mode**

In `Program.cs`, detect the exact argument before `app.Run()`:

```csharp
if (args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase))
{
    await app.Services
        .GetRequiredService<IOrchestrationDatabaseMigrator>()
        .MigrateAsync(CancellationToken.None);
    return;
}
```

Normal startup retains the idempotent hosted migration fallback. The command exits without binding an HTTP port.

- [ ] **Step 7: Run focused proof and scan for leaked defaults**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~IdentityBootstrap|FullyQualifiedName~DatabaseMigrationHostedServiceTests|FullyQualifiedName~PersistentDataProtectionTests" --verbosity minimal
rg -n "Password1!|admin@ezemartino\.com|user@ezemartino\.com|00000000-0000-0000-0000-00000000000[12]" backend
```

Expected: tests PASS; `rg` returns no matches.

- [ ] **Step 8: Commit**

```powershell
git add backend/Orchestration.Api backend/Orchestration.Infrastructure backend/Orchestration.Tests/Persistence
git commit -m "fix: make identity bootstrap production safe"
```

## Task 3: Authenticate SignalR and Deliver Events Only to the Session Owner

**Files:**

- Modify: `backend/Orchestration.Api/Hubs/ActivityHub.cs`
- Modify: `backend/Orchestration.Api/Hubs/SignalRActivityPublisher.cs`
- Create: `backend/Orchestration.Api/Hubs/ActivityHubAuthenticationExtensions.cs`
- Modify: `backend/Orchestration.Api/Program.cs`
- Create: `backend/Orchestration.Tests/Api/Hubs/ActivityHubAuthorizationTests.cs`
- Create: `backend/Orchestration.Tests/Api/Hubs/ActivityHubAuthenticationExtensionsTests.cs`
- Create: `backend/Orchestration.Tests/Api/Hubs/SignalRActivityEventPublisherTests.cs`

- [ ] **Step 1: Write failing authorization and token-transport tests**

Assert `[Authorize]` exists on `ActivityHub`. Resolve `BearerTokenOptions` for `IdentityConstants.BearerScheme`, invoke `OnMessageReceived`, and cover:

```csharp
[Theory]
[InlineData("/hubs/activity", "signalr-token", "signalr-token")]
[InlineData("/hubs/activity/negotiate", "signalr-token", "signalr-token")]
[InlineData("/api/analysis-sessions", "signalr-token", null)]
public async Task Query_token_should_be_accepted_only_for_activity_hub(
    string path,
    string queryToken,
    string? expectedToken)
```

Also verify an `Authorization` header remains authoritative and a missing query token stays missing. Never include actual production tokens in fixtures.

- [ ] **Step 2: Add the restricted Identity bearer event**

Use post-configuration so the options target the opaque Identity bearer scheme:

```csharp
services.PostConfigure<BearerTokenOptions>(
    IdentityConstants.BearerScheme,
    options =>
    {
        var previous = options.Events.OnMessageReceived;
        options.Events.OnMessageReceived = async context =>
        {
            if (previous is not null)
            {
                await previous(context);
            }

            if (string.IsNullOrEmpty(context.Token)
                && context.Request.Path.StartsWithSegments("/hubs/activity")
                && context.Request.Query.TryGetValue("access_token", out var token))
            {
                context.Token = token;
            }
        };
    });
```

Do not parse the token as JWT. Identity API tokens are opaque Data Protection bearer tokens.

- [ ] **Step 3: Write failing publisher isolation tests**

Build a fake `IHubContext<ActivityHub>` whose `IHubClients` records calls. Seed two `AnalysisSession` rows and publish one event. Assert:

```csharp
hubClients.UserCalls.Should().ContainSingle()
    .Which.Should().Be(ownerId.ToString());
hubClients.AllCalls.Should().Be(0);
```

For an unknown session, assert the activity log is persisted but no broadcast occurs. For an ownership-query exception, assert no broad audience call occurs and the exception remains visible to the caller.

- [ ] **Step 4: Implement owner lookup and fail-closed delivery**

Add `[Authorize]` to `ActivityHub`. In `SignalRActivityEventPublisher`, after persistence, query:

```csharp
var ownerId = await _dbContext.AnalysisSessions
    .Where(session => session.Id == activityEvent.SessionId)
    .Select(session => (Guid?)session.UserId)
    .SingleOrDefaultAsync(cancellationToken);

if (ownerId is null)
{
    _logger.LogWarning(
        "Realtime activity delivery skipped because session ownership was not found.");
    return;
}

await _hubContext.Clients
    .User(ownerId.Value.ToString())
    .SendAsync("activityEventReceived", activityEvent, cancellationToken);
```

No `Clients.All`, client-selected group, or recipient parameter remains.

- [ ] **Step 5: Run focused backend tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~ActivityHub|FullyQualifiedName~SignalRActivityEventPublisherTests" --verbosity minimal
rg -n "Clients\.All|Clients\.Group" backend/Orchestration.Api
```

Expected: tests PASS; `rg` finds no realtime broad/group broadcast.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.Api/Hubs backend/Orchestration.Api/Program.cs backend/Orchestration.Tests/Api/Hubs
git commit -m "fix: isolate SignalR activity by user"
```

## Task 4: Send the Opaque Identity Token from the Frontend

**Files:**

- Create: `frontend/src/services/signalR.ts`
- Modify: `frontend/src/hooks/useSignalRConnection.ts`
- Modify: `frontend/src/App.tsx`
- Create: `frontend/tests/signalRConnection.test.ts`
- Modify: `frontend/package.json`

- [ ] **Step 1: Add a failing pure connection-options test**

Add a test script:

```json
"test": "node --test tests/*.test.ts"
```

Test:

```typescript
import assert from "node:assert/strict";
import test from "node:test";
import { createActivityHubConnectionOptions } from "../src/services/signalR.ts";

test("supplies the current opaque Identity access token", async () => {
  const options = createActivityHubConnectionOptions("opaque-token-value");

  assert.equal(await options.accessTokenFactory?.(), "opaque-token-value");
});
```

- [ ] **Step 2: Run the frontend test and verify the factory is absent**

Run:

```powershell
npm --prefix frontend test
```

Expected: FAIL because `createActivityHubConnectionOptions` does not exist.

- [ ] **Step 3: Extract the authenticated SignalR factory**

Implement:

```typescript
import type { IHttpConnectionOptions } from "@microsoft/signalr";

export function createActivityHubConnectionOptions(
  accessToken: string,
): IHttpConnectionOptions {
  return {
    accessTokenFactory: () => accessToken,
  };
}
```

Change the hook signature and builder:

```typescript
export function useSignalRConnection(
  accessToken: string,
  onActivityEvent: (event: ActivityEvent) => void,
) {
  // ...
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(
      `${apiBaseUrl}/hubs/activity`,
      createActivityHubConnectionOptions(accessToken),
    )
    .withAutomaticReconnect()
    .build();
  // ...
}
```

Include `accessToken` in the effect dependency list. In `AuthenticatedApp`, call:

```typescript
const connectionStatus = useSignalRConnection(token, addActivityEvent);
```

- [ ] **Step 4: Run frontend test and build**

Run:

```powershell
npm --prefix frontend test
npm --prefix frontend run build
```

Expected: PASS; TypeScript accepts opaque tokens without JWT decoding in the SignalR path.

- [ ] **Step 5: Commit**

```powershell
git add frontend/src/services/signalR.ts frontend/src/hooks/useSignalRConnection.ts frontend/src/App.tsx frontend/tests/signalRConnection.test.ts frontend/package.json
git commit -m "fix: authenticate frontend SignalR connection"
```

## Task 5: Validate Origins and Harden Render Proxy/WebSocket Handling

**Files:**

- Create: `backend/Orchestration.Api/Hosting/FrontendCorsOptions.cs`
- Create: `backend/Orchestration.Api/Hosting/FrontendCorsOptionsValidator.cs`
- Create: `backend/Orchestration.Api/Hosting/RenderProxyOptions.cs`
- Create: `backend/Orchestration.Api/Hosting/ProductionHostingExtensions.cs`
- Modify: `backend/Orchestration.Api/Program.cs`
- Modify: `backend/Orchestration.Api/appsettings.json`
- Modify: `backend/Orchestration.Api/appsettings.Development.example.json`
- Modify: `backend/Orchestration.Api/appsettings.ProductionLike.example.json`
- Create: `backend/Orchestration.Tests/Api/Hosting/FrontendCorsOptionsValidatorTests.cs`
- Create: `backend/Orchestration.Tests/Api/Hosting/ProductionHostingExtensionsTests.cs`

- [ ] **Step 1: Write failing CORS validation tests**

Matrix:

| Environment | Allowed origins | Expected |
|---|---|---|
| Development | empty | use `http://localhost:5173` |
| Production | empty | fail startup |
| Production | `http://frontend.example.com` | fail |
| Production | `https://frontend.example.com` | pass |
| Any | wildcard, path, query, fragment, relative URI | fail |
| Any | duplicated origin after normalization | fail |

An origin is valid only when `uri.GetLeftPart(UriPartial.Authority) == value.TrimEnd('/')` and `uri.Scheme` is HTTP or HTTPS.

- [ ] **Step 2: Add typed options and shared resolved origins**

Use:

```csharp
public sealed class FrontendCorsOptions
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; init; } = [];
}

public sealed class RenderProxyOptions
{
    public const string SectionName = "RenderProxy";
    public bool Enabled { get; init; }
}
```

Bind and validate on start. Development fallback is applied in one resolver and reused by both CORS and `WebSocketOptions.AllowedOrigins`.

- [ ] **Step 3: Write failing hosting registration tests**

Assert:

- CORS policy `Frontend` contains the resolved origins and allows credentials;
- `WebSocketOptions.AllowedOrigins` contains the same values;
- proxy disabled clears forwarded headers;
- proxy enabled uses only `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host`;
- proxy enabled clears default known networks/proxies for Render's changing proxy layer;
- no wildcard origin can coexist with credentials.

- [ ] **Step 4: Implement hosting registration and pipeline order**

Registration shape:

```csharp
services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(resolvedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

services.Configure<WebSocketOptions>(options =>
{
    foreach (var origin in resolvedOrigins)
    {
        options.AllowedOrigins.Add(origin);
    }
});
```

When `RenderProxy:Enabled=true`, configure `ForwardedHeadersOptions` explicitly. In `Program.cs`, middleware order begins:

```csharp
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseWebSockets();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
```

`UseForwardedHeaders` must precede HSTS/redirect and every middleware that reads scheme/host.

- [ ] **Step 5: Replace hard-coded configuration**

Base config:

```json
"Cors": {
  "AllowedOrigins": []
},
"RenderProxy": {
  "Enabled": false
}
```

Development example:

```json
"Cors": {
  "AllowedOrigins": [ "http://localhost:5173" ]
}
```

Production-like example uses `https://frontend.example.com` and enables `RenderProxy`.

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~FrontendCorsOptionsValidatorTests|FullyQualifiedName~ProductionHostingExtensionsTests" --verbosity minimal
rg -n "localhost:5173" backend/Orchestration.Api/Program.cs backend/Orchestration.Api/appsettings.json
```

Expected: tests PASS; the production code/default config has no hard-coded localhost origin.

- [ ] **Step 7: Commit**

```powershell
git add backend/Orchestration.Api/Hosting backend/Orchestration.Api/Program.cs backend/Orchestration.Api/appsettings*.json backend/Orchestration.Tests/Api/Hosting
git commit -m "fix: validate production origins and proxy headers"
```

## Task 6: Make Real CNV MCP Explicit and Required in Production

**Files:**

- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/CnvRegulationMcpOptions.cs`
- Modify: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/CnvRegulationMcpOptionsValidator.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/UnavailableRegulatoryKnowledgeSource.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/RegulatoryKnowledgeServiceCollectionExtensions.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/ICnvRegulationMcpProbe.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulationMcpProbe.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulationMcpHealthCheck.cs`
- Create: `backend/Orchestration.Infrastructure/Agents/Legal/Regulations/Mcp/CnvRegulationMcpStartupService.cs`
- Modify: `backend/Orchestration.Api/Program.cs`
- Modify: `backend/Orchestration.Api/appsettings.json`
- Modify: `backend/Orchestration.Tests/Agents/Legal/CnvRegulationMcpOptionsValidatorTests.cs`
- Create: `backend/Orchestration.Tests/Agents/Legal/RegulatoryKnowledgeRegistrationTests.cs`
- Create: `backend/Orchestration.Tests/Agents/Legal/UnavailableRegulatoryKnowledgeSourceTests.cs`
- Create: `backend/Orchestration.Tests/Agents/Legal/CnvRegulationMcpProbeTests.cs`
- Modify: `tools/CnvRegulation.McpServer/src/CnvRegulation.McpServer/Tools/CnvRegulationTools.cs`
- Modify: `tools/CnvRegulation.McpServer/tests/CnvRegulation.McpServer.Tests/CnvRegulationToolsRegistrationTests.cs`

- [ ] **Step 1: Write failing options and registration-matrix tests**

Add `Required` expectations:

```csharp
[Fact]
public void Validate_Should_reject_required_but_disabled()
{
    var result = validator.Validate(null, new CnvRegulationMcpOptions
    {
        Required = true,
        Enabled = false
    });

    result.Failed.Should().BeTrue();
}
```

Also reject enabled blank `Command`, empty `Args`, non-positive connection/tool timeouts, and `Required=true` with invalid executable configuration.

Registration matrix:

```csharp
[Theory]
[InlineData("Development", false, false, typeof(MockRegulatoryKnowledgeSource))]
[InlineData("Production", false, false, typeof(UnavailableRegulatoryKnowledgeSource))]
[InlineData("Production", true, true, typeof(McpRegulatoryKnowledgeSource))]
public void Registration_should_select_explicit_source(
    string environment,
    bool enabled,
    bool required,
    Type expectedType)
```

Add a separate assertion that required-but-disabled throws during option validation instead of registering any fallback.

- [ ] **Step 2: Implement `Required` and the source matrix**

Add:

```csharp
public bool Required { get; init; }
```

Registration rules:

```csharp
if (configured.Enabled)
{
    services.AddScoped<IRegulatoryKnowledgeSource, McpRegulatoryKnowledgeSource>();
}
else if (environment.IsDevelopment() || environment.IsEnvironment("Test"))
{
    services.AddScoped<IRegulatoryKnowledgeSource, MockRegulatoryKnowledgeSource>();
}
else
{
    services.AddScoped<IRegulatoryKnowledgeSource, UnavailableRegulatoryKnowledgeSource>();
}
```

`UnavailableRegulatoryKnowledgeSource` returns an explicit review-required/unavailable result using the existing cautious regulatory contracts. It must never manufacture citations or findings.

- [ ] **Step 3: Write failing startup/readiness probe tests**

The probe calls the real MCP client:

```csharp
await client.SearchAsync(
    new CnvRegulationSearchRequest(
        Query: "CNV",
        Limit: 1),
    cancellationToken);
```

Cover success with zero hits, success with one hit, timeout, transport exception, cancellation, disabled optional mode, and required startup failure. Assert health descriptions contain no query, command arguments, URI, credentials, MCP response body, or exception details.

- [ ] **Step 4: Implement one bounded probe reused by startup and health**

Use `ICnvRegulationMcpProbe.ProbeAsync`. `CnvRegulationMcpStartupService` executes it only when `Required=true`. `CnvRegulationMcpHealthCheck`:

- returns healthy without probing when MCP is optional and disabled;
- returns healthy for a completed real query even when hit count is zero;
- returns unhealthy with a fixed safe description on timeout/transport/database failure;
- preserves caller cancellation.

Register the health check only through the typed MCP extension:

```csharp
healthChecks.AddCheck<CnvRegulationMcpHealthCheck>(
    "cnv_mcp",
    tags: ["ready"]);
```

- [ ] **Step 5: Remove “mock” from real MCP tool descriptions**

Change only descriptions for PostgreSQL-capable tools. Preserve tool names, arguments, response contracts, and cautious legal wording. Update exact-string registration tests to require wording such as “recuperación documental CNV” and reject “mock”.

- [ ] **Step 6: Run focused backend and MCP tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~CnvRegulationMcpOptionsValidatorTests|FullyQualifiedName~RegulatoryKnowledgeRegistrationTests|FullyQualifiedName~UnavailableRegulatoryKnowledgeSourceTests|FullyQualifiedName~CnvRegulationMcpProbeTests" --verbosity minimal
dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.McpServer.Tests/CnvRegulation.McpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~CnvRegulationToolsRegistrationTests" --verbosity minimal
```

Expected: PASS; no non-development disabled path returns simulated legal evidence.

- [ ] **Step 7: Commit**

```powershell
git add backend/Orchestration.Infrastructure/Agents/Legal/Regulations backend/Orchestration.Api/Program.cs backend/Orchestration.Api/appsettings.json backend/Orchestration.Tests/Agents/Legal tools/CnvRegulation.McpServer/src/CnvRegulation.McpServer/Tools tools/CnvRegulation.McpServer/tests/CnvRegulation.McpServer.Tests
git commit -m "fix: require real CNV MCP in production"
```

## Task 7: Expose Safe Health, Gate Swagger, and Protect Diagnostics

**Files:**

- Modify: `backend/Orchestration.ServiceDefaults/Extensions.cs`
- Modify: `backend/Orchestration.Api/Program.cs`
- Modify: `backend/Orchestration.Api/Controllers/DiagnosticsController.cs`
- Modify: `backend/Orchestration.Api/Orchestration.Api.csproj`
- Create: `backend/Orchestration.Tests/Api/ProductionEndpointPolicyTests.cs`
- Modify: `backend/Orchestration.Tests/Api/DiagnosticsControllerTests.cs`

- [ ] **Step 1: Write failing endpoint-policy tests**

Add `Microsoft.AspNetCore.Mvc.Testing` version `10.0.7`, then use a test host with substituted database/MCP checks. Prove:

- Production `/alive` is anonymous and reports only HTTP status;
- Production `/health` is anonymous, returns 200 when ready and 503 when a required dependency is unhealthy;
- response body contains no check names, exception messages, connection strings, or secrets;
- Production `/swagger/index.html` returns 404;
- Development Swagger remains available;
- unauthenticated diagnostics return 401;
- authenticated diagnostics return only the existing safe status fields;
- `tool-calling/execute` remains 404 outside Development.

- [ ] **Step 2: Map health in every environment with a fixed writer**

Remove the Development guard from `MapDefaultEndpoints`:

```csharp
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = static async (context, report) =>
    {
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(
            report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy");
    }
});

app.MapHealthChecks("/alive", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = static async (context, report) =>
    {
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(
            report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy");
    }
});
```

Add the application database readiness check:

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrchestrationDbContext>(
        "orchestration_db",
        tags: ["ready"]);
```

- [ ] **Step 3: Gate Swagger and diagnostics**

In `Program.cs`:

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

Add `[Authorize]` to `DiagnosticsController`. Keep fixed status fields; do not add command, args, environment variables, or connection values.

- [ ] **Step 4: Make `Program` available to integration tests**

Append:

```csharp
public partial class Program;
```

This does not change runtime behavior and allows `WebApplicationFactory<Program>`.

- [ ] **Step 5: Run endpoint tests**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductionEndpointPolicyTests|FullyQualifiedName~DiagnosticsControllerTests" --verbosity minimal
```

Expected: PASS in Development and Production test hosts.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.ServiceDefaults/Extensions.cs backend/Orchestration.Api backend/Orchestration.Tests/Api
git commit -m "fix: harden production endpoints and health"
```

## Task 8: Build the Complete Non-Root Linux Image

**Required skill at task start:** `containerize-aspnetcore`.

**Files:**

- Create: `Dockerfile`
- Create: `.dockerignore`
- Modify: `.gitignore`
- Create: `progress.md`

- [ ] **Step 1: Record container prerequisites and exact artifact paths**

In `progress.md`, record:

- API target: `backend/Orchestration.Api/Orchestration.Api.csproj`, `net10.0`;
- MCP target: `tools/CnvRegulation.McpServer/src/CnvRegulation.McpServer/CnvRegulation.McpServer.csproj`, `net8.0`, `linux-x64`, self-contained;
- Python home: `/app/python/data_agent`;
- virtual environment: `/app/python/data_agent/.venv`;
- MCP command: `/app/mcp/CnvRegulation.McpServer`;
- HTTP binding: `http://0.0.0.0:10000`;
- runtime packages: `curl`, `poppler-utils`, `tesseract-ocr`, `tesseract-ocr-eng`, `tesseract-ocr-spa`.

- [ ] **Step 2: Create a failing image-content inspection script in `progress.md`**

Define the post-build commands before writing the Dockerfile:

```powershell
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "test -x /app/mcp/CnvRegulation.McpServer"
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "test -f /app/python/data_agent/requirements.lock"
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "tesseract --version && pdftoppm -v && python3.12 --version"
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "test $(id -u) -ne 0"
```

Expected before implementation: image does not exist.

- [ ] **Step 3: Implement multi-stage Docker build**

Required stages:

1. `mcr.microsoft.com/dotnet/sdk:10.0-bookworm-slim` restores and publishes the API.
2. The SDK stage publishes MCP with:

```dockerfile
RUN dotnet publish tools/CnvRegulation.McpServer/src/CnvRegulation.McpServer/CnvRegulation.McpServer.csproj \
    -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=true -p:DebugType=None \
    -o /artifacts/mcp
```

3. A Python staging stage installs `CSnakes.Stage` version `1.2.1`, stages Python 3.12, and installs `python-agents/data_agent/requirements.lock` into the target virtual environment:

```dockerfile
RUN dotnet tool install --tool-path /tools CSnakes.Stage --version 1.2.1
COPY python-agents/data_agent/requirements.lock /app/python/data_agent/requirements.lock
RUN /tools/setup-python \
    --python 3.12 \
    --venv /app/python/data_agent/.venv \
    --pip-requirements /app/python/data_agent/requirements.lock
```

Copy the staged `/root/.config/CSnakes` cache to `/home/app/.config/CSnakes` in the final image, copy the virtual environment without modification, and give `$APP_UID` ownership of both locations.
4. `mcr.microsoft.com/dotnet/aspnet:10.0-bookworm-slim` installs only runtime OS packages, copies published API/MCP/Python artifacts, changes ownership to `$APP_UID`, and runs as `$APP_UID`.

Final settings:

```dockerfile
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV Python__Home=/app/python/data_agent
EXPOSE 10000
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail --silent http://127.0.0.1:10000/alive || exit 1
ENTRYPOINT ["dotnet", "Orchestration.Api.dll"]
```

Do not copy `.env`, user secrets, local `.venv`, `bin`, `obj`, test results, or the SDK into the final stage.

- [ ] **Step 4: Create `.dockerignore`**

At minimum:

```text
.git
.github
.vs
.vscode
**/bin
**/obj
**/TestResults
**/node_modules
**/.venv
**/*.user
**/*.suo
**/.env
**/.env.*
.render-smoke.env
**/appsettings.Development.json
**/*.pdf.tmp
**/tmp
```

Keep `requirements.lock`, source projects, migrations, and MCP data-quality fixtures needed at build/test time.
Add `.render-smoke.env` to `.gitignore` as a second defense against committing local smoke-test credentials.

- [ ] **Step 5: Build and inspect the image**

Run:

```powershell
docker build --progress=plain -t ai-orchestration-hitl:local .
docker image inspect ai-orchestration-hitl:local --format "{{.Config.User}} {{json .Config.ExposedPorts}} {{json .Config.Healthcheck.Test}}"
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "test -x /app/mcp/CnvRegulation.McpServer"
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "test -f /app/python/data_agent/requirements.lock"
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "tesseract --version && pdftoppm -v && python3.12 --version"
docker run --rm --entrypoint /bin/sh ai-orchestration-hitl:local -c "test $(id -u) -ne 0"
```

Expected: build succeeds; only port 10000 is exposed; health command uses `/alive`; MCP is executable; Python/OCR tools exist; runtime UID is nonzero.

- [ ] **Step 6: Commit**

```powershell
git add Dockerfile .dockerignore .gitignore progress.md
git commit -m "build: add complete production container"
```

## Task 9: Declare Render Infrastructure and Production Flags

**Files:**

- Create: `render.yaml`
- Modify: `frontend/src/services/api.ts`
- Create: `frontend/tests/apiBaseUrl.test.ts`

- [ ] **Step 1: Write a failing frontend API URL test**

Prove build-time URL normalization removes a trailing slash and rejects an empty production value:

```typescript
assert.equal(
  normalizeApiBaseUrl("https://ai-orchestration-hitl-api.onrender.com/"),
  "https://ai-orchestration-hitl-api.onrender.com",
);
```

Keep local fallback only for development.

- [ ] **Step 2: Implement a root Blueprint**

Use this resource topology and fixed instance count:

```yaml
services:
  - type: web
    name: ai-orchestration-hitl-api
    runtime: docker
    plan: standard
    region: oregon
    numInstances: 1
    dockerfilePath: ./Dockerfile
    healthCheckPath: /health
    autoDeployTrigger: checksPass
    preDeployCommand: >-
      dotnet Orchestration.Api.dll --migrate-only &&
      /app/mcp/CnvRegulation.McpServer migrate-db
    envVars:
      - key: ASPNETCORE_ENVIRONMENT
        value: Production
      - key: RenderProxy__Enabled
        value: "true"
      - key: Cors__AllowedOrigins__0
        sync: false
      - key: ConnectionStrings__orchestrationdb
        fromDatabase:
          name: ai-orchestration-hitl-app-db
          property: connectionString
      - key: CNV_REGULATION_DB_CONNECTION_STRING
        fromDatabase:
          name: ai-orchestration-hitl-cnv-db
          property: connectionString
      - key: Mcp__CnvRegulation__Required
        value: "true"
      - key: Mcp__CnvRegulation__Enabled
        value: "true"
      - key: Mcp__CnvRegulation__Command
        value: /app/mcp/CnvRegulation.McpServer
      - key: Mcp__CnvRegulation__Args__0
        value: --storage
      - key: Mcp__CnvRegulation__Args__1
        value: postgres
      - key: ToolCalling__Enabled
        value: "true"
      - key: ToolCalling__ExecutionMode
        value: PlanDriven
      - key: ToolCalling__AllowedTools__0
        value: data.analyze_transactions
      - key: ToolCalling__AllowedTools__1
        value: legal.search_cnv_regulation
      - key: DataAgent__FinancialAnalysisToolsEnabled
        value: "true"
      - key: DataAgent__UsePythonFinancialAnalysis
        value: "true"
      - key: DataAgent__UseFixtureMetricsFallback
        value: "false"
      - key: DataAgent__RequireSessionFinancialMetrics
        value: "true"
      - key: DataAgent__AiReviewEnabled
        value: "false"
      - key: FinancialMetricsExtraction__MaxConcurrentConversions
        value: "1"
      - key: StructuredFinancialMetricsPdfExtraction__TesseractLanguage
        value: eng+spa
      - key: Llm__Enabled
        value: "false"
      - key: LegalAgent__AiReviewEnabled
        value: "false"

  - type: web
    name: ai-orchestration-hitl
    runtime: static
    buildCommand: npm ci && npm run build
    rootDir: frontend
    staticPublishPath: ./dist
    autoDeployTrigger: checksPass
    envVars:
      - key: VITE_API_URL
        sync: false
    routes:
      - type: rewrite
        source: /*
        destination: /index.html
    headers:
      - path: /*
        name: X-Content-Type-Options
        value: nosniff
      - path: /*
        name: Referrer-Policy
        value: strict-origin-when-cross-origin
      - path: /*
        name: X-Frame-Options
        value: DENY

databases:
  - name: ai-orchestration-hitl-app-db
    plan: basic-256mb
    region: oregon
    ipAllowList: []
  - name: ai-orchestration-hitl-cnv-db
    plan: basic-256mb
    region: oregon
    ipAllowList: []
```

The API uses Render `standard` rather than `starter`: the .NET API, Python worker, OCR process, and MCP child share one container, while `starter` provides only 512 MB RAM. Keep `numInstances: 1` until SignalR gains a backplane.

- [ ] **Step 3: Keep optional AI secrets explicit and absent**

Do not declare API keys with empty values. Add `sync: false` entries only when enabling the related subsystem:

```yaml
      - key: Llm__ApiKey
        sync: false
      - key: Llm__Model
        sync: false
```

For the first production profile, keep `Llm__Enabled=false`, `DataAgent__AiReviewEnabled=false`, and `LegalAgent__AiReviewEnabled=false`. Full-text CNV MCP retrieval remains active.

- [ ] **Step 4: Validate Blueprint and frontend**

Run:

```powershell
npm --prefix frontend test
npm --prefix frontend run build
render blueprints validate render.yaml
```

Expected: frontend tests/build PASS; Render CLI exits zero and reports the Blueprint valid. If CLI authentication is unavailable, validate against Render's published Blueprint JSON Schema and record that the authenticated CLI check remains an operator action.

- [ ] **Step 5: Commit**

```powershell
git add render.yaml frontend/src/services/api.ts frontend/tests/apiBaseUrl.test.ts
git commit -m "build: declare Render production topology"
```

## Task 10: Document First Deploy, Corpus Gate, Flags, and Rollback

**Files:**

- Create: `docs/deployment/render.md`
- Modify: `README.md`

- [ ] **Step 1: Write the exact first-deploy sequence**

Document:

1. create Blueprint in Render from `render.yaml`;
2. keep API and both PostgreSQL databases in `oregon`;
3. set `Cors__AllowedOrigins__0` to the final static-site HTTPS origin;
4. set frontend `VITE_API_URL` to the final API HTTPS origin;
5. allow pre-deploy application and CNV schema migrations to complete;
6. confirm target CNV database identity before any ingestion;
7. run one-time approved corpus ingestion;
8. inspect coverage and full-text search quality;
9. redeploy/start API with required MCP;
10. verify health, login, two-user SignalR isolation, Data analysis, CNV retrieval, and restart token survival.

- [ ] **Step 2: Document the non-automatic CNV corpus gate**

Provide commands using the deployed image and the confirmed CNV connection environment:

```powershell
/app/mcp/CnvRegulation.McpServer ingest --storage postgres --source-directory /confirmed/cnv-sources
/app/mcp/CnvRegulation.McpServer inspect-coverage --storage postgres
/app/mcp/CnvRegulation.McpServer validate-search-quality --storage postgres --mode full_text
```

State clearly:

- schema migration is safe to automate;
- corpus ingestion is a separate persistent mutation and requires explicit target confirmation;
- `/health` may be healthy with an empty corpus because it proves transport/database execution;
- production handoff is blocked until approved corpus coverage is non-empty and search-quality checks pass.

- [ ] **Step 3: Add a complete flag table**

Required Render flags:

```text
ASPNETCORE_ENVIRONMENT=Production
RenderProxy__Enabled=true
Cors__AllowedOrigins__0=https://ai-orchestration-hitl.onrender.com
ConnectionStrings__orchestrationdb=Render application database internal URI
CNV_REGULATION_DB_CONNECTION_STRING=Render CNV database internal URI
Mcp__CnvRegulation__Required=true
Mcp__CnvRegulation__Enabled=true
Mcp__CnvRegulation__Command=/app/mcp/CnvRegulation.McpServer
Mcp__CnvRegulation__Args__0=--storage
Mcp__CnvRegulation__Args__1=postgres
ToolCalling__Enabled=true
ToolCalling__ExecutionMode=PlanDriven
ToolCalling__AllowedTools__0=data.analyze_transactions
ToolCalling__AllowedTools__1=legal.search_cnv_regulation
Llm__Enabled=false
DataAgent__AiReviewEnabled=false
DataAgent__FinancialAnalysisToolsEnabled=true
DataAgent__UsePythonFinancialAnalysis=true
DataAgent__UseFixtureMetricsFallback=false
DataAgent__RequireSessionFinancialMetrics=true
FinancialMetricsExtraction__MaxConcurrentConversions=1
StructuredFinancialMetricsPdfExtraction__TesseractLanguage=eng+spa
LegalAgent__AiReviewEnabled=false
IdentityBootstrap__Enabled=false
VITE_API_URL=https://ai-orchestration-hitl-api.onrender.com
```

Explain that `AllowedTools` is an array replaced by provider configuration, so all production entries must be declared. Explain that MCP source selection is bootstrap-scoped and flag changes require process restart.

- [ ] **Step 4: Add secrets and rollback rules**

Document:

- never put database URI, account password, or AI key in Git;
- use `sync: false` or Render database references;
- bootstrap values are optional and only supplied through Render secrets;
- rollback application image and schema only through compatible migrations;
- never roll back by replacing CNV corpus automatically;
- keep API at one instance until a SignalR backplane exists;
- inspect Render deploy/pre-deploy logs without copying secret-bearing environment output.

- [ ] **Step 5: Link the runbook from README and lint**

Run:

```powershell
rg -n "Password1!|postgresql://[^ ]+:[^ ]+@|sk-[A-Za-z0-9]" README.md docs/deployment/render.md render.yaml
git diff --check
```

Expected: no secret-like matches; diff check exits zero.

- [ ] **Step 6: Commit**

```powershell
git add README.md docs/deployment/render.md
git commit -m "docs: add Render production runbook"
```

## Task 11: Prove Cross-Boundary Behavior with Disposable Services

**Required skills at task start:** `aihitl-live-e2e-verification`, `aihitl-cnv-mcp-quality-gate`.

**Files:**

- Modify: `backend/Orchestration.Tests/Orchestration.Tests.csproj`
- Create: `backend/Orchestration.Tests/Integration/ProductionSignalRIsolationTests.cs`
- Create: `backend/Orchestration.Tests/Integration/RequiredMcpReadinessTests.cs`
- Modify: `progress.md`

- [ ] **Step 1: Add only the integration packages required**

Add versions aligned with .NET 10:

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.7" />
<PackageReference Include="Testcontainers.PostgreSql" Version="4.13.0" />
```

`Microsoft.AspNetCore.Mvc.Testing` was added in Task 7. Keep every package on an exact, non-floating version.

- [ ] **Step 2: Write the two-user SignalR isolation test**

Against a Production `WebApplicationFactory` and disposable application PostgreSQL:

1. register user A and user B through `/api/auth/register`;
2. login both and retain opaque access tokens;
3. connect two `HubConnection` clients with `AccessTokenProvider`;
4. create a session as user A;
5. publish an activity event for A's session;
6. assert A receives exactly that event;
7. assert B receives nothing during a bounded two-second window;
8. attempt anonymous hub connection and assert 401/handshake failure.

Do not decode opaque tokens or depend on hard-coded user IDs.

- [ ] **Step 3: Write the required MCP readiness test**

Against disposable CNV PostgreSQL:

1. run `/app/mcp/CnvRegulation.McpServer migrate-db` or the project executable equivalent;
2. start the API with `Required=true`, real self-contained/current-platform MCP command, and `--storage postgres`;
3. call `/health`;
4. assert healthy for an empty migrated corpus;
5. break the CNV connection and assert `/health` becomes 503 with only `Unhealthy`;
6. assert startup fails when the MCP command path is invalid.

Persistent repository integration tests remain opt-in unless the disposable connection variable is set.

- [ ] **Step 4: Run integration proof**

Run:

```powershell
dotnet test backend/Orchestration.Tests/Orchestration.Tests.csproj --no-restore --filter "Category=ProductionIntegration" --verbosity normal
```

Expected: PASS when Docker/disposable PostgreSQL is available. If Docker is unavailable, record the exact environment limitation in `progress.md`, run all non-container tests, and leave this acceptance item open.

- [ ] **Step 5: Run the built container against disposable databases**

Start application and CNV PostgreSQL containers, then:

```powershell
$containerId = docker run --detach --rm --name ai-orchestration-hitl-smoke --network ai-hitl-smoke -p 10000:10000 --env-file .render-smoke.env ai-orchestration-hitl:local
try {
    curl.exe --fail http://127.0.0.1:10000/alive
    curl.exe --fail http://127.0.0.1:10000/health
}
finally {
    docker stop ai-orchestration-hitl-smoke
    Remove-Item -LiteralPath (Resolve-Path .render-smoke.env)
}
```

The local `.render-smoke.env` file is untracked and deleted after the run. Never print it.

- [ ] **Step 6: Commit**

```powershell
git add backend/Orchestration.Tests progress.md
git commit -m "test: prove production isolation and readiness"
```

## Task 12: Run the Complete Production Gate

**Required skill at task start:** `verification-before-completion`.

**Files:**

- Modify only if verification exposes a scoped defect.

- [ ] **Step 1: Restore and build all .NET projects**

Run:

```powershell
dotnet restore backend/Orchestration.slnx
dotnet restore tools/CnvRegulation.McpServer/CnvRegulation.McpServer.sln
dotnet build backend/Orchestration.slnx --no-restore --configuration Release
dotnet build tools/CnvRegulation.McpServer/CnvRegulation.McpServer.sln --no-restore --configuration Release
```

Expected: zero warnings treated as errors by existing project policy; zero errors.

- [ ] **Step 2: Run all backend and MCP tests**

Run:

```powershell
dotnet test backend/Orchestration.slnx --no-build --configuration Release --verbosity minimal
dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.Application.Tests/CnvRegulation.Application.Tests.csproj --configuration Release --verbosity minimal
dotnet test tools/CnvRegulation.McpServer/tests/CnvRegulation.McpServer.Tests/CnvRegulation.McpServer.Tests.csproj --configuration Release --verbosity minimal
```

Expected: all non-opt-in tests PASS; integration skips are named and justified.

- [ ] **Step 3: Run Python and frontend gates**

Run:

```powershell
python -m pytest python-agents/data_agent/tests
npm --prefix frontend ci
npm --prefix frontend test
npm --prefix frontend run build
```

Expected: Python tests PASS; frontend tests/build PASS.

- [ ] **Step 4: Repeat deployment artifact proof**

Run:

```powershell
docker build --progress=plain -t ai-orchestration-hitl:production-gate .
render blueprints validate render.yaml
docker image inspect ai-orchestration-hitl:production-gate --format "{{.Config.User}} {{json .Config.ExposedPorts}} {{json .Config.Healthcheck.Test}}"
```

Expected: image builds; Blueprint validates; image is non-root and exposes only 10000.

- [ ] **Step 5: Scan production invariants**

Run:

```powershell
rg -n "Password1!|admin@ezemartino\.com|user@ezemartino\.com" .
rg -n "Clients\.All|Clients\.Group" backend/Orchestration.Api
rg -n "MockRegulatoryKnowledgeSource" backend/Orchestration.Api backend/Orchestration.Infrastructure
rg -n "UseSwagger|UseSwaggerUI|MapHealthChecks|UseForwardedHeaders" backend
git diff --check
git status --short
```

Expected:

- no committed account/password matches;
- no broad SignalR publishing;
- mock registration is limited to explicit Development/test logic;
- Swagger is environment-gated;
- health is mapped in all environments;
- forwarded headers precede HTTPS middleware;
- no whitespace errors;
- only intentional plan implementation changes exist.

- [ ] **Step 6: Verify branch ancestry and final diff**

Run:

```powershell
git merge-base --is-ancestor main HEAD
git log --oneline --decorate main..HEAD
git diff --stat main...HEAD
git diff --check main...HEAD
```

Expected: branch contains current `main`; commits map cleanly to the tasks; no unrelated changes.

- [ ] **Step 7: Record production decision**

Update `progress.md` with:

- exact commands and exit codes;
- test counts and named skips;
- Docker and Blueprint result;
- two-user isolation result;
- MCP empty-corpus transport result;
- approved corpus coverage result;
- any remaining operator-only Render step.

Production status is **GO** only when all automated gates pass and the manual approved-corpus gate is complete. It is **CONDITIONAL GO** when code/image gates pass but Render provisioning or corpus approval remains an explicit operator action. It is **NO-GO** for identity leakage, cross-user SignalR delivery, invalid CORS/proxy behavior, unhealthy required MCP, missing database migration, failed image build, or failed required tests.

- [ ] **Step 8: Commit verification evidence if `progress.md` changed**

```powershell
git add progress.md
git commit -m "chore: record production verification evidence"
```

## Final Review Checklist

- [ ] Each approved blocker maps to at least one failing-first test and one focused commit.
- [ ] No committed bootstrap credential, fixed bootstrap identity, or silent password repair remains.
- [ ] Database migration runs independently in startup fallback and `--migrate-only`.
- [ ] Data Protection keys persist in application PostgreSQL under stable application name.
- [ ] SignalR hub requires authentication and query tokens are accepted only on `/hubs/activity`.
- [ ] Realtime activity reaches only `AnalysisSession.UserId`; missing ownership broadens nothing.
- [ ] CORS and WebSocket allowed origins share one validated source.
- [ ] Render proxy headers run before HSTS/HTTPS redirection.
- [ ] `/alive` and `/health` are available in Production and disclose only aggregate status.
- [ ] Swagger is Development-only; diagnostics require authentication.
- [ ] Production disabled MCP is unavailable, not simulated; required invalid MCP fails startup.
- [ ] Required MCP health executes a real bounded stdio/PostgreSQL read.
- [ ] Docker image includes API, Python 3.12 environment, OCR tools, and self-contained MCP.
- [ ] Docker final process is non-root and exposes only port 10000.
- [ ] Blueprint keeps one API instance and two private PostgreSQL databases.
- [ ] Render PostgreSQL URIs normalize in both API and MCP without secret logging.
- [ ] `AllowedTools` production array contains both approved entries.
- [ ] LLM and AI-review flags remain disabled without valid provider secrets.
- [ ] CNV schema migration is automated; corpus ingestion stays explicit and target-confirmed.
- [ ] Empty corpus can pass technical readiness but cannot pass production handoff.
- [ ] Full backend, MCP, Python, frontend, Docker, Blueprint, and diff gates have fresh evidence.

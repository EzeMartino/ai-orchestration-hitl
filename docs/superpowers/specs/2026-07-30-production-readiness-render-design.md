# Production Readiness and Render Deployment Design

**Date:** 2026-07-30

**Status:** Approved design

## Problem

The application is functionally close to a production candidate, but six
cross-cutting gaps make a public deployment unsafe or operationally
inconsistent:

1. startup creates known users with a password committed in source;
2. the SignalR hub is anonymous and publishes every activity event to every
   connected client;
3. CORS accepts only the local Vite origin;
4. the repository has no Linux container or Render Blueprint that includes the
   API, Python runtime, OCR tools, and CNV MCP executable;
5. readiness endpoints are development-only, proxy headers are not processed,
   and Swagger is public in every environment;
6. disabling CNV MCP silently selects simulated regulatory output.

The target is a pragmatic first production deployment on Render. It does not
need every enterprise control, but it must preserve tenant privacy, avoid
simulated legal evidence, survive normal redeploys, expose meaningful health
state, and fail clearly when required production dependencies are unavailable.

## Goals

- Remove committed bootstrap credentials and keep database migration
  independent from optional account creation.
- Authenticate SignalR and deliver activity only to the owner of the analysis
  session.
- Make HTTP and WebSocket origins explicit, validated configuration.
- Produce a reproducible Linux image and a Render Blueprint for the API,
  frontend, application PostgreSQL database, and CNV PostgreSQL database.
- Run the existing Python financial-analysis stack, PDF/OCR stack, and CNV MCP
  server from the production image.
- Expose public liveness/readiness endpoints with no sensitive details.
- Process Render proxy headers before HTTPS-sensitive middleware.
- Make real CNV MCP mandatory in the Render production profile and prohibit
  simulated regulatory evidence in production.
- Preserve existing Planner flag semantics, including full replacement of
  `AllowedTools` by the highest-precedence configuration provider.
- Add focused tests and fresh build/runtime proof proportionate to the change.

## Non-goals

- No automatic CNV corpus replacement, ingestion, or embedding rebuild against
  an existing database.
- No automatic promotion from full-text search to hybrid or semantic search.
- No conclusion that retrieved CNV evidence establishes applicability,
  compliance, non-compliance, breach, or legal risk.
- No conversion of the stdio MCP server to an HTTP service.
- No SignalR scale-out backplane in this iteration. The Render Blueprint keeps
  the API at one instance; scaling beyond one instance requires a backplane.
- No full identity-provider replacement, MFA project, email delivery, billing,
  or organization/role model.
- No unrelated dependency upgrade or broad application refactor.
- No production deployment, database mutation, secret creation, push, or pull
  request as part of this branch unless separately requested.

## Approved Architecture

### Render topology

The production topology contains:

- one paid Docker web service for the ASP.NET Core API;
- one Render static site for the Vite frontend;
- one managed PostgreSQL database for application/Identity/session data;
- one managed PostgreSQL database for the CNV regulatory corpus;
- one CNV MCP child process per API instance, launched over stdio.

API and MCP stay in the same image. This matches the existing
`CnvRegulationStdioMcpClient`, avoids exposing MCP over the network, and keeps
API/MCP versions atomic. The MCP project is published as a self-contained
Linux executable so the .NET 10 API image does not need a second shared .NET
runtime.

The frontend and API are separate origins. Their public URLs are supplied
explicitly during Blueprint creation because Render does not expose a
Blueprint property that automatically provides a static site's public URL to
another service.

### Production data flow

```text
Browser
  -> Render static frontend
  -> HTTPS API request / SignalR connection
  -> Render proxy
  -> forwarded-header processing
  -> configured origin check
  -> Identity bearer authentication
  -> controllers / authenticated ActivityHub
  -> application PostgreSQL
  -> API launches local CNV MCP executable over stdio
  -> MCP queries CNV PostgreSQL over Render private networking
```

No MCP port is opened. The only public container port is the API HTTP port.

## 1. Database Migration and Optional Identity Bootstrap

`IdentityDataSeeder` currently combines two responsibilities: applying EF Core
migrations and creating/repairing known users. These responsibilities will be
split.

### Database migration

A dedicated startup service applies application EF Core migrations. Migration
does not depend on Identity bootstrap being enabled. It runs before services
that require the database.

For a paid Render service, the preferred production path is a
`preDeployCommand` so a failed migration prevents the new revision from
replacing the healthy revision. Startup migration remains idempotent as a
fallback for environments that do not support pre-deploy commands.
The API exposes a bounded `--migrate-only` process mode that resolves the same
migrator, applies migrations, and exits without opening the HTTP listener.

### Identity bootstrap

Known emails, IDs, password hashes, and `Password1!` are removed from source.
Bootstrap is disabled by default. If explicitly enabled, all account values
come from validated configuration/secrets:

```text
IdentityBootstrap__Enabled
IdentityBootstrap__Users__0__Email
IdentityBootstrap__Users__0__Password
IdentityBootstrap__Users__0__EmailConfirmed
```

Missing or invalid values make bootstrap fail before creating a partial
account. Passwords are never logged. Existing passwords are never silently
replaced. The public Identity registration endpoint remains unchanged; it is
the normal way to create the first account when bootstrap is disabled.

Development can opt into bootstrap through user-secrets or environment
variables. No development password is committed to JSON.

## 2. Authenticated, User-Scoped SignalR

`ActivityHub` requires authorization. The JavaScript client supplies the
current Identity access token with `accessTokenFactory`.

ASP.NET Core Identity API bearer tokens are opaque rather than JWTs. The
Identity bearer handler will be configured through its
`OnMessageReceived` event to accept the `access_token` query parameter only
when the request path starts with `/hubs/activity`. This supports browser
WebSocket/SSE limitations without accepting query-string tokens on ordinary API
routes.

The publisher resolves `AnalysisSession.UserId` for the event's `SessionId`
and sends through
`IHubContext<ActivityHub>.Clients.User(userId.ToString())`. SignalR's
default user identifier is the authenticated name-identifier claim. No client
can choose the recipient or subscribe to another user's group.

Fail-closed behavior:

- missing session ownership means no realtime broadcast;
- an ownership lookup failure does not fall back to `Clients.All`;
- persisted activity remains subject to the existing authenticated,
  owner-scoped REST endpoint;
- token values and query strings are not written to application logs.

CORS protects negotiation/HTTP transports. The same configured origin list is
also applied to WebSockets because CORS alone does not enforce WebSocket
origins.

The Blueprint fixes `numInstances: 1`. A future multi-instance deployment must
add a SignalR backplane before increasing that count.

## 3. Validated Origin Configuration

A typed `FrontendCorsOptions` section replaces the hard-coded Vite URL:

```json
{
  "Cors": {
    "AllowedOrigins": []
  }
}
```

Rules:

- Production requires at least one origin.
- Each value must be an absolute HTTP/HTTPS origin with no path, query, or
  fragment.
- Wildcards are rejected.
- Production Render origins must use HTTPS.
- Development defaults may include `http://localhost:5173`.
- Credentials, required by the authenticated SignalR flow, remain enabled.

The Render value is supplied as
`Cors__AllowedOrigins__0=https://{frontend-public-host}`. The frontend receives
`VITE_API_URL=https://{api-public-host}` at build time.

## 4. Linux Container and Render Blueprint

### Image contents

The root `Dockerfile` uses multi-stage builds and contains:

- the published .NET 10 API;
- the self-contained Linux CNV MCP executable;
- Python 3.12 and the locked virtual environment required by CSnakes;
- `python-agents/data_agent`;
- Poppler and Tesseract packages/languages used by PDF extraction;
- `curl` for the container health check;
- no SDK, source tree, test output, local virtual environment, or secrets in
  the final stage.

The final process runs as the image's non-root `$APP_UID`. The API binds to
`0.0.0.0:10000`, Render's default web-service port. The image exposes only
port `10000`.

A root `.dockerignore` excludes `.git`, build output, test results,
`node_modules`, local virtual environments, IDE state, secrets, and temporary
PDF/OCR artifacts.

The container health check calls `/alive`; Render's deployment health check
uses `/health`.

### Blueprint

`render.yaml` defines:

- API: paid Docker web service, one instance, `/health`, Dockerfile at repo
  root;
- frontend: Node build producing `frontend/dist`, SPA rewrite to
  `index.html`, security headers, explicit `VITE_API_URL`;
- application PostgreSQL;
- CNV PostgreSQL with pgvector-compatible PostgreSQL;
- internal database references and required application flags;
- secret values with `sync: false`, never hard-coded.

Render supplies PostgreSQL references as `postgresql://...` URIs while Npgsql
expects keyword/value connection strings. Small, tested adapters normalize
Render PostgreSQL URIs before either database consumer builds an Npgsql
connection. Normalized values and credentials are never logged.

The CNV schema migration runs before deployment. Corpus ingestion remains a
separate, documented one-time operation because it mutates persistent
regulatory state and must be executed only against an explicitly confirmed
target. Transport/database readiness can pass against an empty corpus, but
production acceptance and handoff cannot: the operational gate separately
requires approved, non-empty corpus coverage.

### Data-protection keys

Identity bearer tokens use ASP.NET Core Data Protection. Keys will be persisted
in the application PostgreSQL database with a stable application name so
tokens survive Render's ephemeral filesystem, redeploys, and replacement
instances. Keys are not written to the image.

## 5. Proxy, Health, Swagger, and Diagnostics

Forwarded headers run first in the middleware pipeline. Production trusts
forwarded scheme/host data only under the explicit Render-proxy deployment
configuration. HTTPS redirection and HSTS run after forwarded headers, avoiding
redirect loops behind TLS termination.

Endpoint behavior:

- `/alive`: liveness/self check only;
- `/health`: readiness for the application database and required CNV MCP;
- health responses expose status only, not connection strings, exception
  details, queries, corpus contents, or secrets;
- Swagger and Swagger UI exist only in Development;
- diagnostic API endpoints require authentication and never expose secret
  values.

The MCP readiness check performs a bounded, read-only full-text query that
forces transport startup and a real PostgreSQL connection. An empty result is
not itself a transport failure, but production acceptance separately requires
non-empty approved corpus coverage.

## 6. Required Real CNV MCP

`CnvRegulationMcpOptions` gains `Required`. Registration rules are:

| Environment/configuration | Regulatory source |
|---|---|
| `Enabled=true` | `McpRegulatoryKnowledgeSource` |
| Development/test and explicitly disabled | `MockRegulatoryKnowledgeSource` |
| Non-development, disabled, not required | explicit unavailable source |
| `Required=true`, disabled or invalid | startup failure |

The Render profile sets both `Required=true` and `Enabled=true`. It uses:

```text
Mcp__CnvRegulation__Required=true
Mcp__CnvRegulation__Enabled=true
Mcp__CnvRegulation__Command=/app/mcp/CnvRegulation.McpServer
Mcp__CnvRegulation__Args__0=--storage
Mcp__CnvRegulation__Args__1=postgres
CNV_REGULATION_DB_CONNECTION_STRING={internal Render PostgreSQL URI}
```

Validation requires a non-empty executable and argument list when enabled.
Required mode adds a startup probe for executable launch, MCP handshake,
read-only tool execution, and CNV database access. The MCP capability remains
bootstrap-scoped: changing these flags requires a process restart.

The Planner production profile fully declares:

```text
ToolCalling__Enabled=true
ToolCalling__ExecutionMode=PlanDriven
ToolCalling__AllowedTools__0=data.analyze_transactions
ToolCalling__AllowedTools__1=legal.search_cnv_regulation
```

`Llm__Enabled` and `LegalAgent__AiReviewEnabled` are independent. They stay
disabled unless a real provider/model/key is configured; neither is required
for full-text CNV MCP retrieval.

The MCP server's tool descriptions will stop describing PostgreSQL-backed
retrieval as “mock.” Existing cautious semantics remain: citations are
documentary evidence, not proof of applicability or a legal conclusion.

## Error Handling

- Invalid bootstrap, CORS, proxy, MCP, or database configuration fails with a
  concise option name and no secret value.
- Required MCP startup/health failure makes readiness unhealthy and prevents
  the Render revision from becoming active.
- MCP tool timeout/reset behavior remains bounded by the existing connection
  and tool-call timeouts.
- No MCP failure falls back to simulated production findings.
- Missing session ownership never broadens a SignalR audience.
- Invalid Render PostgreSQL URI fails before constructing a partially valid
  connection string.
- Corpus migration and corpus ingestion remain distinct operations.

## Testing and Verification

Implementation follows test-driven development.

### Backend focused tests

- bootstrap disabled/default behavior, validation, and no password replacement;
- migration service independence from bootstrap;
- CORS and WebSocket origin validation;
- opaque bearer token extraction restricted to the hub path;
- unauthorized hub rejection;
- SignalR publisher targets only the session owner's user identifier and never
  `Clients.All`;
- missing-session fail-closed behavior;
- Render PostgreSQL URI normalization without secret-bearing test output;
- forwarded-header/middleware and environment-gated Swagger behavior;
- liveness/readiness registration and sanitized responses;
- MCP required/disabled/invalid registration matrix;
- MCP startup/readiness probe success, timeout, and transport/database failure;
- production never resolves `MockRegulatoryKnowledgeSource`;
- tool/allowlist registration remains consistent.

### MCP tests

- self-contained executable arguments select PostgreSQL storage;
- canonical document/article services continue using the configured repository;
- tool descriptions no longer claim real retrieval is mock;
- existing MCP build/unit tests remain green;
- PostgreSQL integration tests run only against a separately identified,
  disposable test database.

### Frontend tests

- SignalR connection supplies the current access token;
- token changes rebuild the connection;
- API URL remains environment driven;
- existing TypeScript/build tests remain green.

### Deployment proof

- full backend build/test;
- full frontend build/test;
- Python test suite;
- MCP build/test with persistent integration tests skipped unless a dedicated
  disposable database is explicitly configured;
- `docker build`;
- container startup as non-root;
- `/alive` and `/health` behavior against disposable databases;
- authenticated two-user SignalR isolation smoke;
- MCP read-only smoke through API -> stdio -> disposable PostgreSQL;
- `git diff --check`;
- review of the final diff against all six goals and non-goals.

If local Docker is unavailable, this is reported explicitly; image validity is
not inferred from ordinary project builds.

## Operational Runbook

Before the first Render production activation:

1. provision both databases in the same Render region as the API;
2. provide frontend/API public URLs and any secrets prompted by the Blueprint;
3. run the application and CNV schema migrations;
4. explicitly identify and authorize the new CNV database target;
5. ingest the approved corpus once;
6. inspect corpus coverage and verify full-text search quality;
7. start/redeploy the API with required MCP enabled;
8. confirm `/health`, login, two-user SignalR isolation, Data analysis, CNV
   citations, canonical enrichment, persistence, and human-review behavior.

Any later corpus replacement, embedding generation/rebuild, hybrid promotion,
production deployment, or secret rotation remains a separately authorized
operation.

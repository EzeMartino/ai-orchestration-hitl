# Multi-User Authentication and Session Isolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Secure the application using Microsoft Identity and JWT token authentication, isolating all analysis sessions strictly per user so that each user has a private personal dashboard.

**Architecture:** Update the database to inherit from `IdentityDbContext`, add a non-nullable `UserId` foreign key to `AnalysisSession`, expose standard `/api/auth` endpoints using Minimal API identity mapping, protect the controller with `[Authorize]`, filter EF Core database queries by the current authenticated user claims, and build a beautiful, premium React Login/Register flow with bearer fetch wrappers.

**Tech Stack:** ASP.NET Core Identity (.NET 10), EF Core, Npgsql PostgreSQL, JWT Bearer Token, React, TypeScript, Vanilla CSS.

---

### Task 1: Update Database Context & AnalysisSession Entity

**Files:**
*   Modify: `backend/Orchestration.Domain/AnalysisSessions/AnalysisSession.cs`
*   Modify: `backend/Orchestration.Application/Persistence/IOrchestrationDbContext.cs`
*   Modify: `backend/Orchestration.Infrastructure/Persistence/OrchestrationDbContext.cs`
*   Test: `backend/Orchestration.Tests/Domain/AnalysisSessionTests.cs` (Create if missing)

- [ ] **Step 1: Write test for AnalysisSession UserId association**
  Create or update a domain unit test checking that creating a session requires a UserId.
  ```csharp
  [Fact]
  public void Create_Should_Set_UserId()
  {
      var userId = Guid.NewGuid();
      var session = AnalysisSession.Create(userId);
      Assert.Equal(userId, session.UserId);
  }
  ```

- [ ] **Step 2: Run domain unit test to verify compilation failure**
  Run: `dotnet test --filter "FullyQualifiedName~AnalysisSessionTests"`
  Expected: Compile error (no `UserId` property or constructor argument).

- [ ] **Step 3: Modify AnalysisSession Domain Entity**
  Add the property and update `Create`:
  ```csharp
  public Guid UserId { get; private set; }

  public static AnalysisSession Create(Guid userId)
  {
      var now = DateTimeOffset.UtcNow;
      return new AnalysisSession
      {
          Id = Guid.NewGuid(),
          UserId = userId,
          Status = AnalysisSessionStatus.Pending,
          ContextJson = "{}",
          CreatedAt = now,
          UpdatedAt = now
      };
  }
  ```

- [ ] **Step 4: Update DbContext inheritance & EF Core Mapping**
  *   Modify `IOrchestrationDbContext.cs` to add namespaces if necessary.
  *   Modify `OrchestrationDbContext.cs` to inherit from `IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>` instead of `DbContext`.
  *   Add standard configuration inside `OnModelCreating`:
  ```csharp
  base.OnModelCreating(modelBuilder); // Critical for Identity tables mappings

  modelBuilder.Entity<AnalysisSession>(builder =>
  {
      builder.HasOne<IdentityUser<Guid>>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .IsRequired()
             .OnDelete(DeleteBehavior.Restrict);
  });
  ```

- [ ] **Step 5: Run tests and commit**
  Verify the new domain test passes.
  Run: `dotnet test`
  Expected: PASS.
  Commit: `git add . && git commit -m "feat(db): integrate IdentityDbContext and add UserId to AnalysisSession"`

---

### Task 2: Add Database Migration and Data Seeding

**Files:**
*   Create: `backend/Orchestration.Infrastructure/Persistence/IdentityDataSeeder.cs`
*   Modify: `backend/Orchestration.Api/Program.cs`

- [ ] **Step 1: Create Entity Framework Core database migration**
  Run EF command to generate a new migration:
  Run: `dotnet ef migrations add AddUserAuthenticationAndIsolation --project Orchestration.Infrastructure --startup-project Orchestration.Api`
  Expected: Successfully generated migration files in `Orchestration.Infrastructure/Persistence/Migrations/`.

- [ ] **Step 2: Customize migration to populate legacy records**
  Open the newly generated migration file `*_AddUserAuthenticationAndIsolation.cs` and insert a custom SQL command in `Up()` right after adding the nullable `UserId` column and before altering it to `NOT NULL`:
  ```csharp
  // Custom seed migration step
  migrationBuilder.Sql("INSERT INTO \"AspNetUsers\" (\"Id\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PasswordHash\", \"SecurityStamp\", \"ConcurrencyStamp\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\") VALUES ('00000000-0000-0000-0000-000000000001', 'admin@ezemartino.com', 'ADMIN@EZEMARTINO.COM', 'admin@ezemartino.com', 'ADMIN@EZEMARTINO.COM', true, 'AQAAAAIAAYagAAAAEI...', 'SECRETSTAMP', 'CONCURRENCYSTAMP', false, false, false, 0) ON CONFLICT DO NOTHING;");
  migrationBuilder.Sql("UPDATE \"AnalysisSessions\" SET \"UserId\" = '00000000-0000-0000-0000-000000000001' WHERE \"UserId\" IS NULL OR \"UserId\" = '00000000-0000-0000-0000-000000000000';");
  ```
  *(Note: PasswordHash is standard hashed Password1! but will also be reinforced in dynamic seeder.)*

- [ ] **Step 3: Implement startup Hosted Seeding Service**
  Create `backend/Orchestration.Infrastructure/Persistence/IdentityDataSeeder.cs`:
  ```csharp
  using Microsoft.AspNetCore.Identity;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Hosting;
  
  namespace Orchestration.Infrastructure.Persistence;
  
  public class IdentityDataSeeder(IServiceProvider serviceProvider) : IHostedService
  {
      public async Task StartAsync(CancellationToken cancellationToken)
      {
          using var scope = serviceProvider.CreateScope();
          var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
          
          var adminEmail = "admin@ezemartino.com";
          var adminUser = await userManager.FindByEmailAsync(adminEmail);
          
          if (adminUser is null)
          {
              adminUser = new IdentityUser<Guid>
              {
                  Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                  UserName = adminEmail,
                  Email = adminEmail,
                  EmailConfirmed = true
              };
              
              await userManager.CreateAsync(adminUser, "Password1!");
          }
      }
      public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
  }
  ```

- [ ] **Step 4: Register Hosted Service and Run Migration**
  Register in `Program.cs`:
  ```csharp
  builder.Services.AddHostedService<IdentityDataSeeder>();
  ```
  Run EF Core Database Update:
  Run: `dotnet ef database update --project Orchestration.Infrastructure --startup-project Orchestration.Api`
  Expected: Successful database upgrade on local Postgres server.
  Commit: `git add . && git commit -m "feat(seeding): add EF database migrations and identity admin seeder"`

---

### Task 3: Map Identity API Endpoints and Enable Authentication

**Files:**
*   Modify: `backend/Orchestration.Api/Program.cs`
*   Modify: `backend/Orchestration.Api/Controllers/AnalysisSessionController.cs`
*   Test: `backend/Orchestration.Tests/Api/AnalysisSessionsControllerSecurityTests.cs` (Create if missing)

- [ ] **Step 1: Write failing controller security integration test**
  Verify that requests without token authentication return 401.
  ```csharp
  [Fact]
  public async Task GetSessions_WithoutToken_Should_Return_401Unauthorized()
  {
      // ... invoke endpoint through client or direct mock with empty User principal ...
  }
  ```

- [ ] **Step 2: Enable services in Program.cs**
  Modify `Program.cs` to add authentication schemes and map routes:
  ```csharp
  builder.Services.AddIdentityApiEndpoints<IdentityUser<Guid>>()
      .AddEntityFrameworkStores<OrchestrationDbContext>();
  
  builder.Services.AddAuthentication();
  builder.Services.AddAuthorization();
  ```
  And in the middleware pipeline:
  ```csharp
  app.UseAuthentication();
  app.UseAuthorization();
  
  app.MapGroup("/api/auth")
     .MapIdentityApi<IdentityUser<Guid>>()
     .WithTags("Authentication");
  ```

- [ ] **Step 3: Secure the controller with Authorize attribute**
  Add `[Authorize]` to `AnalysisSessionsController.cs` at class level.

- [ ] **Step 4: Run tests to verify**
  Run: `dotnet test`
  Expected: PASS (unauthorized tests correctly return `401 Unauthorized`).
  Commit: `git add . && git commit -m "feat(security): protect analysis controller and map identity api endpoints"`

---

### Task 4: Enforce Session Isolation in Controller Queries

**Files:**
*   Modify: `backend/Orchestration.Api/Controllers/AnalysisSessionController.cs`
*   Test: `backend/Orchestration.Tests/Api/AnalysisSessionControllerTests.cs`

- [ ] **Step 1: Write failing isolation integration test**
  Write a test case validating that User B's session Guid is inaccessible to User A (returns 404 NotFound).
  ```csharp
  [Fact]
  public async Task GetSession_Should_Return_NotFound_If_BelongsToDifferentUser()
  {
      // Set HttpContext.User to UserA, but query Session belonging to UserB
  }
  ```

- [ ] **Step 2: Add CurrentUserId helper to controller**
  Add the property in `AnalysisSessionsController.cs`:
  ```csharp
  using System.Security.Claims;

  private Guid CurrentUserId => Guid.Parse(
      User.FindFirst(ClaimTypes.NameIdentifier)?.Value
      ?? throw new InvalidOperationException("User ID claim is missing.")
  );
  ```

- [ ] **Step 3: Modify Controller Endpoints to enforce isolation**
  *   **GetSessions:** `_dbContext.AnalysisSessions.Where(x => x.UserId == CurrentUserId)`
  *   **CreateSession:** `var session = AnalysisSession.Create(CurrentUserId);`
  *   **GetSession:** `var session = await _dbContext.AnalysisSessions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);`
  *   **GetSessionEvents:** Filter query check: `var sessionExists = await _dbContext.AnalysisSessions.AnyAsync(x => x.Id == id && x.UserId == CurrentUserId, cancellationToken);`
  *   **StartSession, GetStartPreflight, ApproveSession, RejectSession:** Add same `UserId == CurrentUserId` check during session retrieval.
  *   **SaveFinancialMetrics, SaveFinancialMetricsCsv, SaveFinancialMetricsFile, GetFinancialMetrics:** Verify session ownership before persisting metrics.

- [ ] **Step 4: Verify all tests pass**
  Run: `dotnet test`
  Expected: PASS.
  Commit: `git add . && git commit -m "feat(security): enforce strict multi-user analysis session isolation"`

---

### Task 5: Frontend Auth Services Integration

**Files:**
*   Modify: `frontend/src/services/api.ts`

- [ ] **Step 1: Implement fetch Bearer injection token wrapper**
  Rewrite `api.ts` to include the helper:
  ```typescript
  const getAuthToken = (): string | null => localStorage.getItem("auth_token");

  async function authenticatedFetch(url: string, options: RequestInit = {}): Promise<Response> {
    const token = getAuthToken();
    const headers = {
      ...options.headers,
      "Content-Type": "application/json",
      ...(token ? { "Authorization": `Bearer ${token}` } : {})
    };
    const response = await fetch(url, { ...options, headers });
    if (response.status === 401) {
      localStorage.removeItem("auth_token");
      window.location.reload();
    }
    return response;
  }
  ```

- [ ] **Step 2: Replace standard fetches with authenticated fetches**
  Update `loadSessionEvents`, `loadSavedSessions`, `loadSessionDetails`, `loadStructuredFinancialMetrics`, `getStartPreflight`, `createSession`, `startSession`, `submitHumanDecision`, `saveJsonMetrics`, `saveCsvMetrics`, and `uploadFinancialMetricsFile` to use `authenticatedFetch` instead of direct global `fetch`.

- [ ] **Step 3: Commit frontend API integration**
  Commit: `git add frontend/src/services/api.ts && git commit -m "feat(frontend): implement authenticatedFetch bearer auth injector"`

---

### Task 6: Frontend Premium Authentication View & Route Guard

**Files:**
*   Create: `frontend/src/components/Auth/AuthPage.tsx`
*   Create: `frontend/src/components/Auth/AuthPage.css`
*   Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Create AuthPage component**
  Write React forms in `frontend/src/components/Auth/AuthPage.tsx` that make requests to `${apiBaseUrl}/api/auth/login` and `/register`. On login success, save token to `auth_token` and callback to set authenticated state.

- [ ] **Step 2: Write styling in AuthPage.css**
  Create rich mode styling: Deep slate background gradients, backdrop-blur card wrapping, glowing light blue input borders on focus, full loading indicators, and toggle switch links between sign-in/sign-up.

- [ ] **Step 3: Implement Guard in App.tsx**
  Modify `App.tsx` to conditionally display `<AuthPage onLoginSuccess={...} />` if no `token` resides in state/localStorage, guarding the main Dashboard workspace.

- [ ] **Step 4: Commit UI auth flow**
  Commit: `git add . && git commit -m "feat(frontend): build premium dark-mode AuthPage and App route guards"`

---

### Task 7: Frontend Header Badge & Logout Integration

**Files:**
*   Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Add dynamic User Profile display inside Header**
  Extract email from local storage or claims representation, then render user email (e.g. `admin@ezemartino.com`) inside the header top corner. Add a custom styled avatar badge displaying the initial letter.

- [ ] **Step 2: Add Logout handler**
  Add a button executing `localStorage.removeItem("auth_token")` and refreshing the browser state to return to login.

- [ ] **Step 3: Run end-to-end verification**
  Boot backend `npm run dev` and frontend services, log in as `admin@ezemartino.com` / `Password1!`, verify previous items exist, register a new user, create a session, verify they are separate, log out, and check guards.
  Commit: `git add . && git commit -m "feat(frontend): integrate User profile badge and logout button in Header"`

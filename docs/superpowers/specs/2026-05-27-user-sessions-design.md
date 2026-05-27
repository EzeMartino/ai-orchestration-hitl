# Design Specification: User Authentication and Analysis Session Isolation

**Created At:** 2026-05-27
**Status:** Approved
**Topic:** Adding users and isolating analysis sessions per user.

---

## 1. Goal and Background

The application currently has a single public shared environment where anyone can view, create, and interact with all analysis sessions. There is no concept of users or authentication.

This design specification defines the implementation plan to add **multi-user capabilities**:
*   Secure registration and login screens in the frontend.
*   **Microsoft Identity** integration on the backend database (PostgreSQL) using an integrated `DbContext`.
*   Authentication using **JWT (JSON Web Tokens)** Bearer tokens.
*   **Strict database-level session isolation**, where each user can only view, create, and interact with their own analysis sessions.
*   **Seamless migration** of all existing legacy records in the database, assigning them to a default, seeded administrator user (`admin@ezemartino.com` / `Password1!`).

---

## 2. Proposed Architectural Changes

### 2.1 Database & Schema (Section 1)

1.  **Identity Database Integration:**
    Upgrade `OrchestrationDbContext` in `Orchestration.Infrastructure` to inherit from `IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>` instead of `DbContext`.
    This generates all the standard ASP.NET Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`, etc.) inside our PostgreSQL database, using `Guid` for all primary keys.

2.  **AnalysisSession Relationship:**
    Add a non-nullable `UserId` foreign key (FK) in `AnalysisSession.cs`:
    ```csharp
    public Guid UserId { get; private set; }
    ```
    Update the `AnalysisSession.Create(Guid userId)` domain builder method to require this user ID.

3.  **Model Configuration:**
    Configure a foreign key constraint in `OrchestrationDbContext.cs`'s `OnModelCreating`:
    ```csharp
    modelBuilder.Entity<AnalysisSession>(builder =>
    {
        builder.HasOne<IdentityUser<Guid>>()
               .WithMany()
               .HasForeignKey(x => x.UserId)
               .IsRequired()
               .OnDelete(DeleteBehavior.Restrict);
    });
    ```

4.  **Database Migration Plan (`AddUserAuthenticationAndIsolation`):**
    *   Create a migration step that generates Microsoft Identity tables.
    *   Add a nullable `UserId` column to `AnalysisSessions` first.
    *   Execute a SQL script during the migration to populate existing sessions with the fixed GUID of the seeded Admin user (e.g., `00000000-0000-0000-0000-000000000001`):
        ```sql
        UPDATE "AnalysisSessions" SET "UserId" = '00000000-0000-0000-0000-000000000001' WHERE "UserId" IS NULL;
        ```
    *   Alter the `UserId` column to be `NOT NULL`.
    *   Add the foreign key constraint.

---

### 2.2 Backend Authentication & Seeding (Section 2)

1.  **Identity Services & Endpoint Mapping (`Program.cs`):**
    Register the services and map built-in Microsoft Identity Minimal API endpoints in `Orchestration.Api`:
    ```csharp
    builder.Services.AddIdentityApiEndpoints<IdentityUser<Guid>>()
        .AddEntityFrameworkStores<OrchestrationDbContext>();
    
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization();
    ```
    Map the endpoints under `/api/auth`:
    ```csharp
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGroup("/api/auth")
       .MapIdentityApi<IdentityUser<Guid>>()
       .WithTags("Authentication");
    ```

2.  **Automatic Admin Seeding Service (`IdentityDataSeeder`):**
    Create an `IHostedService` (or db-initializer block) that runs on startup:
    *   Checks if the user `admin@ezemartino.com` exists.
    *   If not, creates it with the fixed Guid `00000000-0000-0000-0000-000000000001` and password `Password1!`.
    *   This ensures seamless compatibility with the migrated legacy analysis records.

---

### 2.3 Controller Security & Filtering (Section 3)

1.  **Protection Attribute:**
    Add `[Authorize]` to `AnalysisSessionsController` to enforce JWT authentication.

2.  **User Extraction Helper:**
    Retrieve the current logged-in user's GUID cleanly:
    ```csharp
    private Guid CurrentUserId => Guid.Parse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new InvalidOperationException("User ID claim is missing.")
    );
    ```

3.  **Strict Isolation Filtering:**
    All database queries inside `AnalysisSessionsController` must be filtered by `CurrentUserId`:
    *   **Listing:** `_dbContext.AnalysisSessions.Where(x => x.UserId == CurrentUserId)`
    *   **Retrieval / Modification:** `_dbContext.AnalysisSessions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == CurrentUserId)`
    *   **Creation:** `var session = AnalysisSession.Create(CurrentUserId);`
    *   **Metrics / Events Endpoints:** Validate that the target `SessionId` belongs to `CurrentUserId` before performing operations.
    *   **Enumeration Prevention:** Return `404 NotFound` (instead of `403 Forbidden`) if a session exists but belongs to a different user, hiding information from scan attempts.

---

### 2.4 Frontend Auth Integration (Section 4)

1.  **Storage & Token Persistence:**
    Store the token in `localStorage` as `"auth_token"`. Wiping it on logout.

2.  **API Client Fetch Interceptor (`frontend/src/services/api.ts`):**
    Wrap requests in an `authenticatedFetch` utility that injects the `Authorization: Bearer <token>` header.
    Automatically handle `401 Unauthorized` responses by cleaning `localStorage` and reloading the page, redirecting the user back to the Login view.

3.  **Premium Auth Screens (Login & Register):**
    Create visually spectacular dark-mode views in `frontend/src/components/Auth`:
    *   Background with subtle gradients and blur/glassmorphism.
    *   Responsive, clean forms with clear inline feedback and loader state indicators.
    *   Switching seamlessly between Login and Register.

4.  **Route Protection:**
    In `App.tsx`, if `"auth_token"` is not present, render the `<AuthPage />` instead of the main `<Dashboard />`.

5.  **Profile Badge & Logout in Header:**
    Include the active user's initial avatar and email in the main Dashboard Header, accompanied by a clean "Logout" button.

---

## 3. Verification Plan

### 3.1 Automated Verification
*   **Database Migrations:** Run EF Core database migration on a local Postgres instance and verify all Identity tables and foreign key constraints are correctly created.
*   **Seed Checks:** Verify `admin@ezemartino.com` is seeded on application startup.
*   **Integration Tests:** Verify that requesting `/api/analysis-sessions` returns `401 Unauthorized` without a JWT token, and returns the correct isolated list of sessions when authenticated.

### 3.2 Manual Verification
1.  **Register a New User:** Create `user1@ezemartino.com`.
2.  **Create Sessions:** Create 2 sessions. Verify they are visible.
3.  **Login as Admin:** Log in as `admin@ezemartino.com`. Verify the old legacy sessions are visible, but `user1`'s sessions are **not** visible.
4.  **Security Boundaries:** Try to access a session GUID of `user1` while logged in as `admin` (and vice-versa). Verify that `404 NotFound` is returned.
5.  **Logout Flow:** Verify logging out clears session tokens and locks the routes correctly.

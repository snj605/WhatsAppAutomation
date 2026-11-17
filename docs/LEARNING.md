# Learning notes

These notes explain every concept used in this project, in the order you meet them. Each section has:

- **What it is**: the idea in plain words.
- **Why we use it**: the problem it solves here.
- **Where it is**: the files to open, so you can read real code next to the explanation.
- **Try it**: a small exercise. Doing them is the fastest way to learn.

They are updated with every change. When a change adds a concept, it adds a section here too.

You already know C# basics and OOP (classes, interfaces, inheritance). Everything else is explained.

**Contents**

1. [The solution and its projects](#1-the-solution-and-its-projects)
2. [Clean Architecture](#2-clean-architecture)
3. [Dependency injection](#3-dependency-injection)
4. [Program.cs and the request pipeline](#4-programcs-and-the-request-pipeline)
5. [Controllers](#5-controllers)
6. [CQRS with MediatR](#6-cqrs-with-mediatr)
7. [Validation with FluentValidation](#7-validation-with-fluentvalidation)
8. [The Result pattern](#8-the-result-pattern)
9. [Entity Framework Core and PostgreSQL](#9-entity-framework-core-and-postgresql)
10. [Migrations](#10-migrations)
11. [ASP.NET Core Identity](#11-aspnet-core-identity)
12. [JWT and refresh tokens](#12-jwt-and-refresh-tokens)
13. [Roles and permissions (RBAC)](#13-roles-and-permissions-rbac)
14. [Configuration, secrets and the Options pattern](#14-configuration-secrets-and-the-options-pattern)
15. [Logging](#15-logging)
16. [Rate limiting](#16-rate-limiting)
17. [Tests](#17-tests)
18. [Recipe: adding a new feature](#18-recipe-adding-a-new-feature)
19. [Modern C# you will see here](#19-modern-c-you-will-see-here)
20. [Glossary](#20-glossary)

Web app (Angular)

21. [How the Angular app is organised](#21-how-the-angular-app-is-organised)
22. [Standalone components](#22-standalone-components)
23. [Signals](#23-signals)
24. [Routing, lazy loading and guards](#24-routing-lazy-loading-and-guards)
25. [Calling the API: HttpClient and interceptors](#25-calling-the-api-httpclient-and-interceptors)
26. [Web sign-in: cookies and XSRF](#26-web-sign-in-cookies-and-xsrf)
27. [PrimeNG](#27-primeng)
28. [Front-end tests with Vitest](#28-front-end-tests-with-vitest)

WhatsApp

29. [How sending to WhatsApp works here](#29-how-sending-to-whatsapp-works-here)
30. [Linking: QR code and phone code](#30-linking-qr-code-and-phone-code)
31. [Keeping the WhatsApp login safe](#31-keeping-the-whatsapp-login-safe)
32. [Live updates with SignalR](#32-live-updates-with-signalr)
33. [Two updates at the same time: row versions](#33-two-updates-at-the-same-time-row-versions)
34. [Starting the gateway with F5: hosted services](#34-starting-the-gateway-with-f5-hosted-services)
35. [TypeScript in Node.js without a build step](#35-typescript-in-nodejs-without-a-build-step)

---

## 1. The solution and its projects

**What it is.** A *solution* (`WhatsAppAutomation.slnx`) groups *projects*. Each C# project (`.csproj`) compiles into one `.dll`. A project can use another only if it has a `<ProjectReference>` to it.

All backend projects live inside the `WhatsAppAutomation.Server` folder: the app projects in `src/`, the tests in `tests/`. Paths below are relative to `WhatsAppAutomation.Server/src/` (or `tests/` for the tests).

| Project | Job |
| --- | --- |
| `WhatsAppAutomation.Domain` | Business concepts and rules: entities (`Entities/`) such as refresh tokens, and the permission and role definitions (`Authorization/`). No frameworks. |
| `WhatsAppAutomation.Application` | Use cases: every command and query, with validation and business rules. `Common/` holds what all features share; `Features/<Feature>/Commands` and `Queries` hold one file per use case. |
| `WhatsAppAutomation.Infrastructure` | Talks to the outside world: PostgreSQL, ASP.NET Core Identity, JWT creation. |
| `WhatsAppAutomation.Api` | The web host: controllers, contracts, authorization, startup, the admin CLI. |
| `WhatsAppAutomation.Application.Tests` | Unit tests for use cases, with fakes instead of a database. |
| `WhatsAppAutomation.Api.Tests` | API, architecture and integration tests. |
| `whatsappautomation.client` | The Angular web app. |

**Where it is.** Open `WhatsAppAutomation.slnx` and each `.csproj`. Look at the `<ProjectReference>` lines: Api → Infrastructure → Application → Domain. `WhatsAppAutomation.Server/Directory.Build.props` holds settings every backend project shares (target framework, nullable, warnings as errors).

**Try it.** Add `using WhatsAppAutomation.Infrastructure;` to any file in the Domain project and build. The build fails because Domain has no reference to Infrastructure. That is the compiler protecting the architecture.

## 2. Clean Architecture

**What it is.** Code is arranged in rings. Inner rings hold the most important and most stable code (business rules). Outer rings hold details that change more often (database, web framework). **Dependencies only point inwards**: Domain knows nothing about the database; the database code knows about Domain.

```
  Api (web)  ──►  Infrastructure (DB, Identity)  ──►  Application (use cases)  ──►  Domain (rules)
```

How can Application save data if it cannot see Infrastructure? Through **interfaces**. Application *defines* `IRoleService`; Infrastructure *implements* it (`RoleService`). At runtime, dependency injection (section 3) hands the implementation to whoever asks for the interface. This is the *Dependency Inversion Principle*, the "D" in SOLID.

**Why we use it.** Business rules can be tested without a database, and the database or web framework can change without touching the rules.

**Where it is.** `WhatsAppAutomation.Application/Common/Interfaces/` holds the interfaces. `WhatsAppAutomation.Infrastructure/Identity/` holds their implementations. `tests/WhatsAppAutomation.Api.Tests/Architecture/LayerTests.cs` checks the rules automatically.

**Try it.** Open `IRoleService.cs`, then find its implementation with *Go To Implementation* (Ctrl+F12) in Visual Studio.

## 3. Dependency injection

**What it is.** A class does not create the objects it needs with `new`. It lists them in its constructor, and the framework's *container* creates and passes them in. You register what to create once, at startup.

```csharp
// Registration (Infrastructure/DependencyInjection.cs)
services.AddScoped<IRoleService, RoleService>();

// Use (Application/Features/Roles/Commands/CreateRoleCommand.cs): just ask for it in the constructor
internal sealed class DeleteRoleCommandHandler(IRoleService roles, IPermissionService permissions) ...
```

**Lifetimes** decide how long an object lives:

| Lifetime | One instance per… | Used here for |
| --- | --- | --- |
| `AddSingleton` | whole app | `TimeProvider`, `PermissionCacheSignal`, the JWT issuer |
| `AddScoped` | HTTP request | `AppDbContext`, services that use it, handlers |
| `AddTransient` | every time it is asked for | (not used yet) |

Rule: a singleton must never hold a scoped object, because the scoped one would outlive its request.

**Where it is.** `AddApplication` in `WhatsAppAutomation.Application/DependencyInjection.cs`, `AddInfrastructure` in `WhatsAppAutomation.Infrastructure/DependencyInjection.cs`, and `Program.cs` calls both.

**Try it.** Comment out `services.AddScoped<IRoleService, RoleService>();`, run the app and call `GET /api/v1/roles`. Read the error: the container tells you exactly which service is missing.

## 4. Program.cs and the request pipeline

**What it is.** `Program.cs` builds the app in two halves:

1. **Services** (`builder.Services.Add...`): what the app can use.
2. **Middleware** (`app.Use...`, `app.Map...`): the steps every HTTP request passes through, *in order*.

Order matters. `UseAuthentication` (who are you?) must run before `UseAuthorization` (may you do this?). `UseExceptionHandler` comes first so it can catch errors from everything after it.

`Program.cs` also checks for CLI commands (`migrate`, `create-user`) before starting the web server. Those live in `Cli/AdminCli.cs`.

**Where it is.** `WhatsAppAutomation.Api/Program.cs`.

**Try it.** Move `app.UseAuthorization();` above `app.UseAuthentication();`, run the tests (`dotnet test`) and see which ones fail. Then move it back.

## 5. Controllers

**What it is.** A controller turns HTTP into C#. Attributes describe the HTTP side:

- `[Route("api/v1/roles")]`, `[HttpGet("{id:guid}")]`: the URL and method.
- Parameters such as `SaveRoleRequest request` are filled from the JSON body (*model binding*).
- `[ProducesResponseType]` documents the possible answers for OpenAPI.

Our controllers are **thin**: build a command or query, send it, turn the result into a response. No business logic.

Errors use **Problem Details** (RFC 9457), a standard JSON error shape. We add a `code` such as `roles.in_use` so apps can react to specific errors. See `Api/Extensions/ErrorResults.cs`.

**Where it is.** `WhatsAppAutomation.Api/Controllers/`. The request and response shapes (*contracts*, also called DTOs) are in `Contracts/`. We keep them separate from domain classes so the API can stay stable when internals change.

**Try it.** In `WhatsAppAutomation.Api.http`, add a request for `GET {{host}}/api/v1/permissions` with the Owner's token and run it.

## 6. CQRS with MediatR

**What it is.** *CQRS* (Command Query Responsibility Segregation) splits every use case into one of two kinds:

- **Command**: changes something. `CreateRoleCommand`, `LoginCommand`.
- **Query**: only reads. `GetRolesQuery`, `GetCurrentUserQuery`.

Each has exactly one **handler** class that does the work. Small, focused classes are easy to read, test and change.

**MediatR** connects them. The controller does not know which class handles a request; it calls `mediator.Send(command)`, and MediatR finds the handler. On the way, the request passes through **pipeline behaviors**, which are like middleware for commands:

```
Controller ─Send─► LoggingBehavior ─► ValidationBehavior ─► Handler ─► Result
```

- `LoggingBehavior`: logs the request name, outcome and time (never the data, which may hold passwords).
- `ValidationBehavior`: runs the validators; if any rule fails, the handler is never called.

Our own interfaces `ICommand<T>`, `IQuery<T>` and their handler interfaces (in `Common/Messaging/Messaging.cs`) are thin wrappers over MediatR's `IRequest` that make the command or query intent visible and force every handler to return a `Result`.

**Licence.** MediatR version 13 and later needs a licence key for production (a free Community licence exists for small businesses). Without a key it works and logs a warning. Put the key in user secrets as `MediatR:LicenseKey`.

**Where it is.** `WhatsAppAutomation.Application/Common/Messaging/`, `Common/Behaviors/`, and every use case under `Features/Authentication/`, `Features/Roles/`, `Features/Users/` (one file per command or query). Registration is in `Application/DependencyInjection.cs`.

**Try it.** Put a breakpoint in `ValidationBehavior.Handle` and in `CreateRoleCommandHandler.Handle`, then create a role from the `.http` file with an empty name. See which breakpoint is hit and which is not.

## 7. Validation with FluentValidation

**What it is.** Validation rules written as code next to the command they check:

```csharp
RuleFor(c => c.UserName).NotEmpty().MaximumLength(50);
```

**Why.** Rules are in one place, readable and testable, and the pipeline (section 6) runs them automatically, so handlers can assume the input is well-formed.

Two kinds of checks, and where they go:

| Check | Example | Where |
| --- | --- | --- |
| Shape of the input | name not empty, password not too long | Validator |
| Needs the database | name not already taken, last Owner | Handler |

**Where it is.** Classes ending in `Validator`, in the same file as their command, e.g. `Features/Users/Commands/CreateUserCommand.cs`. One file holds the whole use case: command, validator, handler.

**Try it.** Add a rule that a role description must not contain `<`, then write a test for it in `AccessControlTests`.

## 8. The Result pattern

**What it is.** Handlers return `Result` or `Result<T>` instead of throwing exceptions for expected failures (wrong password, name taken). A result is either a success with a value, or an `Error` with a `Code`, a `Message` and a `Kind`.

**Why.** Exceptions are for the unexpected (database down). Expected outcomes are part of the normal flow, and returning them makes every possible outcome visible in the method's type. The controller maps `ErrorKind` to an HTTP status (Validation → 400, NotFound → 404, Conflict → 409, …).

**Where it is.** `WhatsAppAutomation.Application/Common/Models/Result.cs`; errors per feature in `AuthErrors`, `RoleErrors`, `UserErrors`.

**Try it.** Find every place `RoleErrors.InUse` is returned and every test that checks for 409.

## 9. Entity Framework Core and PostgreSQL

**What it is.** EF Core is an *ORM*: you work with C# classes, it writes the SQL. `AppDbContext` is the gateway: each `DbSet<T>` is a table.

Things to know:

- **Configurations** (`Persistence/Configurations/`) describe tables in code: names, keys, lengths, indexes, foreign keys. `UseSnakeCaseNamingConvention()` turns `DisplayName` into the column `display_name`, the usual PostgreSQL style.
- **LINQ becomes SQL.** `db.Roles.Where(r => r.IsSystem).ToListAsync()` runs one `SELECT … WHERE is_system`.
- **`AsNoTracking()`** for read-only queries: faster, because EF does not watch the objects for changes.
- **`ExecuteUpdateAsync` / `ExecuteDeleteAsync`** change rows directly in SQL without loading them. Used to revoke tokens.
- **Transactions** (`BeginTransactionAsync`) make several changes succeed or fail together.
- **The N+1 problem**: loading 50 users, then running one more query per user for their roles, is 51 queries. `IdentityService.ListUsersAsync` loads users, then all their roles in a second query: 2 queries total.

**Where it is.** `WhatsAppAutomation.Infrastructure/Persistence/`.

**Try it.** In `appsettings.Development.json`, set the log level of `Microsoft.EntityFrameworkCore.Database.Command` to `Information`, call `GET /api/v1/users`, and read the SQL in the console.

## 10. Migrations

**What it is.** A migration is a C# file that changes the database schema, generated by comparing your model to the previous snapshot. They run in order, and the table `__EFMigrationsHistory` remembers which have run.

```
cd WhatsAppAutomation.Server/src
dotnet ef migrations add <Name> --project WhatsAppAutomation.Infrastructure --startup-project WhatsAppAutomation.Api -o Persistence/Migrations
dotnet run --project WhatsAppAutomation.Api -- migrate
```

(Install the tool once with `dotnet tool install --global dotnet-ef`.)

- **Seed data** with `HasData`: the `permissions` table is filled from `Permissions.All`. Add a permission in code, add a migration, and the insert is generated for you.
- **Hand-written SQL** with `migrationBuilder.Sql(...)`: `AddRolesAndPermissions` inserts Owner and Sender with `ON CONFLICT … DO UPDATE`, because databases that already ran `create-user` have those roles.
- **Never edit a migration that has already run** anywhere. Add a new one instead.

**Where it is.** `WhatsAppAutomation.Infrastructure/Persistence/Migrations/`.

**Try it.** Add a permission `users.export` to `Permissions.All`, create a migration called `AddUsersExport`, and read what it generated. Then delete it with `dotnet ef migrations remove` (same `--project` options).

## 11. ASP.NET Core Identity

**What it is.** Microsoft's library for users, password hashing, roles and lockout. We use only its core (`AddIdentityCore`), not its built-in web pages.

- **Passwords are never stored.** Identity stores a slow, salted hash (PBKDF2). Checking a password re-hashes and compares.
- **Lockout**: 5 wrong passwords lock the account for 15 minutes, which stops guessing.
- **Normalized names**: `NormalizedUserName` is an upper-case copy, so `Dhaval` and `dhaval` count as the same name.
- Same answer for unknown user and wrong password, and a dummy hash check for unknown users, so an attacker cannot find out which user names exist.

`AppUser` and `AppRole` extend Identity's classes with our own fields (`DisplayName`, `IsActive`, `IsSystem`, `Description`).

**Where it is.** `WhatsAppAutomation.Infrastructure/Identity/`.

**Try it.** Look at the `password_hash` column of `users` in pgAdmin. Create two users with the same password and compare the hashes. (They differ because of the salt.)

## 12. JWT and refresh tokens

**What it is.** After sign-in the server gives the app two tokens:

- **Access token**: a *JWT* (JSON Web Token). Three base64 parts: header, payload (claims such as `sub` = user id) and a signature made with our secret signing key. The server can trust it without a database lookup, because only the server can produce the signature. It lives 10 minutes. The app sends it on every request: `Authorization: Bearer <token>`.
- **Refresh token**: a long random string, valid 30 days, used only to get a new pair when the access token expires.

Safety measures, and why:

| Measure | Why |
| --- | --- |
| Access token lasts 10 minutes | A stolen one is useful only briefly. |
| Only a SHA-256 hash of the refresh token is stored | A database leak does not leak usable tokens. |
| Refresh tokens rotate on every use | Each one works once. |
| A used token coming back ends the session | That only happens if someone copied it. |
| Tokens are bound to a device id | A token copied to another device is useless. |
| One atomic `UPDATE … WHERE revoked_at IS NULL` | Two refreshes at the same moment cannot both win. |

The JWT says **who** you are, not **what you may do**. Permissions are checked on each request (section 13), so a change to a role works at once instead of after the token expires.

**Where it is.** `Application/Features/Authentication/` (commands), `Infrastructure/Identity/JwtAccessTokenIssuer.cs`, JWT validation setup in `Infrastructure/DependencyInjection.cs`.

**Try it.** Sign in, copy the `accessToken` and paste it into jwt.io (it is a dev token, so that is fine). Read the claims. Change one letter and call `/me`: you get 401.

## 13. Roles and permissions (RBAC)

**What it is.** *Role-Based Access Control*: users get **roles**, roles get **permissions**, endpoints require **permissions**.

```
users ──< user_roles >── roles ──< role_permissions >── permissions
```

- **Permissions are defined in code** (`Domain/Authorization/Permissions.cs`), because each one guards code. The `permissions` table mirrors that list for the settings screen and for foreign keys.
- **Roles are data.** The Owner creates, edits and deletes them through the API (`RolesController`).
- **Owner** is a system role: it always has every permission and cannot be edited or deleted. The **last active Owner** cannot be disabled or lose the role, so nobody can lock everyone out.
- A role that users still have cannot be deleted.

How a request is checked:

1. The action has `[HasPermission(Permissions.Roles.Manage)]`. That is an `[Authorize]` with the policy name `permission:roles.manage`.
2. `PermissionPolicyProvider` turns that name into a policy with a `PermissionRequirement`.
3. `PermissionAuthorizationHandler` asks `IPermissionService` for the user's permissions.
4. `PermissionService` loads them (roles → permissions, or everything for Owner, nothing for a disabled user) and **caches** them for 5 minutes.
5. Every command that changes roles, role permissions or user roles calls `InvalidateAll()`, which clears the whole cache at once through a cancellation token, so changes apply immediately.

401 means "not signed in", 403 means "signed in, but not allowed".

**Where it is.** `Domain/Authorization/`, `Application/Features/Roles/`, `Application/Features/Users/`, `Infrastructure/Identity/PermissionService.cs`, `Api/Authorization/PermissionAuthorization.cs`.

**Try it.** Add a permission `users.export`, a migration, and an endpoint `GET /api/v1/users/export` protected by it. Give it to the Sender role through `PUT /api/v1/roles/{id}` and call it as a Sender.

## 14. Configuration, secrets and the Options pattern

**What it is.** Settings come from several places, later ones winning: `appsettings.json` → `appsettings.Development.json` → **user secrets** (development only) → **environment variables** (`Auth__SigningKey`; `__` means a nested section).

Secrets (database password, signing key, licence keys) go only in user secrets or environment variables, never in files that reach Git.

The **Options pattern** binds a section to a class (`AuthOptions`) and validates it at startup with data annotations (`[Required]`, `[MinLength(32)]`), so a bad setting stops the app at once with a clear message instead of failing later.

**Where it is.** `Application/Features/Authentication/AuthOptions.cs` and `AddOptions<AuthOptions>()…ValidateOnStart()` in `Infrastructure/DependencyInjection.cs`.

## 15. Logging

**What it is.** `ILogger<T>` writes structured logs: `"User {UserId} signed in"` keeps `UserId` as a field you can search, not just text. The `[LoggerMessage]` attribute generates fast logging code at compile time.

**Rule:** never log passwords, tokens, phone numbers or message text.

**Where it is.** `LoggingBehavior.cs`, `LoginCommand.cs`, `RefreshCommand.cs`.

## 16. Rate limiting

**What it is.** ASP.NET Core's built-in rate limiter. Sign-in, refresh and logout allow 10 requests a minute per IP address, then answer 429. Together with lockout, this makes password guessing impractical.

**Where it is.** `Api/Configurations/RateLimitPolicies.cs`, used with `[EnableRateLimiting]` in `AuthController`.

## 17. Tests

Three kinds, written with **xUnit**, in two test projects:

| Kind | What it checks | Example |
| --- | --- | --- |
| Unit | One class alone, with hand-written fakes | `WhatsAppAutomation.Application.Tests/Features/Users/LastOwnerRuleTests.cs` |
| Integration (API) | The real app over HTTP against a real PostgreSQL | `WhatsAppAutomation.Api.Tests/Auth/AuthApiTests.cs`, `Access/AccessControlTests.cs` |
| Architecture | The layer rules of section 2 | `WhatsAppAutomation.Api.Tests/Architecture/LayerTests.cs` |

- `ApiFactory` uses `WebApplicationFactory<Program>` to start the real app in memory and creates a fresh database for the run.
- Tests are named as sentences (`A_role_that_users_still_have_cannot_be_deleted`) so a failure reads like a broken rule.
- Each test follows **Arrange, Act, Assert**: set up, do one thing, check the outcome.

Run: `dotnet test`, or **Test → Test Explorer** in Visual Studio. See the README for the database setting.

**Try it.** Write a test that a Sender without `roles.view` gets 403 from `GET /api/v1/permissions`.

## 18. Recipe: adding a new feature

Example: "list contacts".

1. **Domain**: add the entity (`Entities/Contact.cs`) with its rules, and a permission (`contacts.view`) in `Permissions.All`.
2. **Application**: create `Features/Contacts/Queries/GetContactsQuery.cs` with the query and its handler (commands go in `Commands/`, with their validator). If it needs storage, add a `DbSet` to `IAppDbContext` or an interface in `Common/Interfaces/`.
3. **Infrastructure**: add the EF configuration, implement any new interface, register it in `DependencyInjection.cs`.
4. **Migration**: `dotnet ef migrations add AddContacts …`.
5. **Api**: add the controller action with `[HasPermission(Permissions.Contacts.View)]` and its contracts.
6. **Tests**: an API test for success, 403 without the permission, and each business rule.
7. **These notes**: add any new concept.

## 19. Modern C# you will see here

| Feature | Example | Meaning |
| --- | --- | --- |
| Primary constructor | `class RoleService(AppDbContext db)` | Constructor parameters usable in the whole class. |
| `record` | `record RoleRef(Guid Id, string Name);` | A small immutable data class with value equality. |
| Collection expression | `[.. list]`, `[]` | Build a list or array. `..` spreads another collection in. |
| Nullable reference types | `string?` | `?` means it may be null; the compiler warns when you forget to check. |
| Pattern matching | `if (x is { } value)` | "If x is not null, call it value". |
| `static abstract` interface member | `IFailable<T>.Fail` | Lets generic code call a static method of the type, used in `ValidationBehavior`. |
| `sealed` | `sealed class` | Cannot be inherited. Clearer intent and slightly faster. |
| `internal` | `internal sealed class …Handler` | Visible only inside its project. Handlers are internal because only MediatR calls them. |

## 20. Glossary

| Term | Meaning |
| --- | --- |
| API | The URLs the apps call. Ours start with `/api/v1`. |
| DTO / contract | A class that only carries data in or out of the API. |
| Entity | A class saved in the database, with an identity (id). |
| Handler | The class that executes one command or query. |
| Middleware | A step every HTTP request passes through. |
| Pipeline behavior | A step every MediatR request passes through. |
| Claim | One fact inside a token, e.g. `sub` = user id. |
| Policy | A named authorization rule, e.g. `permission:users.view`. |
| 401 / 403 | Not signed in / signed in but not allowed. |
| Idempotent | Doing it twice has the same effect as once (logout is). |

---

# Web app (Angular)

The web app is in `whatsappautomation.client`. All paths below start at `whatsappautomation.client/src/app`. Angular code is TypeScript: JavaScript with types, which will feel close to C#.

## 21. How the Angular app is organised

**What it is.** One folder per job:

| Folder | Holds | Like on the backend |
| --- | --- | --- |
| `core/` | Things used everywhere, created once: sign-in state, the HTTP interceptor, guards, config. | Infrastructure and cross-cutting code. |
| `layouts/` | The frame around pages: top bar and side menu. | – |
| `features/<area>/` | One folder per screen area (`auth`, `home`, `settings/users`, `settings/roles`). Each has its page and the code to call its API. | `Application/Features/<Feature>`. |
| `app.config.ts` | Registers services at start-up. | `Program.cs` and `DependencyInjection.cs`. |
| `app.routes.ts` | Which URL shows which page. | Controller routes. |

**Why.** New features get a new folder under `features/` and do not touch the others. Contacts will be `features/contacts/`.

**Try it.** Open `main.ts`: `bootstrapApplication(App, appConfig)` is the Angular version of `app.Run()`.

## 22. Standalone components

**What it is.** A component is a class plus an HTML template: one piece of screen. "Standalone" means the component lists the other components it uses in its own `imports: [...]`. Older Angular put them in shared `NgModule` files instead; we removed those (`app-module.ts`).

```ts
@Component({
  selector: 'app-users-page',
  imports: [TableModule, ButtonModule, DialogModule, ...],  // what this template uses
  templateUrl: './users.page.html',
})
export class UsersPage { ... }
```

Templates use built-in control flow: `@if (canManage()) { ... }` and `@for (item of nav(); track item.link) { ... }`.

**Why.** Each file says exactly what it depends on, like `using` lines in C#, and pages can be loaded only when needed (section 24).

**Where.** `features/settings/users/users.page.ts` and `.html`.

**Try it.** Remove `ButtonModule` from the imports of `users.page.ts` and run `npm run build`: the build tells you `p-button` is unknown.

## 23. Signals

**What it is.** A signal is a value that tells Angular when it changes.

```ts
readonly users = signal<UserItem[]>([]);         // a value you can set
this.users.set(list);                            // change it
readonly count = computed(() => this.users().length);  // derived, recalculated automatically
```

You read a signal by calling it: `users()`. When a template reads a signal, Angular redraws that part of the screen when the signal changes. Nothing else needs to be told.

**Why.** The app runs "zoneless": Angular does not watch every click and timer to guess what changed. Signals tell it exactly. It is faster and easier to follow.

**Where.** `core/auth/auth.service.ts` (the signed-in user and permissions), `layouts/main-layout/main-layout.ts` (the menu is a `computed` filtered by permission).

**Try it.** In `main-layout.ts`, find `nav`. Follow how it uses `auth.can(...)`, which reads the `permissionSet` signal.

## 24. Routing, lazy loading and guards

**What it is.** `app.routes.ts` maps URLs to pages:

```ts
{ path: 'settings/users', canActivate: [permissionGuard('users.view')],
  loadComponent: () => import('./features/settings/users/users.page').then(m => m.UsersPage) }
```

- `loadComponent` with `import(...)` is **lazy loading**: the page's code is downloaded only the first time someone opens it. The first download stays small.
- A **guard** runs before a page opens and returns `true` or another URL to go to instead:
  - `signedInGuard`: not signed in → `/login?returnUrl=...`, which brings you back after signing in.
  - `guestGuard`: already signed in → no sign-in page, go Home.
  - `permissionGuard('roles.view')`: without the permission → Home.

**Important.** Guards and hidden buttons are only for a tidy screen. Anyone can change JavaScript in their own browser. The real protection is the API: every endpoint checks `[HasPermission]` again (section 13).

**Where.** `app.routes.ts`, `core/guards/auth.guards.ts`.

**Try it.** Sign in as a user without `roles.view` and type `/settings/roles` in the address bar. You land on Home.

## 25. Calling the API: HttpClient and interceptors

**What it is.** `HttpClient` makes HTTP calls and returns an `Observable` (a value that arrives later, from the RxJS library). `firstValueFrom(...)` turns it into a `Promise` so you can `await` it, like a C# `Task`.

An **interceptor** is a function every HTTP call passes through: the browser version of ASP.NET Core middleware. Ours (`authInterceptor`):

1. Adds `Authorization: Bearer <access token>` to `/api/` calls.
2. If the API answers 401 (the 10-minute token expired), gets a new token once and repeats the call.
3. If that fails too, the session is over: go to the sign-in page.

If five calls get a 401 at the same moment they share one refresh call (`refreshInFlight` with `shareReplay` in `auth.service.ts`). This matters: the server ends the session if the same refresh token is used twice (section 12).

Error answers from the API are ProblemDetails JSON. `core/http/problem.ts` turns them into a message to show.

**Where.** `core/interceptors/auth.interceptor.ts`, `core/auth/auth.service.ts`, `features/settings/settings.api.ts`.

**Try it.** Read `auth.interceptor.spec.ts`: each test is one of the three steps above.

## 26. Web sign-in: cookies and XSRF

**What it is.** The phone API returns the refresh token in the JSON body, and the phone keeps it in secure storage. A browser has no safe storage that page scripts cannot read: if a bad script ever ran on the page (XSS), it could steal a token from `localStorage`. So the web app uses `/api/v1/auth/web/*`:

- The **refresh token** is put in a cookie with these flags:
  - `HttpOnly`: page scripts cannot read it at all.
  - `Secure`: sent over HTTPS only.
  - `SameSite=Strict`: not sent when another site starts the request.
  - `Path=/api/v1/auth/web`: sent only to the sign-in endpoints, not with every call.
- The **access token** is kept only in memory (a variable in `AuthService`). Reloading the page loses it, so on start the app calls `refresh`; the cookie gets it a new one.
- Cookies are sent automatically, which opens another attack: **CSRF** (another site making your browser call our API). The defence used here is "double submit": the server sets a readable `XSRF-TOKEN` cookie, Angular copies it into the `X-XSRF-TOKEN` header, and the server checks they match. Another site cannot read our cookie, so it cannot send the right header. `SameSite=Strict` blocks it too; two defences are better than one.

**Why.** Even if a script were injected, it could not take a long-lived token away.

**Where.** Backend: `WhatsAppAutomation.Server/src/WhatsAppAutomation.Api/Controllers/WebAuthController.cs` and its tests `Auth/WebAuthTests.cs`. Front end: `withXsrfConfiguration` in `app.config.ts`, `restoreSession` in `auth.service.ts`.

**Try it.** Sign in, open the browser developer tools (F12) → Application → Cookies. Then type `document.cookie` in the Console: `XSRF-TOKEN` is listed, `waa_refresh` is not.

## 27. PrimeNG

**What it is.** A library of ready-made components: `p-table`, `p-dialog`, `p-button`, `p-multiselect`, `p-toast` (pop-up messages), `p-confirmdialog` (are-you-sure questions), and many more. The look comes from a theme, **Aura**, set in `app.config.ts` with `providePrimeNG(...)`.

- Toasts: inject `MessageService` and call `add({ severity: 'success', summary: 'Saved' })`.
- Confirmations: inject `ConfirmationService` and call `confirm({ message, accept: () => ... })`.
- Both are shown by `<p-toast>` and `<p-confirmdialog>` in `app.html`, once for the whole app.
- Theme colours are CSS variables such as `var(--p-primary-color)`. Use them in our CSS instead of fixed colours, so a theme change updates everything.

PrimeNG 22 needs a licence key; it goes in `core/config/primeng-license.ts`. Without it a banner says "Invalid PrimeUI License".

**Where.** `app.config.ts`, `features/settings/users/users.page.html`.

**Try it.** In `roles.page.ts`, change a toast's `severity` from `success` to `warn` and look at the difference.

## 28. Front-end tests with Vitest

**What it is.** Vitest runs TypeScript unit tests, like xUnit for C#. Files end in `.spec.ts`. `describe` groups tests, `it` is one test (like `[Fact]`), `expect(x).toBe(y)` is the assertion.

Angular's `TestBed` builds a small app for the test. `HttpTestingController` stands in for the server: the test checks which call was made and decides the answer.

Run: `npm test -- --watch=false` in `whatsappautomation.client`. Without `--watch=false` it keeps running and repeats the tests whenever you save.

**Where.** `core/interceptors/auth.interceptor.spec.ts`.

**Try it.** Add a test that a call to `/health` gets no `Authorization` header.

---

# WhatsApp

## 29. How sending to WhatsApp works here

**What it is.** Three programs work together:

```
Angular app ──HTTPS──▶ ASP.NET Core API ──HTTP + secret──▶ Node.js gateway ──▶ WhatsApp
     ▲                        │  ▲                                │
     └──── SignalR (live) ────┘  └──── events (HTTP + secret) ────┘
```

- The **gateway** (`whatsappautomation.gateway`) uses Baileys, a library that behaves like WhatsApp Web. It is the only program that talks to WhatsApp. It has no business rules.
- The **API** decides who may do what, stores statuses and messages, and tells the app.
- The **app** never talks to the gateway.

**Why.** Baileys is JavaScript, and the rest of the backend is C#. Keeping it in its own small service means a Baileys update or crash cannot break the API, and the API stays testable with a fake gateway (`FakeWhatsAppGateway` in the tests).

**Where.** `Application/Common/Interfaces/IWhatsAppGateway.cs` (what the API needs), `Infrastructure/WhatsApp/HttpWhatsAppGateway.cs` (the real implementation), `whatsappautomation.gateway/src/http.ts` (the other side).

**Try it.** Follow one test message: `WhatsAppController.SendTestMessage` → `SendTestMessageCommand` → `HttpWhatsAppGateway.SendTextAsync` → `http.ts` "messages" → `session.ts` `#sendTextNow`.

## 30. Linking: QR code and phone code

**What it is.** Linking makes the gateway a "linked device" of the phone, like WhatsApp Web.

- **QR**: the gateway gets a new QR text from WhatsApp every ~20 seconds and sends it to the API, which pushes it to the app. The app draws it with the `qrcode` package. The phone scans it.
- **Phone code**: for when the screen is on the same phone (you cannot scan your own screen). The gateway asks WhatsApp for an 8-character code; you type it on the phone.
- After the scan WhatsApp closes the connection with code 515 "restart required". That is normal: the gateway reconnects with the new login, and the status becomes `connected`.
- Linking stops after 3 minutes if nobody finishes it.

**Where.** `session.ts`: `link`, `#onQr`, `#onOpen`, `#onClose`.

**Try it.** In `session.ts`, read the `switch (code)` in `#onClose` and match each case with the table in the comment at the top of the class.

## 31. Keeping the WhatsApp login safe

**What it is.** After linking, Baileys holds keys that let anyone send as your number without scanning again. The gateway stores them in the `whatsapp_auth_state` table, **encrypted with AES-256-GCM**:

- **AES-256** is the cipher; the 32-byte key comes from `GatewayDev:AuthStateKey` (on a server, a secret file).
- **GCM** also adds a tag that detects any change: a modified value fails to decrypt instead of giving wrong data.
- Every value has its own random **IV** (starting value), so the same data never encrypts the same way twice.
- The account id and key name are bound in as **additional data**, so a row copied to another account does not decrypt.

The API never has this key and never reads that table. The migration creates it only so that deleting an account deletes its login (foreign key with cascade).

**Where.** `whatsappautomation.gateway/src/crypto.ts`, `src/auth-store.ts`; tests in `test/crypto.test.ts` and `test/auth-store.test.ts`.

**Try it.** Read `test/crypto.test.ts`: each test is one of the four points above.

## 32. Live updates with SignalR

**What it is.** SignalR keeps a connection open between browser and server (a WebSocket), so the server can push messages without the app asking. Here it pushes QR codes, the phone code, status changes and message ticks.

- Server: `Api/Realtime/WhatsAppHub.cs`. The hub has no methods: only the server sends. `SignalRWhatsAppNotifier` sends to `Clients.User(userId)`, so each user receives only their own updates. `SubClaimUserIdProvider` tells SignalR that the user id is the token's `sub` claim.
- Browsers cannot put an `Authorization` header on a WebSocket, so the token travels as `?access_token=`. `RealtimeSetup.cs` accepts it there, and only for `/hubs`.
- Client: `core/realtime/whatsapp-live.service.ts` builds the connection with `@microsoft/signalr`. `accessTokenFactory` calls `auth.freshToken()`, which renews the 10-minute token when it is about to expire. Events are RxJS `Subject`s the page subscribes to.

**Why not ask every few seconds (polling)?** A QR changes every 20 seconds and ticks arrive at any time. Pushing is instant and costs nothing while nothing happens.

**Try it.** Open the WhatsApp page, then F12 → Network → WS: you see `/hubs/whatsapp` and its messages.

## 33. Two updates at the same time: row versions

**What it is.** When you send a message, the API saves it, calls the gateway, then saves the result. Meanwhile WhatsApp may already report "delivered", and that event updates the **same row**. Without care, the second save would overwrite the first.

PostgreSQL gives every row a hidden version number, `xmin`, which changes on every update. `OutgoingMessageConfiguration` maps it as a **row version**: EF Core adds "AND xmin = the version I read" to its UPDATE. If someone changed the row meanwhile, nothing is updated and EF throws `DbUpdateConcurrencyException`. `OutgoingMessageUpdates.SaveWithRetryAsync` then reads the row again and re-applies the change.

The domain also helps: `OutgoingMessage.Advance` never moves a status backwards, because ticks can arrive out of order (a "read" before the "delivered").

**Where.** `Domain/Entities/OutgoingMessage.cs`, `Application/Features/WhatsApp/OutgoingMessageUpdates.cs`, `Infrastructure/Persistence/Configurations/WhatsAppConfigurations.cs`.

**Try it.** Read `OutgoingMessageTests.cs` in the Application tests.

## 34. Starting the gateway with F5: hosted services

**What it is.** An `IHostedService` is code ASP.NET Core runs when the app starts and stops (`StartAsync`, `StopAsync`). `GatewayDevLauncher` is one: in Development it starts `node src/main.ts` in the gateway folder and passes its settings as environment variables. It does not start the gateway if the secrets are missing, `node_modules` is missing, or a gateway is already running. It closes the gateway's standard input when the API stops, which makes the gateway stop cleanly.

It is registered only when `builder.Environment.IsDevelopment()`, in `Program.cs`. On a server the gateway runs as its own service.

**Where.** `Api/Development/GatewayDevLauncher.cs`.

## 35. TypeScript in Node.js without a build step

**What it is.** Node.js 22.18 and later can run `.ts` files directly: it removes the types and runs the JavaScript that is left. So the gateway has no build step: `npm start` is `node src/main.ts`. The types are still checked by `npm run typecheck` (TypeScript's compiler, `tsc --noEmit`).

The rule that comes with it: only TypeScript that can simply be erased is allowed (no `enum`, no `namespace`, no constructor parameter properties). `tsconfig.json` sets `erasableSyntaxOnly` so the compiler reports any of these.

**Where.** `whatsappautomation.gateway/package.json`, `tsconfig.json`.

# Contacts and broadcasts

## 36. Uploading a file: IFormFile and streams

**What it is.** The import screen sends the file as `multipart/form-data`. ASP.NET Core gives it to the controller as an `IFormFile`. The controller opens it as a `Stream` and passes the stream (not the bytes) to `ImportContactsCommand`, so the Application layer does not know about HTTP. `[RequestSizeLimit]` refuses files over 5 MB before they are read.

The import runs twice: first with `apply=false` (check every row, save nothing, show the result), then with `apply=true` when you press Import. Both runs use the same code, so what you saw is what gets saved. All rows are saved with **one** `SaveChangesAsync`, so an import is all or nothing.

**Where.** `Api/Controllers/ContactsController.cs`, `Application/Features/Contacts/Import/`, `Infrastructure/Contacts/ContactSpreadsheet.cs` (ClosedXML reads and writes Excel).

## 37. Many-to-many: contacts and groups

**What it is.** A contact can be in many groups and a group has many contacts. In the database that needs a third table, `contact_group_members`, with one row per (group, contact). EF Core makes and fills that table for you when both classes have a list of the other (`Contact.Groups`, `ContactGroup.Contacts`); `ContactConfigurations` only names it.

**Where.** `Infrastructure/Persistence/Configurations/ContactConfigurations.cs`.

## 38. Work in the background: BackgroundService and PeriodicTimer

**What it is.** A broadcast is sent slowly, over hours, even with the browser closed. `BroadcastSenderService` is a `BackgroundService` (a hosted service, see section 34) that wakes every 5 seconds with a `PeriodicTimer`. Each time it creates a new **scope** (`IServiceScopeFactory.CreateAsyncScope()`), because the database context is scoped and must not live for the whole app. Inside, `BroadcastDispatcher.RunOnceAsync` sends at most one message per user, if the rules allow.

The pacing state ("next message not before 10:42", "8 sent since the last rest") is kept in memory in `BroadcastPacing`, a singleton. After a restart it starts fresh, which is safe: it only means the first message waits the normal delay.

**Where.** `Infrastructure/Broadcasts/BroadcastSenderService.cs`, `Application/Features/Broadcasts/BroadcastDispatcher.cs`, `BroadcastPacing.cs`. The rules are explained in the README, "Broadcasts: how sending is kept safe".

**Try it.** In `appsettings.Development.json` set `"Broadcasting": { "MinDelaySeconds": 5, "MaxDelaySeconds": 10, "SendFrom": "00:00", "SendUntil": "00:00" }` to watch a test broadcast to your own numbers quickly. Never use such short delays for real broadcasts.

## 39. Planning before sending: preview = create

**What it is.** `BroadcastPlanner` decides, for each chosen contact, the language, the final text (with `{name}` and `{salutation}` filled in) or why they are skipped. The preview endpoint and the create command call the same planner, so the composer's preview is exactly what will be sent. The planner is plain C# with no database, which makes it easy to unit test.

**Where.** `Application/Features/Broadcasts/BroadcastPlanner.cs`, `features/broadcasts/composer.page.ts` in the web app (it asks for a new preview 500 ms after you stop typing, a "debounce").

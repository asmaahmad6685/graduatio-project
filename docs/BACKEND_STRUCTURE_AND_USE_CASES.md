# PhysioGuide — Backend Structure & Use Cases

> **Scope.** This document describes the ASP.NET Core backend that lives in `gysi-proj/`.
> Repository: branch `Final-Backend`, HEAD `9e13beb` at the time of writing.
> Everything below was derived by reading the source (entry point, EF model, every controller,
> every service, the seeder, the migrations and the test projects), not from a template.
> Where the code and an older document disagree, this document follows the code.
>
> Companion document: `PhysioGuide-Frontend/docs/FRONTEND_STRUCTURE_AND_USE_CASES.md`.

---

## Table of contents

1. [At a glance](#1-at-a-glance)
2. [Repository and folder structure](#2-repository-and-folder-structure)
3. [Startup pipeline (`Program.cs`)](#3-startup-pipeline-programcs)
4. [Cross-cutting conventions](#4-cross-cutting-conventions)
5. [Data model — 29 tables](#5-data-model--29-tables)
6. [Migrations](#6-migrations)
7. [HTTP API reference](#7-http-api-reference)
8. [Services and domain rules](#8-services-and-domain-rules)
9. [Realtime (SignalR)](#9-realtime-signalr)
10. [Seeding and demo accounts](#10-seeding-and-demo-accounts)
11. [Use cases by actor](#11-use-cases-by-actor)
12. [State machines](#12-state-machines)
13. [Validation and error catalogue](#13-validation-and-error-catalogue)
14. [Security model](#14-security-model)
15. [Known gaps and inconsistencies](#15-known-gaps-and-inconsistencies)
16. [Tests](#16-tests)
17. [Endpoint index](#17-endpoint-index)

---

## 1. At a glance

| Aspect | Value |
| --- | --- |
| Runtime | .NET 8 (`net8.0`), ASP.NET Core Web API |
| Solution | `gysi-proj/graduatio project.sln` — contains only the web project; test projects are separate `.csproj` files |
| Web project | `gysi-proj/graduatio project/graduatio project.csproj` (root namespace `graduatio_project`) |
| Database | SQL Server through EF Core 8.0.31 (`Microsoft.EntityFrameworkCore.SqlServer`) |
| Auth | JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.31) + opaque refresh tokens |
| Realtime | SignalR hub mapped at `/hubs/messages` |
| Docs | Swagger / Swashbuckle 6.6.2, published in **all** environments at `/swagger` |
| Google sign-in | `Google.Apis.Auth` 1.77.0, ID-token validation with nonce + registration-ticket flow |
| Uploads | Local disk under `wwwroot/uploads`, served by static files |
| Serialization | System.Text.Json, default web camelCase, plus `FlexibleIntConverter` / `FlexibleNullableIntConverter` so IDs may arrive as numbers **or** strings |
| Dev ports | HTTP `http://localhost:5222`, HTTPS `https://localhost:7133` (`Properties/launchSettings.json`) |
| Dev database | `(localdb)\MSSQLLocalDB`, database `PhysioGuideDB` |
| Time zone | Business scheduling uses `Asia/Hebron` through `SchedulingClock`; audit timestamps are UTC |

### Packages (`graduatio project.csproj`)

- `Google.Apis.Auth` 1.77.0
- `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.31
- `Microsoft.EntityFrameworkCore.Design` 8.0.31 (private assets)
- `Microsoft.EntityFrameworkCore.SqlServer` 8.0.31
- `Microsoft.EntityFrameworkCore.Tools` 8.0.31 (private assets)
- `Swashbuckle.AspNetCore` 6.6.2

### How to run

```bash
cd "gysi-proj/graduatio project"
dotnet run                 # uses the "http" launch profile, opens /swagger
# or
dotnet run --launch-profile https
```

Requirements:

- SQL Server LocalDB (or change `ConnectionStrings:PhysioGuideConnection`).
- `Jwt:Key` of at least **32 UTF-8 bytes**. In Development it comes from User Secrets
  (`UserSecretsId` is set in the `.csproj`); in deployment use `Jwt__Key`.
- The app applies migrations and seeds demo data automatically on startup (see §3).

---

## 2. Repository and folder structure

```text
gysi-proj/
├── .github/                         CI configuration
├── docs/                            project documentation (booking, assessment, availability, API contract)
│   ├── Appointments-Booking.md
│   ├── Assessment-only-migration.sql
│   ├── Assessment.md
│   ├── Availability-Schedules.md
│   ├── api-contract.md
│   ├── openapi.json
│   ├── PhysioGuide.postman_collection.json  Postman collection, one folder per controller
│   └── BACKEND_STRUCTURE_AND_USE_CASES.md   ← this file
├── graduatio project.sln            solution with the single web project
├── graduatio project/               the API
│   ├── Controllers/                 16 controller files: 15 concrete controllers + 1 abstract base
│   ├── Data/
│   │   ├── ApplicationDbContext.cs  29 DbSets, relationships, indexes, constraints
│   │   └── DbSeeder.cs              idempotent demo data + accounts
│   ├── DTOs/                        request/response contracts grouped by feature
│   │   ├── Admin/                   AdminDtos.cs, ReviewCenterApplicationDto.cs
│   │   ├── Appointments/            request/response/cancel/reject/reschedule/create DTOs
│   │   ├── Assessment/              definition, submission, result DTOs
│   │   ├── Auth/                    patient/center register, login, refresh, change password,
│   │   │                            Google DTOs
│   │   ├── Availability/            query/response (legacy availability service)
│   │   ├── Center/                  CenterWorkspaceDtos.cs (all center-workspace payloads)
│   │   ├── Messaging/               send message, open conversation, mark read
│   │   ├── Patient/                 account, profile, review, saved-center DTOs
│   │   ├── Schedules/               schedule window DTOs
│   │   ├── Service/                 create/update/response service DTOs
│   │   └── Therapist/               profile + workspace DTOs
│   ├── Hubs/
│   │   └── MessagingHub.cs          `[Authorize]` SignalR hub (no server methods)
│   ├── Migrations/                  16 EF Core migrations (see §6)
│   ├── Models/                      29 entity classes (29 tables, incl. 3 join entities)
│   ├── Options/
│   │   └── GoogleAuthOptions.cs     GoogleAuth configuration + validation
│   ├── Properties/launchSettings.json
│   ├── Services/                    25 domain/infrastructure services (see §8)
│   ├── wwwroot/uploads/             uploaded images (served at /uploads/...)
│   ├── Program.cs                   composition root and middleware pipeline
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── appsettings.Production.json
│   ├── PhysioGuide_Google_React_Handoff.md
│   ├── graduatio project.http       sample requests
│   └── graduatio project.csproj
└── tests/
    ├── PhysioGuide.Appointments.Tests/  booking, workflow, concurrency, reschedule/completion
    ├── PhysioGuide.Assessment.Tests/    assessment API + care-rule precedence
    └── PhysioGuide.Availability.Tests/  availability + schedule APIs
```

### Controllers (17)

| Controller | Route prefix | Role gate |
| --- | --- | --- |
| `AuthController` | `api/auth` | anonymous (register/login/refresh), authenticated (logout/change-password) |
| `GoogleAuthController` | `api/Auth/google` | anonymous, rate-limited |
| `AppointmentsController` | `api/appointments` | `[Authorize]`, Patient enforced in code |
| `PatientController` | `api/patient` | `Patient` |
| `CentersController` | `api/centers` | anonymous |
| `CenterController` | `api/center` | `Center` |
| `CenterWorkspaceController` | `api/center` | `Center` |
| `SchedulesController` | `api/schedules` | `Center` |
| `TherapistController` | `api/therapist` | `Therapist` |
| `TherapistWorkspaceController` | `api/therapist` | `Therapist` |
| `ConversationsController` | `api/conversations` | `[Authorize]`, participant-scoped |
| `NotificationsController` | `api/notifications` | `[Authorize]` |
| `AssessmentController` | `api/assessment` | `Patient` |
| `AdminController` | `api/admin/center-applications` | `Admin` |
| `AdminManagementController` | `api/admin` | `Admin` |

---

## 3. Startup pipeline (`Program.cs`)

The whole application is composed in `Main`; there is no `Startup` class.

### 3.1 Service registration, in order

1. **Controllers + JSON**
   - `AddControllers()`.
   - Registers `FlexibleIntConverter` and `FlexibleNullableIntConverter`: request bodies/query
     values may carry IDs as `"12"` or `12`; responses always serialize as numbers.
2. **DbContext**
   - `UseSqlServer(Configuration.GetConnectionString("PhysioGuideConnection"))`.
3. **Domain services**
   - Scoped: `PasswordService`, `TokenService`, `AssessmentService`, `ScheduleService`,
     `AvailabilityService`, `SchedulingWriteCoordinator`, `AppointmentService`,
     `NotificationService`, `RefreshTokenService`.
   - Singletons: `TimeProvider.System` (registered twice), `SchedulingClock`,
     `GoogleNonceStore`, `IGoogleTokenValidator → GoogleTokenValidator`,
     `GoogleRegistrationTicketStore`.
4. **SignalR** — `AddSignalR()`.
5. **Google options**
   - Binds `GoogleAuth` section to `GoogleAuthOptions`.
   - `Validate`: disabled is fine; when enabled, `WebClientId` must look like a Google Web
     Client ID, and `ValidateOnStart()` makes bad configuration fail at boot.
6. **Rate limiting — first `AddRateLimiter`**
   - `RejectionStatusCode = 429`.
   - Policy `GoogleAuth`: fixed window, **20 requests/minute/IP**, `QueueLimit = 0`,
     `Retry-After: 60`, JSON `{ message, code: "rate_limit_exceeded" }`.
7. **JWT key guard**
   - Throws `InvalidOperationException` when `Jwt:Key` is missing or shorter than 32 UTF-8 bytes.
8. **Authentication**
   - Default scheme JwtBearer; validates issuer, audience, lifetime and signing key.
   - `OnMessageReceived`: for paths starting with `/hubs`, reads the token from the
     `access_token` query string (WebSocket/SSE cannot send an `Authorization` header).
   - `OnAuthenticationFailed`: marks `TokenExpired` when a `SecurityTokenExpiredException` occurs.
   - `OnChallenge`: suppresses the default response and writes
     `401 { code: "token_expired", message: ... }` or
     `401 { code: "unauthenticated", message: ... }`.
   - `OnForbidden`: writes `403 { code: "forbidden", message: ... }`.
9. **Swagger** with a `Bearer` security definition and a global security requirement.
10. **CORS** — policy `AllowAll`: any origin, method and header (development convenience; see §14).
11. **Rate limiting — second `AddRateLimiter`**
    - Adds named fixed-window policies:
      `auth-login` **10/min** and `auth-register` **5/min**, both with `QueueLimit = 0`.
    - Replaces `OnRejected` with the generic
      `429 { code: "rate_limited", message: "Too many requests. Try again later." }` and
      `Retry-After` in seconds.
    - Because ASP.NET Core accumulates limiter options, the policies from both calls coexist;
      the second rejection callback wins for every policy, including `GoogleAuth`.

### 3.2 Middleware pipeline, in order

```text
UseExceptionHandler   → 500 { code: "internal_error" }, details only in logs
UseCors("AllowAll")
UseRateLimiter        (first call)
UseSwagger / UseSwaggerUI
UseStaticFiles        (serves wwwroot, including /uploads)
UseHttpsRedirection
UseRouting
UseRateLimiter        (second, redundant call)
UseAuthentication
UseAuthorization
MapControllers
MapHub<MessagingHub>("/hubs/messages")
MapGet("/") → "Physio Guide API is running!"
```

There is no `UseStatusCodePages`, no HSTS configuration, no health-check endpoint and no
global request logging beyond the exception handler.

### 3.3 Startup database work

Both blocks run **after** the pipeline is built and **before** `app.Run()`:

1. **Migrations**
   ```csharp
   using var scope = app.Services.CreateScope();
   var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
   db.Database.Migrate();
   ```
   Wrapped in `try/catch`: a failure prints `[Migrate] Failed: ...` and the API still starts.

2. **Seeding**
   ```csharp
   DbSeeder.SeedAsync(db, passwordService).GetAwaiter().GetResult();
   ```
   Wrapped in `try/catch`: a failure prints `[Seed] Skipped: ...` and the API still starts.

Consequence: a broken connection string produces a running API whose endpoints fail at query
time rather than at boot. The console is the only signal.

### 3.4 Configuration keys

`appsettings.json` (values shown are the effective non-secret defaults):

| Key | Meaning |
| --- | --- |
| `ConnectionStrings:PhysioGuideConnection` | SQL Server connection string |
| `Jwt:Key` | HMAC signing key, ≥ 32 UTF-8 bytes (User Secrets / `Jwt__Key`) |
| `Jwt:Issuer` | `PhysioGuideAPI` |
| `Jwt:Audience` | `PhysioGuideClient` |
| `Jwt:ExpireMinutes` | `15` |
| `Jwt:RefreshDays` | `7` |
| `GoogleAuth:Enabled` | `true` in the checked-in development settings |
| `GoogleAuth:WebClientId` | Google OAuth Web Client ID |

`appsettings.Development.json` targets
`Server=(localdb)\MSSQLLocalDB;Database=PhysioGuideDB;Trusted_Connection=True;...`.

---

## 4. Cross-cutting conventions

### 4.1 Error envelope

Every hand-written error uses `Services/ApiError.cs` and the shape:

```json
{ "code": "snake_case_code", "message": "Human readable message." }
```

Some errors add a `details` object (for example the `center_has_therapists` conflict reports
`{ "therapists": 3 }`). Validation failures produced by `[ApiController]` use the standard
`ValidationProblemDetails` shape: `{ "errors": { "Field": ["message"] } }` with status 400.

Fixed infrastructure responses:

| Situation | Status | Body |
| --- | --- | --- |
| Missing/invalid token | 401 | `{ code: "unauthenticated" }` |
| Expired token | 401 | `{ code: "token_expired" }` |
| Wrong role | 403 | `{ code: "forbidden" }` |
| Rate limit | 429 | `{ code: "rate_limited" }` (or `rate_limit_exceeded` in the first callback) |
| Unhandled exception | 500 | `{ code: "internal_error" }` |

### 4.2 Identity and ownership

- JWT claims: exactly two — `ClaimTypes.NameIdentifier` (numeric user id) and `ClaimTypes.Role`
  (`Patient` / `Center` / `Therapist` / `Admin`). Email and profile ids are returned in the login body,
  not embedded in the token.
- `CurrentUser` helpers: `IsRole`, `TryGetUserId`, `GetUserId`.
- Every controller that mutates data resolves the caller's profile row from the JWT user id and
  then checks ownership of the target row. Cross-tenant access returns **404**, not 403, so the
  existence of another tenant's record is not leaked (verified by the appointment workflow tests).
- `[Authorize(Roles = "...")]` is used for hard role gates; several controllers additionally
  re-check the role in code so that a token with a stale or malformed identity cannot slip through.

### 4.3 IDs and payload shapes

- Route IDs are integers (`{id:int}`), but JSON request IDs accept strings thanks to the flexible
  converters.
- Response IDs are usually **strings** (`appointment.AppointmentId.ToString()`), because the React
  client treats all entity IDs as strings. Collections sometimes use numeric IDs; the client's
  mappers normalize both.
- Dates/times in responses are ISO-8601 (`Iso(...)` helper) — `DateTime` values are stored and
  returned as UTC for audit fields, while appointment `StartDateTime`/`EndDateTime` are wall-clock
  values interpreted in the center's local scheduling context.
- JSON property naming is camelCase.

### 4.4 Status codes used

| Code | Used for |
| --- | --- |
| 200 | successful read/update/action |
| 201 | register, create therapist/appointment/service/patient/image |
| 204 | delete operations that return no body |
| 400 | validation, invalid transition, invalid credentials payload |
| 401 | missing/expired token, wrong current password |
| 403 | role mismatch, disabled account, unapproved center |
| 404 | missing resource **or** foreign resource |
| 409 | duplicate email, duplicate review, slot conflict, concurrent scheduling conflict, center-has-therapists |
| 429 | rate limited |
| 500 | unhandled exception |
| 503 | Google sign-in unavailable (feature disabled or validator unavailable) |

### 4.5 Uploads

`ImageStorage` is the single gate for image files:

- Max size **2 MB**.
- Allowed content types/extensions: `image/jpeg`, `image/jpg`, `image/png`, `image/webp`, `image/gif`.
- Saves to `wwwroot/uploads/{guid}{ext}` and returns `/uploads/{guid}{ext}`.
- Used by patient profile image, therapist profile image, center workspace images, admin settings image.
- No virus scanning, no image re-encoding, no per-user quota.

### 4.6 Time and scheduling

- `SchedulingClock` resolves `Asia/Hebron` and converts `DateTimeOffset`/UTC to local scheduling
  time with a DST guard (an invalid/ambiguous local time is rejected rather than silently shifted).
- Appointment slots are computed on a **30-minute grid**.
- Treatment length is driven by the selected service's `DurationMinutes`; `SchedulingRules`
  additionally models a 50-minute treatment + 10-minute break pattern for the seeded data.
- `SchedulingWriteCoordinator` serializes schedule writes with SQL Server `sp_getapplock`, so two
  concurrent requests cannot create overlapping appointments or schedule windows.

---

## 5. Data model — 29 tables

`ApplicationDbContext` exposes 29 `DbSet`s backed by 29 model classes (`TherapistService`,
`CenterPatient` and `PatientSavedCenter` are pure join entities). There are **no** database check
constraints; validation lives in DTOs, controllers and services. All string lengths below come
from the fluent configuration.

### 5.1 `Users`

Account root for every role.

| Column | Type | Notes |
| --- | --- | --- |
| `UserId` | int PK | identity |
| `Email` | nvarchar(256), required | **unique index** |
| `PasswordHash` | nvarchar(max), nullable | null for Google-only accounts |
| `PhoneNumber` | nvarchar(20), required | |
| `Role` | nvarchar(20), required | `Patient`, `Center`, `Therapist`, `Admin` |
| `IsActive` | bit | false blocks login and authenticated actions |
| `CreatedAt` | datetime2 | UTC |
| `UpdatedAt` | datetime2?, nullable | UTC |

### 5.2 `ExternalLogins`

| Column | Type | Notes |
| --- | --- | --- |
| `ExternalLoginId` | int PK | |
| `UserId` | int FK → Users | |
| `Provider` | nvarchar | e.g. `Google` |
| `ProviderSubject` | nvarchar | Google `sub` |
| `CreatedAt` | datetime2 | |

Unique index `(Provider, ProviderSubject)` and index `(UserId, Provider)`.

### 5.3 `Patients`

| Column | Type | Notes |
| --- | --- | --- |
| `PatientId` | int PK | |
| `UserId` | int FK → Users, 1:1 | |
| `FullName` | nvarchar(150), required | |
| `Gender` | nvarchar(20), nullable | `male` / `female` / `other` in account updates; registration only allows male/female |
| `ProfileImageUrl` | nvarchar(500), nullable | |
| `DateOfBirth` | date, nullable | cannot be in the future (validated) |
| `City` | nvarchar(100), nullable | |

### 5.4 `PatientSavedCenters`

Favourite centers (patient-side bookmarks).

| Column | Type | Notes |
| --- | --- | --- |
| `PatientId` | int | composite PK part, FK → Patients |
| `CenterId` | int | composite PK part, FK → Centers |
| `SavedAt` | datetime2 | |

`(PatientId, CenterId)` is the key, so a center can be saved only once per patient.

### 5.5 `CenterPatients`

Explicit center↔patient relationship. A patient becomes "known" to a center when the center
creates the patient record, when the patient books there, or when staff add them.

| Column | Type | Notes |
| --- | --- | --- |
| `CenterId` | int | composite PK part, FK → Centers |
| `PatientId` | int | composite PK part, FK → Patients |
| `CreatedAt` | datetime2 | |

### 5.6 `Centers`

| Column | Type | Notes |
| --- | --- | --- |
| `CenterId` | int PK | |
| `UserId` | int FK → Users, 1:1 | |
| `NameArabic` | nvarchar(200), required | |
| `NameEnglish` | nvarchar(200), nullable | |
| `DescriptionArabic` | nvarchar(1500), nullable | |
| `DescriptionEnglish` | nvarchar(1500), nullable | |
| `City` | nvarchar(100), required | |
| `Address` | nvarchar(300), required | |
| `AddressArabic` | nvarchar(300), nullable | added 2026-11-12 |
| `ProfileImageUrl` | nvarchar(500), nullable | |
| `WebsiteUrl` | nvarchar(500), nullable | |
| `Status` | nvarchar(30), required | `Pending`, `Approved`, `Rejected`, `Suspended` (seed also uses `Active` for older rows) |
| `RejectionReason` | nvarchar(500), nullable | |
| `ApprovedByAdminId` | int? FK → Admins | |
| `ApprovedAt` | datetime2?, nullable | |
| `CreatedAt` / `UpdatedAt` | datetime2 | |
| `Latitude` / `Longitude` | float?, nullable | map coordinates |
| `LocationConfirmed` | bit | user confirmed the pin |
| `ReviewedAt` | datetime2?, nullable | |

### 5.7 `Admins`

| Column | Type | Notes |
| --- | --- | --- |
| `AdminId` | int PK | |
| `UserId` | int FK → Users, 1:1 | |
| `FullName` | nvarchar(150), required | |
| `ProfileImageUrl` | nvarchar(500), nullable | |

### 5.8 `Specialties`

| Column | Type | Notes |
| --- | --- | --- |
| `SpecialtyId` | int PK | |
| `NameArabic` | nvarchar(150), required | |
| `NameEnglish` | nvarchar(150), nullable | |
| `DescriptionArabic` / `DescriptionEnglish` | nvarchar(1000), nullable | |
| `IsActive` | bit | |

### 5.9 `Services`

| Column | Type | Notes |
| --- | --- | --- |
| `ServiceId` | int PK | |
| `CenterId` | int FK → Centers | |
| `SpecialtyId` | int FK → Specialties | |
| `NameArabic` | nvarchar(200), required | |
| `NameEnglish` | nvarchar(200), nullable | |
| `DescriptionArabic` / `DescriptionEnglish` | nvarchar(1500), nullable | |
| `Price` | decimal(10,2) | |
| `DurationMinutes` | int | drives slot length (5–600 in DTO) |
| `ImageUrl` | nvarchar(500), nullable | |
| `IsActive` | bit | inactive services are hidden from discovery/booking |
| `CreatedAt` | datetime2 | |

### 5.10 `Therapists`

| Column | Type | Notes |
| --- | --- | --- |
| `TherapistId` | int PK | |
| `UserId` | int FK → Users, 1:1 | |
| `CenterId` | int FK → Centers | |
| `SpecialtyId` | int FK → Specialties | |
| `FullName` | nvarchar(150), required | |
| `FullNameAr` | nvarchar(150), nullable | added 2026-11-12 |
| `Bio` | nvarchar(1000), nullable | |
| `BioAr` | nvarchar(1000), nullable | added 2026-11-12 |
| `ProfileImageUrl` | nvarchar(500), nullable | |
| `YearsOfExperience` | int?, nullable | |
| `Qualifications` | nvarchar(1000), nullable | comma/newline separated |
| `Languages` | nvarchar(300), nullable | |
| `IsAvailable` | bit | shown as accepting patients |
| `IsActive` | bit | false disables login effects / hides from lists |
| `CreatedAt` | datetime2 | |

### 5.11 `TherapistServices`

Many-to-many between therapists and the services they perform.

| Column | Type | Notes |
| --- | --- | --- |
| `TherapistId` | int | composite PK, FK → Therapists |
| `ServiceId` | int | composite PK, FK → Services |

### 5.12 `CenterWorkingHours`

One row per center per day.

| Column | Type | Notes |
| --- | --- | --- |
| `WorkingHourId` | int PK | |
| `CenterId` | int FK → Centers | |
| `DayOfWeek` | int (enum) | Sunday = 0 … Saturday = 6 |
| `OpeningTime` / `ClosingTime` | time, nullable | |
| `IsClosed` | bit | |

Unique index `(CenterId, DayOfWeek)`.

### 5.13 `TherapistSchedules`

Recurring availability windows for a therapist.

| Column | Type | Notes |
| --- | --- | --- |
| `ScheduleId` | int PK | |
| `TherapistId` | int FK → Therapists | |
| `DayOfWeek` | int (enum) | |
| `StartTime` / `EndTime` | time | |
| `IsAvailable` | bit | |

Overlap prevention is enforced by `ScheduleService`, not by a DB constraint.

### 5.14 `CenterImages`

| Column | Type | Notes |
| --- | --- | --- |
| `CenterImageId` | int PK | |
| `CenterId` | int FK → Centers | |
| `ImageUrl` | nvarchar(500), required | |
| `IsPrimary` | bit | at most one primary is maintained in code |
| `DisplayOrder` | int | |

### 5.15 `CenterDocuments`

Licence/verification documents (upload path exists in the model; the workspace API doesn't expose
a document uploader in this build).

| Column | Type | Notes |
| --- | --- | --- |
| `CenterDocumentId` | int PK | |
| `CenterId` | int FK → Centers | |
| `DocumentType` | nvarchar(100), required | |
| `FileUrl` | nvarchar(500), required | |
| `UploadedAt` | datetime2 | |

### 5.16 `Appointments`

| Column | Type | Notes |
| --- | --- | --- |
| `AppointmentId` | int PK | |
| `PatientId` | int FK → Patients | |
| `CenterId` | int FK → Centers | |
| `TherapistId` | int FK → Therapists | |
| `ServiceId` | int FK → Services | |
| `StartDateTime` / `EndDateTime` | datetime2 | wall-clock center time |
| `PriceAtBooking` | decimal(10,2) | snapshot of the service price |
| `PatientNotes` | nvarchar(1000), nullable | |
| `Status` | nvarchar(30), required | `Pending`, `Confirmed`, `Completed`, `Cancelled`, `Rejected` |
| `RejectionReason` | nvarchar(500), nullable | |
| `CancellationReason` | nvarchar(500), nullable | |
| `CreatedAt` / `UpdatedAt` | datetime2 | |

### 5.17 `AppointmentStatusHistories`

Audit trail for every status transition.

| Column | Type | Notes |
| --- | --- | --- |
| `AppointmentStatusHistoryId` | int PK | |
| `AppointmentId` | int FK → Appointments | |
| `OldStatus` | nvarchar, nullable | null for the initial row |
| `NewStatus` | nvarchar | |
| `ChangedByUserId` | int? FK → Users | |
| `ChangedAt` | datetime2 (UTC) | |

### 5.18 `Reviews`

One review per completed appointment.

| Column | Type | Notes |
| --- | --- | --- |
| `ReviewId` | int PK | |
| `PatientId` | int FK → Patients | |
| `CenterId` | int FK → Centers | |
| `AppointmentId` | int FK → Appointments | **unique index** |
| `Rating` | int | 1–5 |
| `Comment` | nvarchar(1000), nullable | |
| `IsVisible` | bit | admin moderation hides without deleting |
| `CreatedAt` / `UpdatedAt` | datetime2 | |
| `Reply` | nvarchar(1000), nullable | center reply |
| `RepliedAt` | datetime2? | |

### 5.19 `Notifications`

In-app bell rows.

| Column | Type | Notes |
| --- | --- | --- |
| `NotificationId` | int PK | |
| `UserId` | int FK → Users | |
| `Type` | nvarchar(100), required | e.g. `appointment`, `message`, `review`, `system` |
| `Title` | nvarchar(200), required | |
| `Message` | nvarchar(1000), required | |
| `RelatedEntityId` | int?, nullable | appointment/conversation id |
| `IsRead` | bit | |
| `CreatedAt` | datetime2 (UTC) | |

### 5.20 `Conversations`

Exactly one conversation per patient↔therapist pair.

| Column | Type | Notes |
| --- | --- | --- |
| `ConversationId` | int PK | |
| `PatientId` | int FK → Patients | |
| `TherapistId` | int FK → Therapists | |
| `CreatedAt` | datetime2 | |

Unique index `(PatientId, TherapistId)`.

### 5.21 `Messages`

| Column | Type | Notes |
| --- | --- | --- |
| `MessageId` | int PK | |
| `ConversationId` | int FK → Conversations | |
| `SenderUserId` | int FK → Users | |
| `MessageText` | nvarchar(2000), required | empty allowed only when an attachment exists (controller rule) |
| `IsRead` | bit | |
| `AttachmentUrl` | nvarchar(500), nullable | |
| `SentAt` | datetime2 (UTC) | |

### 5.22 `PasswordResetTokens`

Present but unreachable: password recovery endpoints are `[NonAction]`.

| Column | Type | Notes |
| --- | --- | --- |
| `PasswordResetTokenId` | int PK | |
| `UserId` | int FK → Users | |
| `TokenHash` | nvarchar(500), required | |
| `ExpiresAt` | datetime2 | |
| `UsedAt` | datetime2? | |
| `CreatedAt` | datetime2 | |

### 5.23 `RefreshTokens`

| Column | Type | Notes |
| --- | --- | --- |
| `RefreshTokenId` | int PK | |
| `UserId` | int FK → Users, cascade delete | |
| `TokenHash` | nvarchar(64), required | **unique index**, SHA-256 hex |
| `CreatedAt` | datetime2 | |
| `ExpiresAt` | datetime2 | now + 7 days |
| `RevokedAt` | datetime2? | |
| `ReplacedByTokenHash` | nvarchar?, nullable | rotation chain |
| `DeviceInfo` | nvarchar(200), nullable | |

### 5.24 `NotificationPreferences`

One row per user (unique index `UserId`).

| Column | Type | Notes |
| --- | --- | --- |
| `NotificationPreferenceId` | int PK | |
| `UserId` | int FK → Users, cascade | |
| `EmailEnabled` | bit | default true |
| `InAppEnabled` | bit | default true |
| `AppointmentReminders` | bit | default true |
| `Marketing` | bit | default false |
| `Language` | nvarchar(10), required | `en` / `ar` |

### 5.25 `PatientNotes`

Private center-staff notes about a patient.

| Column | Type | Notes |
| --- | --- | --- |
| `PatientNoteId` | int PK | |
| `CenterId` | int FK → Centers, restrict | |
| `PatientId` | int FK → Patients, restrict | |
| `AuthorUserId` | int FK → Users | |
| `Text` | nvarchar(2000), required | |
| `CreatedAt` | datetime2 (UTC) | |

Index `(CenterId, PatientId)`.

### 5.26 Assessment tables

**`AssessmentQuestions`**

| Column | Type |
| --- | --- |
| `QuestionId` int PK | |
| `QuestionArabic` nvarchar | |
| `QuestionEnglish` nvarchar | |
| `DisplayOrder` int | |
| `IsActive` bit | |

**`AssessmentOptions`**

| Column | Type | Notes |
| --- | --- | --- |
| `OptionId` int PK | | |
| `QuestionId` int FK | | |
| `TextArabic` / `TextEnglish` | nvarchar | |
| `SpecialtyId` int? FK → Specialties | | |
| `Score` int? | | |

**`Assessments`**

| Column | Type |
| --- | --- |
| `AssessmentId` int PK | |
| `PatientId` int FK | |
| `SuggestedSpecialtyId` int? FK → Specialties | |
| `CreatedAt` datetime2 | |

**`AssessmentAnswers`**

| Column | Type |
| --- | --- |
| `AssessmentAnswerId` int PK | |
| `AssessmentId` int FK | |
| `QuestionId` int FK | |
| `OptionId` int? FK | |
| `NumericValue` int? | |
| `OtherDescription` nvarchar? | |

### 5.27 Delete behaviour summary

`ApplicationDbContext` configures `Restrict` for references that protect clinical/business history
(appointments → patient/center/therapist/service, reviews, notes, status history) and `Cascade`
for rows that belong to a parent (user → profile/refresh tokens/preferences, center → images,
appointment → status history, etc.). Because `Restrict` would block hard deletes, the admin
delete endpoints remove dependent rows explicitly inside a transaction (see §7.13.4–7.13.6).

### 5.28 Index summary

| Index | Table | Unique |
| --- | --- | --- |
| `Email` | Users | yes |
| `(Provider, ProviderSubject)` | ExternalLogins | yes |
| `(UserId, Provider)` | ExternalLogins | no |
| `AppointmentId` | Reviews | yes |
| `(CenterId, DayOfWeek)` | CenterWorkingHours | yes |
| `(PatientId, TherapistId)` | Conversations | yes |
| `TokenHash` | RefreshTokens | yes |
| `UserId` | NotificationPreferences | yes |
| `(CenterId, PatientId)` | PatientNotes | no |
| `(PatientId, CenterId)` | PatientSavedCenters (PK) | yes |
| `(CenterId, PatientId)` | CenterPatients (PK) | yes |
| `(TherapistId, ServiceId)` | TherapistServices (PK) | yes |

---

## 6. Migrations

16 migrations, applied in this order (last one at HEAD `9e13beb`):

| # | Migration | What it changes |
| --- | --- | --- |
| 1 | `20261001220637_InitialCreate` | Creates the base 23 tables: Users, Admins, Patients, Centers, Specialties, Services, Therapists, TherapistServices, TherapistSchedules, CenterWorkingHours, CenterImages, CenterDocuments, Appointments, AppointmentStatusHistories, Reviews, Notifications, Conversations, Messages, PasswordResetTokens, AssessmentQuestions, AssessmentOptions, Assessments, AssessmentAnswers (plus indexes). |
| 2 | `20261003134616_AddCenterCoordinates` | `Centers.Latitude`, `Centers.Longitude`. |
| 3 | `20261007094638_AddTherapistAccountAndMessagingChanges` | Adds `Therapists.UserId` + FK/index (therapist logins) and `Conversations.TherapistId` + FK/index; drops the previous therapist–user/conversation columns. |
| 4 | `20261012144759_AddAssessmentAnswerDetails` | `AssessmentAnswers.NumericValue`, `AssessmentAnswers.OtherDescription`. |
| 5 | `20261013110822_AddPatientDateOfBirthAndCity` | `Patients.City` (date of birth already existed in the base schema). |
| 6 | `20261013153819_AddPatientSavedCenters` | Creates `PatientSavedCenters` (patient favourites). |
| 7 | `20261019185323_AddExternalLoginsAndNullablePasswordHash` | Creates `ExternalLogins`; makes `Users.PasswordHash` nullable (Google-only accounts). |
| 8 | `20261026194433_ApiIntegrationSchema` | Creates `RefreshTokens` and `NotificationPreferences`; adds `Patients.DateOfBirth`, `Centers.LocationConfirmed`, `Centers.ReviewedAt`, `Reviews.Reply`, `Reviews.RepliedAt`; drops obsolete columns. |
| 9 | `20261027065958_PatientNotesAndMessageAttachment` | Creates `PatientNotes`; adds `Messages.AttachmentUrl`. |
| 10 | `20261027073447_IntegrateWithFron` | **Empty migration** — no schema operations. |
| 11 | `20261030170957_AdminRelated` | **Empty migration** — no schema operations. |
| 12 | `20261031121612_AddCenterPatients` | Creates `CenterPatients` (explicit center↔patient link). |
| 13 | `20261102110402_AddTherapistArabicNameAndBio` | `Therapists.FullNameAr`, `Therapists.BioAr`. |
| 14 | `20261111222442_AddAdminProfileImage` | `Admins.ProfileImageUrl`. |
| 15 | `20261112114431_AddServiceImage` | `Services.ImageUrl`. |
| 16 | `20261112164016_AddCenterAddressArabic` | `Centers.AddressArabic`. |

Commands:

```bash
cd "gysi-proj/graduatio project"
dotnet ef migrations list
dotnet ef database update
dotnet ef migrations add <Name>
```

At runtime the app calls `Database.Migrate()` itself (§3.3), so manual `database update` is only
needed when you want to see errors directly.

---

## 7. HTTP API reference

Base URL in development: `http://localhost:5222`. All routes below are relative.
Unless stated otherwise, requests and responses are JSON; `Authorization: Bearer <accessToken>` is required.

### 7.1 Authentication — `api/auth`

#### `POST /api/auth/register/patient`

Anonymous, rate limit `auth-register` (5/min).

```json
{
  "fullName": "Lina Ahmad",
  "email": "lina@example.com",
  "phoneNumber": "+970599123456",
  "password": "Secret123",
  "gender": "female"
}
```

| Field | Rules |
| --- | --- |
| `fullName` | required, ≤ 150 |
| `email` | required, valid email, ≤ 256, unique (case-sensitive as stored) |
| `phoneNumber` | required, `^\+?[0-9()\-\s]{7,20}$` |
| `password` | required, ≥ 8 |
| `gender` | optional, `male` / `female` (case-insensitive) |

Behaviour:

1. Rejects a duplicate email with `409 duplicate_email` and an `errors.Email` entry.
2. Creates `Users` (role `Patient`, active) and `Patients` inside one transaction, then commits.
   Notification preferences are **not** created here; they are created lazily by the first preference
   save or returned as defaults by the read endpoints.
3. Returns `200`:
   ```json
   { "message": "Patient registered successfully.", "userId": 12,
     "patientId": "7", "email": "lina@example.com", "role": "patient" }
   ```

No access token is issued; the client signs in afterwards.

#### `POST /api/auth/register/center`

Anonymous, rate limit `auth-register`.

```json
{
  "centerName": "Al Salam Physiotherapy",
  "email": "center@example.com",
  "phoneNumber": "+970599000000",
  "location": "Al Rasheed St, Gaza",
  "city": "Gaza",
  "latitude": 31.5017,
  "longitude": 34.4668,
  "password": "Secret123"
}
```

Behaviour:

1. Duplicate email → `409 duplicate_email`.
2. In one transaction creates `Users` (role `Center`, active), `Centers` with
   `Status = "Pending"`, `NameArabic = centerName`, `Address = location`, optional coordinates and
   `LocationConfirmed` when coordinates are supplied, plus **seven `CenterWorkingHours` rows**: every
   day `08:00–17:00`, Friday `08:00–13:00`, none closed.
3. Notifies every admin (`centerApproval`, related id = center id).
4. Returns `200` with `message`, `userId`, `centerId` (string), `email`, `role: "center"`,
   `status: "Pending"`.
5. The center **cannot sign in** until an admin approves it (§7.1 login rules and §12.1).

#### `POST /api/auth/login?role=<patient|center|admin>`

Anonymous, rate limit `auth-login` (10/min).

```json
{ "email": "patient@physioguide.dev", "password": "Patient123!" }
```

The optional `role` query value selects the portal:

- `center` accepts both `Center` and `Therapist` accounts (therapists sign in through the center portal).
- `patient`, `admin` match the account role exactly.
- A mismatch returns the **same** `401 unauthenticated` as a wrong password, so the account's role
  is not leaked.
- Omitting `role` keeps the legacy behaviour (any role may sign in).

Additional gates:

| Condition | Response |
| --- | --- |
| Unknown email or wrong password | `401 unauthenticated` "Invalid email or password." |
| `Users.IsActive = false` | `403 account_not_active` |
| Center profile missing | `403 center_profile_missing` |
| Center `Pending` | `403 account_not_active` "pending admin approval" |
| Center `Rejected` | `403 account_rejected` + `details.rejectionReason` |
| Center `Suspended` | `403 account_suspended` |
| Therapist missing/inactive | `403 { message }` (no `code`) |
| Therapist's center not approved/active | `403 { message }` (no `code`) |

Success response:

```json
{
  "message": "Login successful.",
  "accessToken": "<jwt>",
  "refreshToken": "<opaque 64-byte base64url>",
  "expiresIn": 900,
  "userId": 4,
  "patientId": "3",
  "centerId": null,
  "therapistId": null,
  "fullName": "Demo Patient",
  "email": "patient@physioguide.dev",
  "role": "patient"
}
```

Role ids are returned as strings; `userId` stays numeric. `fullName` falls back to the email when
no profile row exists. A refresh token is issued through `RefreshTokenService` (hash stored only).

#### `POST /api/auth/refresh`

Anonymous.

```json
{ "refreshToken": "..." }
```

- Valid, unused, unexpired token → rotates it (old row revoked, `ReplacedByTokenHash` set) and returns
  `{ message, accessToken, refreshToken, expiresIn }`.
- Invalid/expired/revoked token → `401 invalid_refresh_token`.
- **Reuse detection:** presenting an already-rotated token revokes **all** active refresh tokens for
  that user (`RevokeAllForUserAsync`) and returns `401`.
- Inactive user → `401 invalid_refresh_token`.

#### `POST /api/auth/logout`

`[Authorize]`.

```json
{ "refreshToken": "optional" }
```

Revokes the token when supplied and always returns `204 No Content` (idempotent). The access token
remains valid until it expires (15 minutes); JWT is stateless.

#### `POST /api/auth/change-password`

`[Authorize]` (any role).

```json
{ "currentPassword": "Old12345", "newPassword": "New12345" }
```

- Wrong current password → `400` validation with `errors.CurrentPassword`.
- Success → stores the new hash, sets `UpdatedAt`, **revokes every refresh token for the user**
  (all other devices must sign in again) and returns `{ message: "Password changed successfully." }`.

### 7.2 Google sign-in — `api/Auth/google`

All four endpoints are anonymous and rate-limited by the `GoogleAuth` policy (20/min/IP).
If Google authentication is disabled or misconfigured, they answer
`503 google_auth_unavailable`.

The flow is deliberately split so a Google account never becomes a PhysioGuide account silently:

1. **`POST /challenge`** — body `{ flowBinding }` where `flowBinding` is the SHA-256 hex of a
   browser-generated 32-byte verifier. Returns a nonce + challenge id (5-minute lifetime,
   in-memory, capped at 1024 entries).
2. Browser obtains a Google ID token for that nonce.
3. **`POST /sign-in`** — body `{ idToken, challengeId, flowVerifier }`.
   - Validates the token signature, audience and nonce, and that the verifier matches the challenge.
   - Existing linked account → returns the same session payload as password login
     (`accessToken`, `refreshToken`, ids, role).
   - Unknown Google identity → returns a **registration ticket** (5 minutes, bound to the verifier)
     instead of creating an account.
4. **`POST /register/patient`** — `{ registrationTicket, flowVerifier, phoneNumber, fullName, gender? }`.
5. **`POST /register/center`** — `{ registrationTicket, flowVerifier, phoneNumber, centerName, city, location, latitude?, longitude? }`.

Registration creates `ExternalLogins` plus the profile; centers are created `Pending` exactly like
password registration. Tickets and nonces are in-memory singletons, so a multi-instance deployment
would need a distributed store.

### 7.3 Public discovery — `api/centers`

Anonymous.

#### `GET /api/centers?city=<text>`

Returns approved, active centers with their public profile, services, therapists, images and
aggregate rating data used by the patient discovery screens. `city` is an optional filter.

Only centers with `Status = "Approved"` (and an active user account) are discoverable; pending,
rejected and suspended centers never appear.

#### `GET /api/centers/{centerId}/slots?therapistId=&serviceId=&date=YYYY-MM-DD`

Computes bookable start times for a therapist/service/day.

- `therapistId` and `serviceId` are required in practice (defaults of `0` produce no slots).
- `date` must be `YYYY-MM-DD`, interpreted in `Asia/Hebron`.
- Slot grid: 30 minutes, starting at the intersection of
  center working hours for that weekday and the therapist's `TherapistSchedules` window for that weekday.
- Each slot occupies `service.DurationMinutes`; slots that would run past closing time are dropped.
- Appointments with status `Pending` or `Confirmed` (including those of the same patient) are treated
  as busy; `Cancelled`, `Rejected` and `Completed` do not block.
- A center that is closed that day, a therapist who is unavailable/inactive, a service from another
  center and a past date all yield an empty list.

### 7.4 Appointment request — `api/appointments`

#### `POST /api/appointments/request`

`[Authorize]`; role is checked in code (`403 forbidden` for non-patients).

```json
{
  "centerId": 1,
  "therapistId": 2,
  "serviceId": 3,
  "date": "2026-12-01",
  "time": "09:30",
  "notes": "Lower back pain"
}
```

Validation and rules (in order):

1. Caller must have a `Patients` row (`404 not_found` otherwise).
2. `date` must parse as `yyyy-MM-dd`; `time` as `HH:mm` (24-hour). Validation errors are returned as a
   `400` problem with field keys `Date` / `Time`.
3. Center, therapist and service must exist and be mutually consistent:
   therapist belongs to the center, service belongs to the center, and the therapist performs the
   service through `TherapistServices`.
4. `BookingRules` must accept the slot: 30-minute grid, within center hours, within the therapist's
   schedule, not in the past, not overlapping an existing `Pending`/`Confirmed` appointment for that
   therapist **or** that patient.
5. A race between two requests is serialized by `SchedulingWriteCoordinator` (`sp_getapplock`); the
   loser gets `409 slot_unavailable` rather than a double booking. A start time in the past also
   returns `409 slot_unavailable`.
6. Creates the appointment with `Status = "Pending"`, `PriceAtBooking = service.Price`,
   `EndDateTime = start + service.DurationMinutes`, `CreatedAt = UtcNow`. **No** status-history row is
   written for the initial request; history begins with the first center/admin transition.
7. Sends one notification of type `appointmentRequest` to **both** the therapist's user account and
   the center's user account; `NotificationService` also pushes the realtime workspace event.

Response: the created appointment (id as string, `status: "pending"`) with `201`.

Only this one action is live in `AppointmentsController`; the cancel/reschedule/complete actions at
the bottom of the file are commented out and are implemented through the center workspace and admin
endpoints instead.

### 7.5 Patient area — `api/patient`

`[Authorize(Roles = "Patient")]` on the whole controller.

#### `GET /api/patient/appointments?status=&from=&to=`

Returns the caller's appointments. Each row is produced by `AppointmentMapper.Build`:

`id`, `patientId`, `therapistId`, `serviceId`, `date` (`yyyy-MM-dd`), `time` (`HH:mm`), `status`
(lower-case), `duration` (minutes), `therapistImage`, `therapistAvailable`, `centerId`, `centerName`,
`serviceName`, `therapistName`, `therapistNameAr`.

- The payload does **not** include the price, patient notes or a “reviewed” flag; the UI derives price
  from the workspace/services and review eligibility from completed appointments.
- `status` filters with a case-insensitive exact match (`pending`, `confirmed`, `completed`,
  `cancelled`, `rejected`, `approved` are all accepted literally).
- `from` / `to` are `YYYY-MM-DD` bounds on `StartDateTime`; invalid values are ignored.
- Results are ordered by start time **descending** (`AppointmentMapper.LoadAsync`).

#### `GET /api/patient/messaging-workspace?since=<iso>`

Aggregate used by the patient's messages screen:

- `patientProfile` and the single-entry `patients` array (the patient themself);
- `therapists` the patient has conversations with (name EN/AR, image, specialty, center, experience,
  bio, availability label);
- `conversations` with therapist identity, center, last message/time, unread count and the messages
  themselves (filtered by `since` when supplied);
- `notifications` (optionally only those newer than `since`) with `id`, `type`, `title`, `message`,
  `isRead`, `date`, `relatedId`; reminder rows are created/pruned on this read, not by a background job;
- `notificationPreferences`.

`since` is parsed as UTC; invalid values are ignored (full payload returned).

#### `GET /api/patient/account`

Returns `{ profile, preferences }`:

- `profile`: `fullName`, `email`, `phoneNumber`, `gender`, `dateOfBirth`, `city`,
  `profileImageUrl` (built by `ProfileDto`);
- `preferences`: the `NotificationPreferences` row (or defaults when none exists).

#### `PUT /api/patient/account/profile`

`UpdateProfileDto`: required `fullName` (≤150), optional `phoneNumber` (≤20, `[Phone]`),
`gender` (`male|female|other`, case-insensitive), `dateOfBirth`, `city` (≤100),
`profileImageUrl` (≤500).

Write semantics: `fullName`/`gender`/`dateOfBirth` are always written (null clears gender/date);
`city`, `profileImageUrl` and `phoneNumber` are only written when the JSON field is present, so a
profile save without an image does not erase it. Response: `{ profile }`.
The backend does **not** independently reject a future date of birth — that rule lives in the
frontend (`profileErrors`) and in the DB type, not in this action.

#### `PUT /api/patient/account/preferences`

`UpdatePreferencesDto`: `emailEnabled`, `inAppEnabled`, `appointmentReminders`, `marketing`,
`language` (`en|ar`). Creates the row on first write (upsert).

#### `POST /api/patient/account/image`

`multipart/form-data` with `file`. Uses `ImageStorage` (2 MB, jpeg/png/webp/gif). Returns the public
`/uploads/...` URL; the profile is not updated automatically — the client sends the URL to the
profile endpoint.

#### `POST /api/patient/reviews`

```json
{ "centerId": 3, "rating": 5, "comment": "Excellent care" }
```

The client does **not** choose the appointment. The server selects the caller's **most recent
`Completed` appointment at that center that has no review yet** and links the review to it.

- Center must exist and be `Approved` (`404` otherwise).
- No eligible completed/unreviewed appointment → `400 review_not_allowed`
  "You can review this center after a completed appointment."
- Rating 1–5 and comment ≤1000 are enforced by the DTO.
- Creates a visible review (`IsVisible = true`) and returns `200` with
  `id`, `centerId`, `rating`, `comment`, `createdAt`.
- **No notification is created** for the center or the admins by this action.
- The unique index on `Reviews.AppointmentId` is what prevents a second review for the same visit.

#### `GET /api/patient/saved-centers`

Returns the caller's favourite center ids/rows (used by discovery to render the heart state).

#### `PUT /api/patient/saved-centers/{centerId}`

Idempotently saves a center (approved centers only). `404` for unknown/unapproved centers.

#### `DELETE /api/patient/saved-centers/{centerId}`

Idempotently removes the favourite (`204`).

### 7.6 Center workspace — `api/center`

`[Authorize(Roles = "Center")]` for both `CenterController` and `CenterWorkspaceController`.
The caller's center is resolved from the JWT; every id in the URL is checked against that center.

#### `GET /api/center/workspace`

The single aggregate the center portal loads (and polls):

- `center`: the mapped center DTO plus `nameArabic`, `descriptionArabic`, `email`, `phone`,
  `approvalStatus`, `reviewedAt`, `reviewReason` (images/working hours are part of the center DTO);
- `therapists`: name EN/AR, image, bios, contact, specialty, experience, qualifications, languages,
  availability, active flag, `serviceIds`;
- `services`: bilingual name/description, price, duration, image, specialty, active flag;
- `patients`: only patients linked through `CenterPatients`, with contact, demographics, status and
  `appointmentCount`;
- `specialties`: the catalog used by service/therapist forms;
- `appointments`: all statuses through `AppointmentMapper`;
- `reviews`: visible + hidden, with reply and `repliedAt`;
- `notifications`: the center account's bell rows;
- `conversations`: patient name/image, therapist, last message, unread count;
- `today`: `{ date, appointmentCount, appointments }` for the dashboard.

Notification **preferences** are not part of this aggregate; they come from
`GET /api/center/workspace/preferences`. The dashboard summary tiles are derived on the client from
this payload.

#### `PUT /api/center/workspace/center`

`UpdateCenterInfoDto`: bilingual name/description, city, address (English + Arabic), profile image,
email (valid email, ≤254), phone, website, latitude/longitude, `locationConfirmed`.

Rules: coordinates must be a valid pair (−90..90, −180..180) and `locationConfirmed` must be true when
coordinates are supplied; changing the email enforces uniqueness on `Users`; emptied required fields
are rejected. Only the caller's center can be edited.

#### `PUT /api/center/workspace/hours`

`UpdateWorkingHoursDto`: `weekStart`, `weekEnd`, `fridayStart`, `fridayEnd` in `HH:mm`.

Parses all four times as `HH:mm` (field errors `WeekStart`/`WeekEnd`/`FridayStart`/`FridayEnd` on
failure) and then writes all seven `CenterWorkingHours` rows: Friday uses the Friday window, **every
other day including Saturday** uses the week window, and every row gets `IsClosed = false`. Missing
rows are created.

There is **no** check that the end time is after the start time and **no** check against existing
appointments on this endpoint — a center can save `17:00 → 08:00` or close a day that already has
confirmed bookings. Slot computation will simply return no slots for that day. Response echoes the
four times.

#### `POST /api/center/workspace/images`

`multipart/form-data` with `file` and/or an `imageUrl` form field plus `isPrimary`. If a file is sent it
is validated by `ImageStorage`; otherwise `imageUrl` is stored as-is. Sending neither returns
`400 invalid_upload` with a `file` field error. A new primary image clears the previous primary flag.

#### `DELETE /api/center/workspace/images/{imageId}`

Deletes one gallery row owned by the center (`404` otherwise).

#### `DELETE /api/center/workspace/images?url=...`

Deletes by URL (query key `url`); same ownership check.

#### `POST /api/center/workspace/services`

`SaveServiceDto`: bilingual name (both required), descriptions, `price` (0–1,000,000),
`durationMinutes` (5–600), `specialtyId` (must exist), optional `imageUrl`, `isActive`.

A new service is linked to the center automatically. `201` on success.

#### `PUT /api/center/workspace/services/{serviceId}`

Same body; only the owning center may edit. Changing duration does not rewrite existing appointments.

#### `PUT /api/center/workspace/therapists/{therapistId}`

`UpdateTherapistDto`: required `fullName` and `specialtyId`; optional Arabic name/bio, bio,
image, years of experience (0–60), qualifications, languages, `isAvailable`, `isActive`, and
`serviceIds` (null = leave the links unchanged; a list replaces them).

Rules: therapist must belong to the caller's center; `specialtyId` and every `serviceId` must exist and
the services must belong to the center; duplicate ids are de-duplicated; a therapist can be
soft-disabled (`isActive = false`) without deleting history.

#### `POST /api/center/workspace/patients`

`CreateCenterPatientDto`: creates a **login account** and patient profile on behalf of the center.

- Unique email enforced; password ≥ 8 is hashed.
- Creates `Users` (role `Patient`), `Patients` and the `CenterPatients` link in one transaction.
  No notification-preference row is created.
- `201` with the patient row.
- The new patient can sign in immediately with the supplied password.

#### `PUT /api/center/workspace/patients/{patientId}`

`UpdateCenterPatientDto`: name, email, phone, gender, date of birth, image, city, `isActive`.

- Only patients linked to the caller's center can be edited (`404` otherwise).
- Changing email updates `Users.Email` and keeps uniqueness.
- `isActive = false` blocks that patient's login but keeps all history.

#### `POST /api/center/workspace/patients/{patientId}/notes`

`AddPatientNoteDto` `{ text }` (required, ≤2000). Stores a private note authored by the calling user.
`404` when the patient is not linked to the center.

#### `PUT /api/center/workspace/appointments/{appointmentId}/status`

```json
{ "status": "approved", "reason": "optional" }
```

Accepted input values: `approved`, `rejected`, `cancelled`, `completed`, `pending` (case-insensitive).

Behaviour at HEAD (`AppointmentStatusRules.Apply`):

- The value is trimmed, lower-cased and capitalized: `approved` → **`Approved`**, `rejected` →
  `Rejected`, `cancelled` → `Cancelled`, `completed` → `Completed`, `pending` → `Pending`.
  There is **no** mapping from `approved` to `Confirmed` here.
- **There is no transition state machine on this endpoint.** Any of the five values is written over
  any current status; the only guard is the DTO's regex. In particular a `Completed` or `Cancelled`
  appointment can be moved back to `Pending`.
- **No `AppointmentStatusHistories` row is written by this endpoint.** History rows are produced only
  by the admin appointment update (§7.13.7) and by the unused `AppointmentService`.
- `reason` (≤500, optional) is stored in `RejectionReason` for `Rejected` and in
  `CancellationReason` for `Cancelled`; for every other status both reason fields are cleared.
- On an actual status change the patient gets an `appointment` notification, the therapist gets an
  `appointmentChange` notification, and a cancellation additionally notifies every admin
  (`appointmentCancelled`). `NotificationService` pushes the realtime event.
- The response is the mapped appointment.

Consequences of the `Approved` spelling: `BookingRules` blocks every status except `Cancelled` and
`Rejected`, so an `Approved` appointment still occupies its slot; but
`SchedulingRules.IsBlockingStatus` recognises only `Pending`/`Confirmed`, so a later schedule edit can
move a window out from under an `Approved` appointment. See §15.

#### `POST /api/center/workspace/reviews/{reviewId}/reply`

`ReplyToReviewDto` `{ reply }` (required, ≤1000). Only reviews for the caller's center. Sets `Reply`
and `RepliedAt`, overwriting a previous reply. `404` for foreign reviews.

#### `GET /api/center/workspace/preferences`

Returns the center account's `NotificationPreferences` row (creates defaults if absent).

#### `PUT /api/center/workspace/preferences`

Same shape as the patient preference endpoint (`emailEnabled`, `inAppEnabled`, `appointmentReminders`,
`marketing`, `language`).

#### `POST /api/center/therapists` — `CenterController`

Creates a therapist login + profile for the caller's center.

```json
{
  "fullName": "Omar Haddad",
  "email": "omar@example.com",
  "phoneNumber": "+970599111222",
  "password": "Therapist123!",
  "specialtyId": 2,
  "bio": "...",
  "yearsOfExperience": 6,
  "qualifications": "...",
  "languages": "Arabic, English",
  "serviceIds": [1, 2]
}
```

- Unique email, password ≥ 8, specialty and services must belong to the center.
- Creates `Users` (role `Therapist`, active), `Therapists` and `TherapistServices` links in one
  transaction. No notification-preference row is created.
- `201` with the therapist row. The therapist signs in through the center portal with
  `?role=center` (or without a role).

### 7.7 Schedules — `api/schedules`

`[Authorize(Roles = "Center")]`. All operations resolve the therapist's center and verify ownership.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/schedules/therapists/{therapistId}` | List the therapist's weekly windows ordered by day/start |
| POST | `/api/schedules/therapists/{therapistId}` | Create a window |
| PUT | `/api/schedules/therapists/{therapistId}/{scheduleId}` | Update a window |
| DELETE | `/api/schedules/therapists/{therapistId}/{scheduleId}` | Delete a window |

Body (`ScheduleWindowRequestDto`): `dayOfWeek` (0–6), `startTime`, `endTime` (`HH:mm`),
`isAvailable`.

`ScheduleService` enforces:

- end after start;
- no overlapping window for the same therapist/day (touching endpoints are fine);
- the window must sit inside the center's working hours for that day (a closed day cannot receive
  availability);
- deleting or shrinking a window that contains a `Pending` or `Confirmed` appointment is refused with
  a conflict, so confirmed bookings are never silently orphaned.

### 7.8 Therapist area — `api/therapist`

`[Authorize(Roles = "Therapist")]` on both controllers.

#### `GET /api/therapist/profile`

Returns the therapist's own profile: `fullName`, `fullNameAr`, `phoneNumber`, `bio`, `bioAr`,
`profileImageUrl`, `yearsOfExperience`, `qualifications`, `languages`, `isAvailable`, `isActive`,
`specialtyId`, a nested `specialty` (`specialtyId`, `nameArabic`, `nameEnglish`) and a nested `center`
(`centerId`, `nameArabic`, `nameEnglish`). Services are **not** part of this payload; they come from
the workspace aggregate and the public discovery payload.

#### `PUT /api/therapist/profile`

`UpdateTherapistProfileDto`: `fullName` (required), `fullNameAr`, `bio`, `bioAr`, `profileImageUrl`,
`yearsOfExperience` (0–60), `qualifications`, `languages`.

A therapist may edit only descriptive fields — **not** center, specialty, services, `isActive` or
`isAvailable`. Empty strings clear optional fields; the name cannot be blank.

#### `GET /api/therapist/workspace`

Aggregate for the therapist portal:

- own profile and center summary;
- appointments assigned to the therapist (all statuses, patient/service resolved);
- patients derived from those appointments;
- conversations with unread counts and recent messages;
- notifications and notification preferences;
- specialty, center, services and weekly schedule used by the portal.

#### `POST /api/therapist/workspace/images`

`multipart/form-data` `file`; `ImageStorage` rules. Returns the URL without changing the profile.

#### `GET /api/therapist/workspace/preferences` / `PUT /api/therapist/workspace/preferences`

Same notification-preference contract as the other portals.

### 7.9 Conversations — `api/conversations`

`[Authorize]` at controller level; every action checks that the caller is the patient or the therapist
of that conversation. A non-participant gets `404` (not `403`), so conversation existence is hidden.

#### `POST /api/conversations` — Patient only

```json
{ "therapistId": 6 }
```

- Finds or creates the single `(patientId, therapistId)` conversation (unique index).
- `201` when created, `200` when it already existed; body contains the conversation id and counterpart.
- Therapist must exist, be active, and belong to an approved center.

#### `GET /api/conversations/{id}/messages?since=<iso>`

Returns messages ordered by `sentAt`, with sender id, text, attachment, read flag. `since` enables
incremental polling.

#### `POST /api/conversations/{id}/messages`

```json
{ "text": "Hello", "attachment": "/uploads/abc.png" }
```

- `text` ≤2000, `attachment` ≤500.
- At least one of them must be non-empty (`400 empty_message`).
- Stores the message with the caller's user id, marks the sender as read, and pushes it over SignalR
  to both participants.
- `201` with the message row.

#### `POST /api/conversations/{id}/read`

```json
{ "messageIds": [12, 13] }
```

Marks only messages in that conversation as read (foreign ids are ignored). Returns the updated
unread count.

### 7.10 Notifications — `api/notifications`

#### `POST /api/notifications/read`

`[Authorize]` (any role).

```json
{ "id": 42 }
```

Marks the notification read **only if it belongs to the caller** (`404` otherwise). Returns the new
unread count. There is no bulk "mark all read" endpoint; the client loops or uses its local state.

### 7.11 Pain assessment — `api/assessment`

`[Authorize(Roles = "Patient")]`.

#### `POST /api/assessment`

The submission is a **flat form of codes**, not a list of question ids:

```json
{
  "painLocation": "LowerBack",
  "painSeverity": 7,
  "duration": "OneToFourWeeks",
  "painDescriptions": ["Aching", "Stiffness"],
  "previousInjurySurgery": "None",
  "otherDescription": null
}
```

| Field | Rules |
| --- | --- |
| `painLocation` | required, one of `Neck`, `Shoulder`, `UpperBack`, `LowerBack`, `ArmHand`, `Hip`, `Knee`, `LegFoot`, `MultipleAreas` |
| `painSeverity` | required integer **0–10**, JSON number must be strict (no numeric strings) |
| `duration` | required, one of `Today`, `TwoToSevenDays`, `OneToFourWeeks`, `OneToThreeMonths`, `MoreThanThreeMonths` |
| `painDescriptions` | required, ≥1 distinct codes from `Aching`, `Stiffness`, `Sharp`, `Burning`, `NumbnessTingling`, `PainWithMovement`, `Other` |
| `previousInjurySurgery` | required, one of `None`, `PreviousInjury`, `PreviousSurgery`, `RecentInjury` |
| `otherDescription` | optional, only allowed when `Other` is selected; ≤500 after trimming |

`[JsonUnmappedMemberHandling(Disallow)]` rejects unknown JSON members with a 400, so an old client
sending an `answers` array fails fast instead of silently submitting an empty assessment.

The five-question definition lives in `AssessmentCatalog` and is seeded by
`AssessmentDefinitionInitializer`. The server stores `Assessments` + `AssessmentAnswers` (mapping codes
onto the seeded rows), computes the suggested specialty, and returns the result: suggested specialty,
answer labels and **suggested care** (`SuggestedCareDto`).

Care precedence (`AssessmentCareRules`): professional review → general physiotherapy guidance →
post-surgical → persistent pain → regional specialty advice. The most urgent applicable rule wins.
Answers are immutable after submission; a new assessment is a new row.

#### `GET /api/assessment/history`

Returns the caller's assessments newest-first with suggested specialty and timestamp.

#### `GET /api/assessment/{assessmentId}`

Returns one assessment **only if it belongs to the caller** (`404` otherwise), including the full
answer breakdown and care suggestion.

### 7.12 Admin — center applications — `api/admin/center-applications`

`[Authorize(Roles = "Admin")]`.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `?status=pending|approved|rejected` | List applications (defaults to pending) with center name, city, contact, submitted date, document/image counts |
| GET | `/{id}` | Full application: profile, address/coordinates, images, documents, owner account, review metadata |
| POST | `/{id}/review` | Approve or reject |

Review body (`ReviewCenterApplicationDto`):

```json
{ "decision": "approved", "reason": "optional, required when rejecting" }
```

`decision` is restricted by regex to `approved` / `rejected` (case-insensitive).

- Approve → `Centers.Status = "Approved"`, `ApprovedByAdminId` = calling admin, `ApprovedAt`,
  `ReviewedAt`, reason cleared.
- Reject → `Status = "Rejected"`, `RejectionReason` (required, ≤500, otherwise a `Reason` field
  validation error), `ReviewedAt`.
- There is **no** guard that the center must still be `Pending`: the endpoint will happily re-approve a
  suspended center or reject an already-approved one. There is no 409 here.
- The center owner receives a `center` notification; the response is the same application DTO the list
  and detail endpoints return, so the UI can update its row without refetching.
- Approval unlocks center and therapist login (§7.1).

### 7.13 Admin management — `api/admin`

`[Authorize(Roles = "Admin")]` on the controller; every action re-checks the role.

#### 7.13.1 `GET /api/admin/dashboard?year=<int>`

KPI payload for the admin home: counts of centers by status, patients, therapists, appointments by
status, reviews, revenue-ish aggregates, and monthly series for `year` (defaults to the current year)
used by the charts.

#### 7.13.2 `GET /api/admin/activity?page=1&pageSize=20`

Paginated audit/activity feed (appointments, registrations, reviews, status changes) with entity type,
actor, target and link data used by the Activity screen. `pageSize` is capped by the controller.

#### 7.13.3 `GET /api/admin/notifications`

Admin bell payload: recent system/activity notifications with unread count.

#### 7.13.4 Centers

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/centers?status=&search=&city=&specialty=&page=&pageSize=` | Paged center list with counts |
| GET | `/centers/{id}` | Detail: identity (EN/AR), contact, city/address, status/review dates, location, `therapists`, `services`, `specialties`, `documents`, `photos`, and `stats { appointments, reviews { total, average } }` |
| PUT | `/centers/{id}/status` | `UpdateStatusDto { status, reason? }` — `Approved`, `Rejected`, `Suspended`, `Pending` |
| PUT | `/centers/{id}` | `AdminUpdateCenterDto` — names, city, address, website, phone, description |
| DELETE | `/centers/{id}` | Hard delete (see below) |

Status rules: the body accepts `approved`, `rejected`, `suspended`, `pending` (case-insensitive, stored
capitalized). A **rejection requires** `reason` (≤500, stored in `RejectionReason` and returned as a
field error under `Reason` when missing); other transitions clear the reason. Approving stamps
`ApprovedByAdminId` (from the calling admin), `ApprovedAt` and `ReviewedAt`; rejecting stamps
`ReviewedAt`; every change notifies the center owner. Response:
`{ id, status, rejectionReason, updatedAt }`.

Delete rules:

- If the center still has therapists → `409 center_has_therapists` with
  `details.therapists = n`. Therapists are separate logins and must be moved or removed first.
- Otherwise, in one transaction: therapist-service links for its services, reviews,
  status histories, appointments, services, images, documents, working hours, center-patient links,
  saved-center rows, patient notes, the owner's notifications/refresh tokens/external logins/reset
  tokens, the center row and finally the owner `Users` row are deleted.
- Returns `{ id, deleted: true }`.

#### 7.13.5 Patients

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/patients?search=&gender=&city=&sort=recent|newest&isActive=&page=&pageSize=` | Paged list with appointment/review counts |
| GET | `/patients/{id}` | Detail: profile, account status, appointments, assessments, reviews, notes, centres |
| PUT | `/patients/{id}/status` | `{ status: "active"|"suspended" }` → `Users.IsActive` (`suspended` ⇒ false); any other value → `400 invalid_status` |
| PUT | `/patients/{id}` | `AdminUpdatePatientDto`: fullName, phone, city, gender, dateOfBirth |
| DELETE | `/patients/{id}` | Hard delete with cascade described below |

Delete cascade (single transaction): reviews, status histories, appointments, assessment answers +
assessments, messages + conversations, saved centers, center-patient links, patient notes authored by
or about the patient, notifications, refresh/external/reset tokens, patient row, user row.

Status changes are the supported way to block a patient without losing clinical history.

#### 7.13.6 Physiotherapists

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/physiotherapists?search=&centerId=&specialtyId=&isActive=&page=&pageSize=` | Paged list |
| POST | `/physiotherapists` | Create login + profile (same contract as center-side creation, plus `centerId`) |
| GET | `/physiotherapists/{id}` | Detail incl. center, specialty, services, schedule, appointment counts |
| PUT | `/physiotherapists/{id}` | `AdminUpdateTherapistDto`: name, phone, centerId?, specialtyId?, years, bio, isAvailable? |
| PUT | `/physiotherapists/{id}/status` | `{ status: "active"|"suspended" }` → `Therapists.IsActive` **and** `Users.IsActive` |
| DELETE | `/physiotherapists/{id}` | Hard delete: reviews on their appointments, histories, appointments, messages/conversations, therapist-services, schedules, notes they authored, notifications/tokens, therapist, user |

Creating a therapist requires a unique email, password ≥ 8, an existing center and specialty.

#### 7.13.7 Appointments

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/appointments?status=&centerId=&therapistId=&patientId=&search=&date=&page=&pageSize=` | Paged list with resolved names |
| GET | `/appointments/options` | Dropdown data: centers, therapists, patients, services |
| POST | `/appointments` | Create an appointment, auto-confirmed |
| PUT | `/appointments/{id}` | Edit center/therapist/service/start/end/status/notes |
| DELETE | `/appointments/{id}` | Hard delete |

Create body (`AdminCreateAppointmentDto`): `patientId`, `centerId`, `therapistId`, `serviceId`,
`startDateTime`, `endDateTime`, optional `patientNotes`. All ids must exist; end must be after start;
price is copied from the service; status is `Confirmed`; **no** status-history row is written on create;
no notification is produced. Admin creation **bypasses** slot-availability checks — the admin is
trusted to resolve conflicts.

Update body (`AdminUpdateAppointmentDto`): any of centerId, therapistId, serviceId, start, end,
status, notes. When the therapist/center changes, the controller verifies consistency; when the
status changes, a history row is written. Setting `status = "cancelled"` does not require a reason at
the admin level.

#### 7.13.8 Reviews

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/reviews?visibility=all|visible|hidden&centerId=&minRating=&maxRating=&search=&sort=newest|oldest|highest&reviewId=&page=&pageSize=` | Paged moderation list |
| POST | `/reviews/{id}/moderate` | `{ action: "hide"|"show"|"remove", reason? }` |

- `hide` → `IsVisible = false`, sets `UpdatedAt`, returns `{ id, isVisible, action }`.
- `show` → `IsVisible = true`, same response.
- `remove` → hard delete and returns `{ message: "Review removed successfully." }`; because
  `Reviews.AppointmentId` is unique and the row is gone, that visit becomes reviewable again.
- `reason` is accepted by the DTO (≤500) but **not persisted** — there is no moderation table or audit
  row for it, and the review author is not notified.
- Unknown review → `404 not_found`.

#### 7.13.9 Settings

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/settings` | `{ adminId, adminFullName, email, phone, profileImageUrl, role, isActive, maintenanceMode: false, notifications }` |
| PUT | `/settings` | Partial update of full name, phone, profile image and notification flags |
| POST | `/settings/image` | `multipart/form-data` `file` → `ImageStorage`, returns `{ url }` |

Persistence semantics:

- `adminFullName` and `contactPhone` are written only when non-empty; `profileImageUrl` is written when
  the JSON field is present (empty string clears it); `notifications.*` upserts the admin's
  `NotificationPreferences`.
- `contactEmail`, `siteDescription` and `maintenanceMode` are **accepted but ignored** — there is no
  settings table. `maintenanceMode` is always returned as `false`.
- The image endpoint only stores the file and returns its URL; the client must send that URL to
  `PUT /settings` to make it the profile image.
- The PUT response omits `notifications` (the GET includes it).

---

## 8. Services and domain rules

### 8.1 Service inventory

| Service | Lifetime | Responsibility |
| --- | --- | --- |
| `PasswordService` | scoped | Hash/verify with ASP.NET Core `PasswordHasher<User>` (PBKDF2). Never stores plaintext. |
| `TokenService` | scoped | Creates HS256 JWTs with `NameIdentifier` + `Role`; exposes `AccessTokenLifetimeSeconds`. |
| `RefreshTokenService` | scoped | Issues 64-byte random tokens, stores only SHA-256 hex, rotates, revokes one/all, detects reuse. |
| `BookingRules` | static | Slot generation and booking validation (center/therapist/service consistency, hours, schedule, conflicts, DST). |
| `ScheduleService` | scoped | Validates and persists therapist schedule windows inside center hours without orphaning bookings. |
| `SchedulingRules` | static | 50-minute treatment + 10-minute break constants, blocking-status test, occupied-end calculation. |
| `SchedulingClock` | singleton | `Asia/Hebron` “now” and DST ambiguity/invalid-time guard. |
| `SchedulingWriteCoordinator` | scoped | Process semaphore + `Serializable` transaction + `sp_getapplock` per patient/therapist, ordered acquisition, conflict translation. |
| `AvailabilityService` | scoped | Older slot/availability computation kept for tests and compatibility; not called by the live controllers. |
| `AppointmentService` | scoped | Older full appointment workflow (create/cancel/reschedule/complete, writes history). **No live endpoint uses it**; the controller that did is commented out. |
| `AppointmentStatusRules` | static | Writes a status + the correct reason field, capitalizes the stored value, clears the other reason. |
| `AssessmentService` | scoped | Validates submissions, persists assessments/answers, builds results. |
| `AssessmentCatalog` | static | The five-question definition and option metadata. |
| `AssessmentCareRules` | static | Care-suggestion precedence. |
| `AssessmentDefinitionInitializer` | static | Idempotently seeds questions/options under an app lock + serializable transaction. |
| `NotificationService` | scoped | Persists notifications for users/admins and pushes `workspaceChanged` over SignalR; failures are logged, not thrown. |
| `ImageStorage` | static | Upload validation/persistence (2 MB, jpeg/png/webp/gif) to `wwwroot/uploads`. |
| `CurrentUser` | static | Claim helpers (`IsRole`, `TryGetUserId`, `GetUserId`). |
| `ApiError` | static | Uniform error envelope builders. |
| `FlexibleIntConverter` / `FlexibleNullableIntConverter` | JSON converters | Accept numeric or string ids on input. |
| `CenterMapper` / `AppointmentMapper` | static | Build the discovery/workspace/appointment DTOs with resolved names, images, ratings and status labels. |
| `GoogleNonceStore` / `GoogleRegistrationTicketStore` | singleton | In-memory, 5-minute, 1024-entry capped, flow-verifier-bound tickets/nonces. |
| `GoogleTokenValidator` | singleton | Validates Google ID tokens against the configured Web Client ID. |

### 8.2 Booking rules (`BookingRules`)

- Only a center with `Status == "Approved"` can be booked; the check is applied to the center row,
  not only the id.
- `ResolveAsync` verifies, in one query set, that the therapist belongs to the center, the service
  belongs to the center, and the therapist is linked to the service through `TherapistServices`.
  Failure returns a specific `404` (`center_unavailable`, `therapist_not_found`,
  `service_not_found`, etc.).
- Slot candidates start on a **30-minute grid** (`00`/`30`) and are limited by:
  - the center's `CenterWorkingHours` for that weekday (`IsClosed` or missing row ⇒ no slots),
  - the therapist's `TherapistSchedules` window for that weekday where `IsAvailable`,
  - `service.DurationMinutes` (the whole service must fit before closing).
- Existing appointments are **free** only when `Status` is `Cancelled` or `Rejected`. Every other
  status (`Pending`, `Confirmed`, `Approved`, `Completed`) occupies the interval for that therapist.
- `GetBookedAsync` filters by **therapist only** (and the day). A patient can therefore request two
  overlapping appointments with different therapists — there is no patient-level conflict check on the
  server. (The frontend's mock booking rules do check the patient; see the frontend document.)
- Past start times are rejected; the comparison uses local scheduling time.
- DST: an interval whose start/end/last tick is invalid or ambiguous in `Asia/Hebron`, or whose offset
  changes across the interval, is refused rather than double-counted.

### 8.3 Scheduling rules and windows

- `SchedulingRules` constants: treatment **50 min**, break **10 min**, slot **60 min**.
  `GetOccupiedEnd` extends an appointment to at least `start + 50` and adds the 10-minute break.
- `IsBlockingStatus` recognises only `Pending` and `Confirmed` (not `Approved`, not `Completed`).
- `IsBookableCenter` compares against the literal `"Active"`, which no current registration path
  produces (centers are `Pending`/`Approved`/`Rejected`/`Suspended`). This method is effectively dead
  logic and a leftover from an earlier status vocabulary.

`ScheduleService` (used by `SchedulesController`) enforces:

1. Start time before end time, valid `HH:mm` values.
2. The center must have working hours for that day and the window must be **inside** them.
3. Windows for the same therapist/day must not overlap (touching endpoints are allowed).
4. Existing schedules on the same day must themselves have valid bounds.
5. Any `Pending`/`Confirmed` appointment (plus its 10-minute break) that would fall outside the
   resulting windows blocks the create/update/delete with a conflict.
6. Failures use `SchedulingValidationException` (→ 400 with field errors) or
   `SchedulingConflictException` (→ 409).

Note that rule 5 cannot see `Approved` appointments (see §12.2 and §15).

### 8.4 Appointment status writes

`AppointmentStatusRules.Apply` does three things only:

```csharp
appointment.Status = Capitalize(status.Trim().ToLowerInvariant());
appointment.UpdatedAt = DateTime.UtcNow;
if (Rejected) { RejectionReason = reason; CancellationReason = null; }
else if (Cancelled) { CancellationReason = reason; RejectionReason = null; }
else { both reasons = null; }
```

It does not validate transitions, does not write history and does not check who is allowed to make the
change — the calling controller owns ownership checks. Admin appointment updates bypass this helper and
write their own history rows (§7.13.7).

### 8.5 Assessment domain

- `AssessmentCatalog` defines **five active questions**, each with ordered options carrying optional
  `SpecialtyId` and `Score`; some questions accept a numeric value and some allow an “other” text.
- `AssessmentDefinitionInitializer` seeds them once: it takes a SQL Server application lock, opens a
  serializable transaction, and refuses to run concurrently (conflict exception) so two app instances
  cannot double-seed.
- `AssessmentService` validates that every active question is answered once, options belong to their
  question, `otherDescription` is present when required (≤500) and numeric values are in range.
- `AssessmentCareRules` picks exactly one suggestion, in this precedence:
  1. **Professional review** (red flags / severe or neurological symptoms),
  2. **General physiotherapy guidance** (non-specific musculoskeletal pain),
  3. **Post-surgical** rehabilitation,
  4. **Persistent pain** (long-standing/chronic pattern),
  5. **Regional specialty advice** (the matched specialty from the answers).
- Results are returned with English/Arabic labels and the suggested specialty; nothing is auto-booked.

### 8.6 Notifications and realtime push

`NotificationService.NotifyAsync`:

- filters out non-positive ids, de-duplicates recipients;
- when `onlyWhenUnreadExists` is true, skips any recipient who already has an unread notification of
  the same `Type` + `RelatedEntityId` (prevents bell spam for repeated messages);
- inserts rows in one `SaveChanges`, catching failures and logging them so the business action still
  succeeds;
- pushes `workspaceChanged` with `{ type: "notification" }` to `Clients.Users(userIds)`.

`NotifyAdminsAsync` resolves every `Users.Role == "Admin"` account and calls the same method.

Notification `Type` values actually written: `appointmentRequest`, `appointment`,
`appointmentChange`, `appointmentCancelled`, `appointmentReminder`, `message`, `center`,
`centerApproval`, `therapistVerified`, `services`, `professional`, `system`. The client treats the push
payload as a hint to refetch its workspace, not as the data itself.

### 8.7 Tokens and passwords

- JWT: HS256, issuer/audience from configuration, lifetime `Jwt:ExpireMinutes` (15), claims
  `NameIdentifier` + `Role`. No `jti`, no token version, no server-side revocation list.
- Refresh token: 64 random bytes (base64url), stored as SHA-256 hex in `RefreshTokens.TokenHash`,
  lifetime `Jwt:RefreshDays` (7).
- Rotation: `TryRotateAsync` revokes the presented row, records `ReplacedByTokenHash`, issues a new row.
- Reuse: presenting an already-revoked token triggers `RevokeAllForUserAsync` — every active session of
  that user is terminated.
- Logout revokes one token; change-password revokes all; admin delete cascades remove the rows.
- Passwords: `PasswordHasher<User>` (PBKDF2-HMAC-SHA256, 100k iterations by default), minimum length 8
  enforced by DTOs; no complexity rule, no breach check, no lockout counter.
- Password reset: schema and DTOs exist, endpoints are `[NonAction]`.

### 8.8 Uploads

`ImageStorage.SaveAsync(file, webRootPath)`:

| Check | Failure |
| --- | --- |
| file present and length > 0 | `400 invalid_upload` |
| ≤ 2 MB | `400 file_too_large` |
| content type `image/jpeg`, `image/jpg`, `image/png`, `image/webp`, `image/gif` | `400 unsupported_media_type` |

Saved as `uploads/{guid}{extension}` under `wwwroot`; returned URL is `/uploads/{file}`. Existing files
are never overwritten because the name is a GUID. There is no cleanup job for orphaned uploads and no
path traversal risk because the client never supplies the filename.

### 8.9 Concurrency

`SchedulingWriteCoordinator.ExecuteAsync` wraps a write in:

1. a process-wide `SemaphoreSlim(1,1)`;
2. a `Serializable` SQL transaction;
3. `sys.sp_getapplock` (exclusive, transaction-owned, 15 s timeout) for each patient lock
   (`PhysioGuide.PatientAppointments.{id}`) then each therapist lock
   (`PhysioGuide.TherapistSchedules.{id}`), always in ascending id order to avoid deadlocks.

SQL errors 1205/1222/2601/2627/3960/51001, `DbUpdateConcurrencyException` and `DbUpdateException` are
translated into `SchedulingConflictException` (409).

---

## 9. Realtime (SignalR)

- Hub: `Hubs/MessagingHub`, route `/hubs/messages`, decorated `[Authorize]`.
- The hub declares **no server-callable methods**. It exists purely as a push channel.
- Authentication: the JWT is read from the `access_token` query string for `/hubs/*` paths.
- SignalR maps a connection to `Clients.User(userId)` using the `NameIdentifier` claim, so every open
  tab/device of a user receives the event.
- Events sent by the backend today:

| Event | Payload | Trigger |
| --- | --- | --- |
| `workspaceChanged` | `{ type: "notification" }` | `NotificationService` after creating notifications (new appointment request, status change, message, center decision, etc.) |

- The frontend listens for `workspaceChanged` and refetches its workspace; it does not trust the event
  as a data payload.
- There is no presence tracking, typing indicator, delivery receipt or room/group model.

---

## 10. Seeding and demo accounts

`DbSeeder.SeedAsync` is idempotent: it checks for existing rows before inserting. It runs after
migrations at every startup (failures are logged and skipped).

### 10.1 Accounts

| Role | Email | Password | Profile |
| --- | --- | --- | --- |
| Admin | `admin@physioguide.dev` | `Admin123!` | `Admins` row, full name “System Administrator” |
| Patient | `patient@physioguide.dev` | `Patient123!` | demo patient |
| Center (approved) | `center@physioguide.dev` | `Center123!` | primary demo center, `Approved` |
| Center (approved) | `center2@physioguide.dev` | `Center123!` | second demo center for discovery/comparison |
| Center (pending) | `pending-center@physioguide.dev` | `Center123!` | `Pending`, used to demo the approval flow |
| Therapist | `lina.khoury@physioguide.dev` | `Therapist123!` | therapist at the primary center |
| Therapist | `omar.haddad@physioguide.dev` | `Therapist123!` | therapist at the primary center |
| Therapist | `rania.saleh@physioguide.dev` | `Therapist123!` | therapist at the second center |
| Therapist | `khaled.amer@physioguide.dev` | `Therapist123!` | therapist at the second center |

All seeded users are `IsActive = true`.

### 10.2 Data

Depending on which tables are empty, the seeder creates:

- specialties (orthopedics, sports, neurology, pediatrics, post-surgery, etc.);
- services per center with prices and durations;
- seven working-hour rows per center;
- therapist schedules across weekdays;
- patients linked to centers (`CenterPatients`);
- appointments in several statuses (pending, confirmed, completed, cancelled) spread across recent and
  upcoming dates, so the dashboards and charts are not empty;
- reviews (some with replies), messages/conversations, notifications and assessment questions/options.

Exact ids are not stable — never hard-code them in tests. The frontend's mock mode has its own data and
is unrelated to this seeder.

---

## 11. Use cases by actor

This section is the behavioural contract: what each actor can do, what the system enforces, and what
the edge cases are. Every case maps to endpoints in §7.

### 11.1 Anonymous visitor

| # | Use case | Behaviour |
| --- | --- | --- |
| A1 | Open the API | `GET /` returns `Physio Guide API is running!` |
| A2 | Explore Swagger | `/swagger` is available in every environment, with Bearer auth support |
| A3 | Browse approved centers | `GET /api/centers` returns only `Approved` centers; optional `city` filter |
| A4 | Inspect a center | Services, therapists, images, hours, rating and contact details are included in the list/detail payload |
| A5 | Check availability | `GET /api/centers/{id}/slots` for a therapist/service/date; empty list when closed/full/past |
| A6 | Register as patient | Creates account + profile; then signs in |
| A7 | Register as center | Creates a `Pending` center + working hours; cannot sign in until approved |
| A8 | Sign in with Google | Challenge → Google → sign-in; unknown identity gets a registration ticket |
| A9 | Complete a Google registration | Patient or center profile is created only after the ticket step |

Edge cases: duplicate email (`409 duplicate_email`), weak password (400), phone format (400),
registration rate limit (429), Google disabled (503), pending center login (403),
rejected center login (403 + reason), suspended center login (403).

### 11.2 Patient

| # | Use case | Endpoint(s) | Rules / edge cases |
| --- | --- | --- | --- |
| P1 | Sign in | `POST /api/auth/login?role=patient` | Inactive account → 403; wrong portal → generic 401 |
| P2 | Search/filter centers | `GET /api/centers` | Only approved centers; favourites come from `saved-centers` |
| P3 | View center details | `GET /api/centers` payload | Services, therapists, gallery, reviews, coordinates |
| P4 | View therapist details | Discovery payload | Specialty, bio (EN/AR), experience, languages, services |
| P5 | Check slots | `GET /api/centers/{id}/slots` | 30-min grid, service duration, center hours ∩ therapist schedule |
| P6 | Request appointment | `POST /api/appointments/request` | Pending only; duplicates/overlaps/past → `409 slot_unavailable`; notifies therapist + center |
| P7 | List own appointments | `GET /api/patient/appointments` | `status`, `from`, `to` filters |
| P8 | Track status | Same | Pending → Approved/Confirmed/Rejected/Cancelled; completed visits enable reviews |
| P9 | Cancel/reschedule | **No patient endpoint** | The patient must message/ask the center; center/admin perform changes |
| P10 | Review a center | `POST /api/patient/reviews` | Requires a completed, unreviewed appointment at that center; one review per visit |
| P11 | Save/unsave center | `PUT`/`DELETE /api/patient/saved-centers/{id}` | Idempotent; unapproved center → `400 center_unavailable` on save |
| P12 | Edit profile | `PUT /api/patient/account/profile` | Name required; null image/phone/city preserved; gender can be cleared |
| P13 | Upload avatar | `POST /api/patient/account/image` | 2 MB, image types only |
| P14 | Notification preferences | `PUT /api/patient/account/preferences` | Upsert; `en`/`ar` only |
| P15 | Change password | `POST /api/auth/change-password` | Current password required; revokes all refresh tokens |
| P16 | Pain assessment | `POST /api/assessment` | 5 questions, one answer each, care suggestion |
| P17 | Assessment history | `GET /api/assessment/history`, `GET /api/assessment/{id}` | Own records only |
| P18 | Message a therapist | `POST /api/conversations`, then messages | One conversation per pair; participant-scoped |
| P19 | Read messages | `GET /api/conversations/{id}/messages?since=` | Incremental polling |
| P20 | Mark messages read | `POST /api/conversations/{id}/read` | Only ids in that conversation |
| P21 | Notifications | `POST /api/notifications/read` | Own notification only; no bulk endpoint |
| P22 | Sign out | `POST /api/auth/logout` | Refresh token revoked; access token expires naturally |

Patient-visible status vocabulary caveat: a center's “approve” action writes `Approved`, not
`Confirmed`. The patient UI's mapper normalises both to its `confirmed` tab, but the database keeps the
literal center-chosen word.

### 11.3 Center

| # | Use case | Endpoint(s) | Rules / edge cases |
| --- | --- | --- | --- |
| C1 | Sign in | `POST /api/auth/login?role=center` | Requires `Approved` center + active owner |
| C2 | Load dashboard | `GET /api/center/workspace` | One aggregate for every center screen |
| C3 | Edit center profile | `PUT /api/center/workspace/center` | Bilingual fields, coordinates require confirmation, unique email |
| C4 | Edit working hours | `PUT /api/center/workspace/hours` | Writes all 7 days; no ordering/appointment validation (see gaps) |
| C5 | Manage gallery | `POST /workspace/images`, `DELETE /workspace/images/{id}` / by URL | 2 MB, ownership checked |
| C6 | Create service | `POST /workspace/services` | Bilingual names, price 0–1M, 5–600 min, specialty |
| C7 | Edit service | `PUT /workspace/services/{id}` | Ownership; no retroactive price/duration changes |
| C8 | Add therapist login | `POST /api/center/therapists` | Unique email, center-owned specialty/services, transaction |
| C9 | Edit therapist | `PUT /workspace/therapists/{id}` | Services replace when supplied; null leaves unchanged |
| C10 | Manage schedule windows | `GET/POST/PUT/DELETE /api/schedules/therapists/{id}...` | Inside center hours, no overlap, protects Pending/Confirmed bookings |
| C11 | Create patient account | `POST /workspace/patients` | Creates login + profile + center link atomically |
| C12 | Edit patient | `PUT /workspace/patients/{id}` | Only linked patients; email uniqueness; `isActive` blocks login |
| C13 | Add private note | `POST /workspace/patients/{id}/notes` | ≤2000, authored by caller |
| C14 | Approve/reject/cancel/complete appointment | `PUT /workspace/appointments/{id}/status` | Accepts 5 statuses, no transition machine, writes reason fields, notifies |
| C15 | Reply to review | `POST /workspace/reviews/{id}/reply` | Own center only; overwrites previous reply |
| C16 | Notification preferences | `GET/PUT /workspace/preferences` | Same contract as elsewhere |
| C17 | Upload center/therapist images | `POST /workspace/images`, `POST /api/therapist/workspace/images` (therapist), `POST /api/center/workspace/images` | Returned URLs are applied by the corresponding save call |
| C18 | Change password | `POST /api/auth/change-password` | Any authenticated role |
| C19 | Sign out | `POST /api/auth/logout` | |

Edge cases: pending/rejected/suspended center cannot sign in; deleting a therapist is an admin action;
the center cannot delete itself; a service from another center is rejected; a patient from another
center is invisible (404); schedule edits that would orphan a confirmed booking are refused.

### 11.4 Therapist

| # | Use case | Endpoint(s) | Rules |
| --- | --- | --- | --- |
| T1 | Sign in | `POST /api/auth/login?role=center` (or no role) | Therapist must be active and its center approved/active |
| T2 | Load workspace | `GET /api/therapist/workspace` | Own appointments, patients, conversations, notifications, preferences, center summary |
| T3 | View/edit profile | `GET/PUT /api/therapist/profile` | Descriptive fields only; cannot change center/specialty/services/status |
| T4 | Upload profile image | `POST /api/therapist/workspace/images` | 2 MB image types |
| T5 | Notification preferences | `GET/PUT /api/therapist/workspace/preferences` | |
| T6 | View conversations | Workspace payload | Only conversations where they are the therapist |
| T7 | Read/send messages | `GET/POST /api/conversations/{id}/messages` | Participant-scoped 404 for outsiders |
| T8 | Mark messages read | `POST /api/conversations/{id}/read` | |
| T9 | Mark notification read | `POST /api/notifications/read` | |
| T10 | Change password / sign out | Auth endpoints | |
| T11 | Change appointment status | **No therapist endpoint** | Status changes are center/admin actions; the therapist is notified |
| T12 | Edit schedule | **No therapist endpoint** | Schedules are managed by the center (`SchedulesController`) |

### 11.5 Admin

| # | Use case | Endpoint(s) | Rules / edge cases |
| --- | --- | --- | --- |
| N1 | Sign in | `POST /api/auth/login?role=admin` | Any active admin user |
| N2 | Dashboard KPIs | `GET /api/admin/dashboard?year=` | Counts + monthly series |
| N3 | Activity feed | `GET /api/admin/activity` | Paginated, newest first |
| N4 | Admin notifications | `GET /api/admin/notifications` | Bell payload |
| N5 | Review center applications | `GET/POST /api/admin/center-applications...` | Approve/reject; reason required on reject |
| N6 | List/search centers | `GET /api/admin/centers` | Filters: status, search, city, specialty; pagination |
| N7 | Center detail | `GET /api/admin/centers/{id}` | Full operational picture |
| N8 | Change center status | `PUT /api/admin/centers/{id}/status` | approved/rejected/suspended/pending; reason required to reject |
| N9 | Edit center | `PUT /api/admin/centers/{id}` | Partial updates |
| N10 | Delete center | `DELETE /api/admin/centers/{id}` | Blocked by `center_has_therapists`; otherwise transactional cascade |
| N11 | List/search patients | `GET /api/admin/patients` | search/gender/city/sort/isActive |
| N12 | Patient detail | `GET /api/admin/patients/{id}` | Appointments, reviews, assessments, notes |
| N13 | Suspend/reactivate patient | `PUT /api/admin/patients/{id}/status` | `active`/`suspended`; history kept |
| N14 | Edit patient | `PUT /api/admin/patients/{id}` | Profile fields only |
| N15 | Delete patient | `DELETE /api/admin/patients/{id}` | Transactional cascade incl. appointments/assessments/messages |
| N16 | List/search therapists | `GET /api/admin/physiotherapists` | center/specialty/isActive filters |
| N17 | Create therapist | `POST /api/admin/physiotherapists` | Unique email, center + specialty required |
| N18 | Therapist detail/edit | `GET/PUT /api/admin/physiotherapists/{id}` | Can move center/specialty, change availability |
| N19 | Suspend/reactivate therapist | `PUT /api/admin/physiotherapists/{id}/status` | Updates both `Therapists.IsActive` and `Users.IsActive` |
| N20 | Delete therapist | `DELETE /api/admin/physiotherapists/{id}` | Transactional cascade incl. schedules/conversations |
| N21 | List/filter appointments | `GET /api/admin/appointments` | status/center/therapist/patient/search/date/pagination |
| N22 | Appointment form options | `GET /api/admin/appointments/options` | Centers, therapists, patients, services |
| N23 | Create appointment | `POST /api/admin/appointments` | Auto-confirmed, bypasses slot checks, validates consistency and end > start |
| N24 | Edit appointment | `PUT /api/admin/appointments/{id}` | Partial; status change writes history; price re-snapshotted when service changes |
| N25 | Delete appointment | `DELETE /api/admin/appointments/{id}` | Hard delete |
| N26 | Moderate reviews | `GET /api/admin/reviews`, `POST /api/admin/reviews/{id}/moderate` | hide/show/remove |
| N27 | Settings | `GET/PUT /api/admin/settings`, `POST /api/admin/settings/image` | Profile identity, contact, description, maintenance flag, notification defaults |
| N28 | Change password/sign out | Auth endpoints | |

### 11.6 System / background behaviour

There are no hosted background services. “Automatic” behaviour happens inside requests or at startup:

| When | What |
| --- | --- |
| App start | `Database.Migrate()` then `DbSeeder.SeedAsync` (both failure-tolerant) |
| Any notification-producing action | Row insert + SignalR `workspaceChanged` |
| Patient messaging-workspace read | Reminder notifications are generated for the patient's `Confirmed` appointments within the next 24 h (unless `AppointmentReminders` is off), and stale reminders are deleted when the appointment is no longer eligible |
| Assessment first use | `AssessmentDefinitionInitializer` seeds the 5-question catalog under an app lock |
| Concurrent booking/schedule write | `sp_getapplock` serialization |

No scheduled emails, no SMS, no push notifications to mobile, no cleanup of expired refresh tokens or
orphaned uploads.

---

## 12. State machines

### 12.1 Center status

```text
register ──► Pending ──approve──► Approved ──suspend──► Suspended
                 │                   ▲                     │
                 └──reject──► Rejected└──────approve────────┘
```

- `Pending`: cannot sign in; hidden from discovery; shown in admin applications.
- `Approved`: sign-in allowed (owner + its therapists), discoverable, bookable.
- `Rejected`: sign-in blocked with `account_rejected` and the stored reason; can be re-approved by an
  admin.
- `Suspended`: sign-in blocked; data retained; can be re-approved.
- `ReviewedAt` is stamped on approval/rejection; `ApprovedAt`/`ApprovedByAdminId` on approval.

### 12.2 Appointment status (as implemented)

Canonical values used by the booking engine: `Pending`, `Confirmed`, `Completed`, `Cancelled`,
`Rejected`. The center DTO additionally produces the literal `Approved` (see §7.6), and the admin DTO
accepts `approved` too, storing `Approved`.

```text
request ──► Pending ──center "approved"──► Approved ──► (any status)
              │                              
              ├──"rejected"──► Rejected (terminal in UI)
              ├──"cancelled"─► Cancelled
              └──admin/legacy confirmed──► Confirmed ──► Completed
```

Because no transition guard exists on the center endpoint, the diagram is descriptive, not enforced.
Slot occupancy is determined by `BookingRules` (everything except Cancelled/Rejected blocks), while
schedule protection uses `SchedulingRules` (only Pending/Confirmed block). This asymmetry is listed in
§15.

### 12.3 Account activity

| Actor | Field | Effect |
| --- | --- | --- |
| Patient | `Users.IsActive` | Blocks login; data retained |
| Therapist | `Therapists.IsActive` + `Users.IsActive` | Blocks login; hidden from discovery when the mapper filters inactive therapists |
| Center | `Centers.Status` + `Users.IsActive` | Status gates login and discovery |
| Admin | `Users.IsActive` | Blocks login (no admin UI for this; DB only) |

### 12.4 Review visibility

```text
created (IsVisible = true) ──hide──► hidden (IsVisible = false) ──show──► visible
        │
        └──remove──► deleted
```

---

## 13. Validation and error catalogue

### 13.1 Error codes emitted today

| Code | Status | Where / meaning |
| --- | --- | --- |
| `not_found` | 404 | Missing **or foreign** resource (ownership is not leaked) |
| `unauthenticated` | 401 | Bad/missing credentials or token |
| `token_expired` | 401 | JWT lifetime exceeded (client should refresh) |
| `invalid_refresh_token` | 401 | Refresh token invalid/expired/revoked/reused |
| `forbidden` | 403 | Role gate |
| `account_not_active` | 403 | Inactive user, pending center |
| `account_rejected` | 403 | Rejected center (includes reason) |
| `account_suspended` | 403 | Suspended center |
| `center_profile_missing` | 403 | Center user without `Centers` row |
| `center_unavailable` | 400/404 | Saving an unapproved center / booking an unavailable center |
| `invalid_status` | 400 | Admin status vocabulary violation |
| `invalid_action` | 400 | Review moderation action not hide/show/remove |
| `invalid_time` | 400 | End not after start (admin schedule/appointment) |
| `invalid_upload` | 400 | Missing/empty file |
| `unsupported_media_type` | 400 | Non-image upload |
| `file_too_large` | 400 | Upload > 2 MB |
| `duplicate_email` | 409 | Registration with an existing email |
| `email_taken` | 409 | Admin-created therapist/patient email conflict |
| `slot_unavailable` | 409 | Past time, overlap, or lost booking race |
| `review_not_allowed` | 400 | No completed unreviewed appointment to review |
| `center_has_therapists` | 409 | Center deletion blocked (details.therapists) |
| `rate_limited` | 429 | Fixed-window limiter |
| `rate_limit_exceeded` | 429 | Google limiter’s own message (usually overwritten, see §15) |
| `google_auth_unavailable` | 503 | Google sign-in disabled/misconfigured |
| `internal_error` | 500 | Unhandled exception |

`ApiError.Validation` produces a 400 with `errors: { Field: [messages] }`; field keys sometimes keep
PascalCase (`Email`, `Reason`, `CurrentPassword`, `Date`, `Time`, `WeekStart`) because the frontend maps
those names.

### 13.2 Input validation locations

| Layer | What it checks |
| --- | --- |
| DTO DataAnnotations | required fields, lengths, ranges, regexes, email/phone formats |
| Controllers | ownership, date/time parsing, cross-entity consistency, status vocabularies, state rules |
| Services | slot availability, schedule geometry, assessment completeness, token validity |
| Database | unique indexes, max lengths, FK integrity |

No FluentValidation, no AutoMapper, no MediatR. Validation errors from DataAnnotations are automatic
thanks to `[ApiController]`.

---

## 14. Security model

### 14.1 Authentication

- Password hashes via `PasswordHasher<User>`; no plaintext, no reversible storage.
- JWT HS256, 15-minute lifetime, validated issuer/audience/lifetime/signature.
- Refresh tokens are opaque, hashed at rest, rotated on use, reuse-revoked.
- Google: ID token validated server-side; nonce + flow-binding + single-use registration ticket prevent
  login CSRF and silent account creation.

### 14.2 Authorization

- Role-based controller gates + explicit in-code role checks for patient booking.
- Resource ownership is resolved from the JWT user id, never from a client-supplied user/patient id.
- Cross-tenant reads/writes return `404` to avoid enumeration.
- Admin endpoints are all under `[Authorize(Roles = "Admin")]`.

### 14.3 Transport and CORS

- `UseHttpsRedirection` is enabled, but the checked-in CORS policy is `AllowAll` (any origin, any header,
  any method). For production this should be narrowed to the deployed frontend origin.
- No HSTS, no CSP, no cookie policy: the API is token-based and stateless.

### 14.4 Rate limits

| Policy | Limit | Applied to |
| --- | --- | --- |
| `auth-login` | 10/min | `POST /api/auth/login` |
| `auth-register` | 5/min | patient and center registration |
| `GoogleAuth` | 20/min/IP | all Google endpoints |

Fixed windows, no queueing, `Retry-After` returned. Limits are per-process; a multi-instance deployment
needs a distributed limiter.

### 14.5 Upload safety

- Size/type allow-list, GUID filenames, server-controlled directory.
- Files are served from the same origin as the API via static files; no `Content-Disposition` hardening
  and no image re-encoding.
- SVG is not allowed (good: SVG can carry script).

### 14.6 Secrets

- `Jwt:Key` must come from User Secrets or `Jwt__Key`; startup fails if it is short.
- Google Web Client ID is public by design; no client secret is used.
- Connection strings live in `appsettings.*.json` for development; production should override them.

### 14.7 Known security limitations

- CORS `AllowAll`.
- No account lockout or CAPTCHA on login beyond IP rate limiting.
- No email verification for password registration.
- No password recovery.
- Refresh tokens are not bound to a device fingerprint (only optional `DeviceInfo` text).
- Access tokens cannot be revoked before expiry.
- Admin “maintenance mode” is a settings value only; it does not block traffic.

---

## 15. Known gaps and inconsistencies

These are properties of the code at HEAD, not recommendations disguised as facts. They matter when you
build against the API.

1. **`Approved` vs `Confirmed` appointment status.** The center status endpoint writes `Approved`;
   booking treats it as occupying a slot, but schedule protection and some UI mappers look for
   `Confirmed`. Tests named “CenterApprovedTransitions…” describe a stricter behaviour (history +
   transitions) that the current controller does not implement, so those tests may not reflect HEAD.
2. **No transition validation on center status updates.** Any of the five values can overwrite any
   status, including reopening a completed or cancelled appointment.
3. **No history on center status updates.** Only admin appointment edits (and dead `AppointmentService`)
   write `AppointmentStatusHistories`.
4. **Working-hours endpoint has no ordering/appointment checks** and reopens Saturday every time it is
   saved (loop sets `IsClosed = false` for all seven days).
5. **`SchedulingRules.IsBookableCenter` checks `"Active"`**, a center status no current flow produces.
   `AvailabilityService` and `SchedulingRules` are effectively legacy; `BookingRules` is the live path.
   In addition, `BookingRules.ComputeSlotsAsync` loads only the **first** matching
   `TherapistSchedule` row for the day, while `ScheduleService` allows several non-overlapping windows
   per day — extra windows are ignored when offering slots.
6. **`AppointmentService` is dead code** but still DI-registered; its cancel/reschedule/complete logic
   is not reachable through HTTP. The commented-out block in `AppointmentsController` shows the intent.
7. **Password recovery is disabled** (`[NonAction]`), although DTOs, table and rate-limit policy remain.
8. **Google ticket/nonce stores are in-memory** — restart or multiple instances invalidate pending flows.
9. **Rate limiter is registered twice** (`AddRateLimiter` twice, `UseRateLimiter` twice). The second
   `OnRejected` overwrites the Google-specific message, and the middleware runs twice. It is harmless
   today but confusing, and the Google policy’s custom message is effectively unreachable.
10. **Two empty migrations** (`IntegrateWithFron`, `AdminRelated`) add noise to the history.
11. **Therapist login failures return `{ message }` without `code`**, unlike every other auth failure;
    clients must handle both shapes.
12. **Admin can create appointments outside availability**, and can set `status: "approved"`, which
    stores `Approved` (same vocabulary issue as #1).
13. **Admin review moderation “remove” is a hard delete**, so the appointment becomes reviewable again;
    “hide” is the non-destructive option.
14. **No pagination contract on `GET /api/centers`** — the whole approved catalog is returned.
15. **No refresh-token cleanup or upload cleanup job.**
16. **`Program.cs` swallows migration/seed failures**, so a schema mismatch surfaces later as 500s.
17. **Swagger is public in production**, which exposes the full contract.
18. **CORS allows every origin**, including with credentials-less bearer tokens (still enables any site to
    call the API from a browser using a stolen token).
19. **Assessment answers are immutable**, and there is no “draft” concept; every submission is a new row.
20. **Notification preferences do not gate server-side notification creation** — the backend still writes
    rows; the frontend filters display. Only `NotificationService.onlyWhenUnreadExists` de-duplicates.

---

## 16. Tests

Test projects are not in the `.sln`; run them individually.

```bash
dotnet test tests/PhysioGuide.Appointments.Tests
dotnet test tests/PhysioGuide.Assessment.Tests
dotnet test tests/PhysioGuide.Availability.Tests
```

| Project | Files | Coverage focus |
| --- | --- | --- |
| `PhysioGuide.Appointments.Tests` | `AppointmentApiFactory`, `AppointmentWorkflowTests`, `BookingApiTests`, `BookingConcurrencyTests`, `RescheduleAndCompletionTests` | Ownership isolation, platform/role gates, invalid JWT identities, inactive users, ineligible centers, cancellation rules, center status transitions/history, reason lengths, unexpected fields, concurrent booking |
| `PhysioGuide.Assessment.Tests` | `AssessmentApiFactory`, `AssessmentApiTests`, `AssessmentCareRulesTests` | Submission validation, ownership, history, care-rule precedence |
| `PhysioGuide.Availability.Tests` | `AvailabilityApiFactory`, `AvailabilityApiTests`, `ScheduleApiTests` | Slot computation, schedule CRUD/overlap/protection |

The factories boot the API in-memory with a test database and seeded users, then exercise it through
`HttpClient`. Some tests target the stricter appointment workflow described in §15.1; if they fail at
HEAD, the cause is the controller behaviour, not the test harness.

---

## 17. Endpoint index

| # | Method | Route | Auth |
| --- | --- | --- | --- |
| 1 | POST | `/api/auth/register/patient` | anonymous (5/min) |
| 2 | POST | `/api/auth/register/center` | anonymous (5/min) |
| 3 | POST | `/api/auth/login?role=` | anonymous (10/min) |
| 4 | POST | `/api/auth/refresh` | anonymous |
| 5 | POST | `/api/auth/logout` | authenticated |
| 6 | POST | `/api/auth/change-password` | authenticated |
| 7 | POST | `/api/Auth/google/challenge` | anonymous (20/min) |
| 8 | POST | `/api/Auth/google/sign-in` | anonymous (20/min) |
| 9 | POST | `/api/Auth/google/register/patient` | anonymous (20/min) |
| 10 | POST | `/api/Auth/google/register/center` | anonymous (20/min) |
| 11 | GET | `/api/centers?city=` | anonymous |
| 12 | GET | `/api/centers/{centerId}/slots` | anonymous |
| 13 | POST | `/api/appointments/request` | Patient |
| 14 | GET | `/api/patient/appointments` | Patient |
| 15 | GET | `/api/patient/messaging-workspace` | Patient |
| 16 | GET | `/api/patient/account` | Patient |
| 17 | PUT | `/api/patient/account/profile` | Patient |
| 18 | PUT | `/api/patient/account/preferences` | Patient |
| 19 | POST | `/api/patient/account/image` | Patient |
| 20 | POST | `/api/patient/reviews` | Patient |
| 21 | GET | `/api/patient/saved-centers` | Patient |
| 22 | PUT | `/api/patient/saved-centers/{centerId}` | Patient |
| 23 | DELETE | `/api/patient/saved-centers/{centerId}` | Patient |
| 24 | GET | `/api/center/workspace` | Center |
| 25 | PUT | `/api/center/workspace/center` | Center |
| 26 | PUT | `/api/center/workspace/hours` | Center |
| 27 | POST | `/api/center/workspace/images` | Center |
| 28 | DELETE | `/api/center/workspace/images/{imageId}` | Center |
| 29 | DELETE | `/api/center/workspace/images` | Center |
| 30 | POST | `/api/center/workspace/services` | Center |
| 31 | PUT | `/api/center/workspace/services/{serviceId}` | Center |
| 32 | PUT | `/api/center/workspace/therapists/{therapistId}` | Center |
| 33 | POST | `/api/center/workspace/patients` | Center |
| 34 | PUT | `/api/center/workspace/patients/{patientId}` | Center |
| 35 | POST | `/api/center/workspace/patients/{patientId}/notes` | Center |
| 36 | PUT | `/api/center/workspace/appointments/{appointmentId}/status` | Center |
| 37 | POST | `/api/center/workspace/reviews/{reviewId}/reply` | Center |
| 38 | GET | `/api/center/workspace/preferences` | Center |
| 39 | PUT | `/api/center/workspace/preferences` | Center |
| 40 | POST | `/api/center/therapists` | Center |
| 41 | GET | `/api/schedules/therapists/{therapistId}` | Center |
| 42 | POST | `/api/schedules/therapists/{therapistId}` | Center |
| 43 | PUT | `/api/schedules/therapists/{therapistId}/{scheduleId}` | Center |
| 44 | DELETE | `/api/schedules/therapists/{therapistId}/{scheduleId}` | Center |
| 45 | GET | `/api/therapist/profile` | Therapist |
| 46 | PUT | `/api/therapist/profile` | Therapist |
| 47 | GET | `/api/therapist/workspace` | Therapist |
| 48 | POST | `/api/therapist/workspace/images` | Therapist |
| 49 | GET | `/api/therapist/workspace/preferences` | Therapist |
| 50 | PUT | `/api/therapist/workspace/preferences` | Therapist |
| 51 | POST | `/api/conversations` | Patient |
| 52 | GET | `/api/conversations/{id}/messages` | participant |
| 53 | POST | `/api/conversations/{id}/messages` | participant |
| 54 | POST | `/api/conversations/{id}/read` | participant |
| 55 | POST | `/api/notifications/read` | authenticated |
| 56 | POST | `/api/assessment` | Patient |
| 57 | GET | `/api/assessment/history` | Patient |
| 58 | GET | `/api/assessment/{assessmentId}` | Patient |
| 59 | GET | `/api/admin/center-applications` | Admin |
| 60 | GET | `/api/admin/center-applications/{id}` | Admin |
| 61 | POST | `/api/admin/center-applications/{id}/review` | Admin |
| 62 | GET | `/api/admin/dashboard` | Admin |
| 63 | GET | `/api/admin/activity` | Admin |
| 64 | GET | `/api/admin/notifications` | Admin |
| 65 | GET | `/api/admin/centers` | Admin |
| 66 | GET | `/api/admin/centers/{id}` | Admin |
| 67 | PUT | `/api/admin/centers/{id}/status` | Admin |
| 68 | PUT | `/api/admin/centers/{id}` | Admin |
| 69 | DELETE | `/api/admin/centers/{id}` | Admin |
| 70 | GET | `/api/admin/patients` | Admin |
| 71 | GET | `/api/admin/patients/{id}` | Admin |
| 72 | PUT | `/api/admin/patients/{id}/status` | Admin |
| 73 | PUT | `/api/admin/patients/{id}` | Admin |
| 74 | DELETE | `/api/admin/patients/{id}` | Admin |
| 75 | GET | `/api/admin/physiotherapists` | Admin |
| 76 | POST | `/api/admin/physiotherapists` | Admin |
| 77 | GET | `/api/admin/physiotherapists/{id}` | Admin |
| 78 | PUT | `/api/admin/physiotherapists/{id}` | Admin |
| 79 | PUT | `/api/admin/physiotherapists/{id}/status` | Admin |
| 80 | DELETE | `/api/admin/physiotherapists/{id}` | Admin |
| 81 | GET | `/api/admin/appointments` | Admin |
| 82 | GET | `/api/admin/appointments/options` | Admin |
| 83 | POST | `/api/admin/appointments` | Admin |
| 84 | PUT | `/api/admin/appointments/{id}` | Admin |
| 85 | DELETE | `/api/admin/appointments/{id}` | Admin |
| 86 | GET | `/api/admin/reviews` | Admin |
| 87 | POST | `/api/admin/reviews/{id}/moderate` | Admin |
| 88 | GET | `/api/admin/settings` | Admin |
| 89 | PUT | `/api/admin/settings` | Admin |
| 90 | POST | `/api/admin/settings/image` | Admin |
| 91 | GET | `/` | anonymous (plain text) |

Plus the SignalR endpoint `/hubs/messages` and static `/uploads/{file}`.

---

### Document maintenance

When the API changes, update the affected section and the endpoint index together. The source of truth
remains `Controllers/`, `Services/`, `Data/ApplicationDbContext.cs` and the DTO folders; this document
is a map of them, not a replacement.


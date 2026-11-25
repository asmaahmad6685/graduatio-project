# Backend changes after `e185ebf`

> **Scope.** Everything that changed in `gysi-proj/` since commit
> `e185ebffaf44ec64b69905184c00d3ced066c98c` on branch `Final-Backend` (HEAD `cf2dbf9`):
> **41 commits, 78 files, +26,949 / −423 lines.** Migrations are listed by name only because their
> bodies are generated.
>
> The document has two parts: §1–§11 describe what those commits changed, and §12 records the
> in-file purpose comments that were added afterwards and are still **uncommitted** in the working tree.
>
> Companion document: `docs/BACKEND_STRUCTURE_AND_USE_CASES.md` (full structure, API and use cases).

---

## 1. Repository, tooling and docs

| File | Status | What it does | Last commit |
| --- | --- | --- | --- |
| `.gitignore` | modified | Also ignores `.freebuff` (local agent output) and `graduatio project/wwwroot/uploads/*` (runtime uploads must not be committed). | `bb4d790` |
| `docs/BACKEND_STRUCTURE_AND_USE_CASES.md` | added | Full backend guide: startup pipeline, conventions, all 29 tables, migrations, HTTP API reference, services, SignalR, seeding and use cases by actor. | `cf2dbf9` |
| `docs/PhysioGuide.postman_collection.json` | added | Postman collection for every route, grouped by controller, with the auth and body fields ready to run. | `cf2dbf9` |
| `docs/api-contract.md` | modified | Documents `?role=` on `POST /api/Auth/login`, the `specialties` catalogue in `GET /api/center/workspace`, centre-created patients (`POST/PUT /api/center/workspace/patients`), `POST /api/center/therapists` and the therapist `phoneNumber` field. | `50eb7e5` |
| `PhysioGuide_Google_React_Handoff.md` | added | Hand-off notes for the frontend team describing the Google sign-in challenge/nonce/ticket flow. | `61688f7` |
| `graduatio project/graduatio project.csproj` | modified | Adds `Google.Apis.Auth` 1.77.0 (token validation) and a `UserSecretsId`, so the JWT key and the Google client ID can live in user secrets outside the repo. | `61688f7` |
| `appsettings.json` | modified | Adds the `GoogleAuth` section (`Enabled`, `WebClientId`) and rotates `Jwt:Key` to a 32-byte base64 value; the old readable key no longer signs tokens. | `8ea9f13` |
| `appsettings.Development.json` | modified | Whitespace/formatting only — connection string unchanged. | `089167b` |

---

## 2. Startup pipeline (`Program.cs`)

| Change | What it does |
| --- | --- |
| SignalR | `AddSignalR()` plus `MapHub<MessagingHub>("/hubs/messages")`; the JWT handler now reads `?access_token=` for `/hubs` paths because WebSocket/SSE requests cannot send an `Authorization` header. |
| `NotificationService` | Registered scoped; every controller that raises events injects it (see §10). |
| `GoogleAuthOptions` | Bound and validated at startup: if `Enabled` is true and `WebClientId` is missing or is still the placeholder, the app refuses to start instead of ignoring the misconfiguration. `GoogleNonceStore`, `GoogleRegistrationTicketStore`, `IGoogleTokenValidator` and `TimeProvider` are registered as singletons. |
| Rate limiter | `UseRateLimiter()` with the fixed-window policy `GoogleAuth`: 20 requests per minute per IP, `429` with `Retry-After: 60` and a `rate_limit_exceeded` JSON body. |
| JWT key guard | Startup throws when `Jwt:Key` is missing or shorter than 32 UTF-8 bytes (User Secrets in Development, `Jwt__Key` elsewhere). |
| Routing order | `UseRouting()` is now explicit before `UseRateLimiter()`/`UseAuthentication()`. |
| Auto-migration | On start the app runs `db.Database.Migrate()`; a failure is printed as `[Migrate] Failed: …` and the API still starts, so a schema problem cannot take the whole service down. Seeding runs afterwards as before. |

`Options/GoogleAuthOptions.cs` (added) holds `SectionName = "GoogleAuth"`, `Enabled`, `WebClientId`
and `HasValidWebClientId()`, which rejects empty values and the `YOUR_GOOGLE_WEB_CLIENT_ID`
placeholder.

---

## 3. Data model and migrations

### New tables and columns

| File | Status | What it adds | Last commit |
| --- | --- | --- | --- |
| `Models/CenterPatient.cs` | added | Join row (`CenterId`, `PatientId`, `CreatedAt`) that makes a patient appear in a centre's list before they have an appointment or a note. | `e6e1b2a` |
| `Models/PatientSavedCenter.cs` | added | Favourite centres of a patient, keyed by `(PatientId, CenterId)` with `SavedAt`. | `4ca9bd5` |
| `Models/ExternalLogin.cs` | added | One linked Google (or future provider) identity per user: `Provider`, `ProviderSubject`, `CreatedAt`; unique on `(Provider, ProviderSubject)` and `(UserId, Provider)`. | `61688f7` |
| `Models/Admin.cs` | modified | `ProfileImageUrl` (max 500) for the admin avatar. | `3916521` |
| `Models/Center.cs` | modified | `AddressArabic` (max 300) beside the English address. | `8779966` |
| `Models/Patient.cs` | modified | `DateOfBirth` is now `DateOnly?` (was `DateTime?`) and `City` (max 100) was added for admin filtering. | `6538e5c` |
| `Models/Service.cs` | modified | `ImageUrl` (max 500) — the card image shown to centres and patients. | `79d3b0a` |
| `Models/Therapist.cs` | modified | `FullNameAr` and `BioAr` (max 150 / 1000) for the Arabic UI. | `eed2f98` |
| `Models/User.cs` | modified | `PasswordHash` became nullable: Google-only accounts have no local password. | `61688f7` |
| `Data/ApplicationDbContext.cs` | modified | `DbSet`s for the three new tables, their keys/indexes/delete behaviour, and max lengths for the new string columns. | `8779966` |
| `Data/DbSeeder.cs` | modified | Demo patient now seeds `DateOfBirth` as `DateOnly`; the rest of the fixture data is unchanged. | `bb4d790` |

### Migrations (generated, listed for the schema history)

| Migration | Adds |
| --- | --- |
| `20261013110822_AddPatientDateOfBirthAndCity` | `Patients.City` (`nvarchar(100)`); `DateOfBirth` already existed as a column and only its CLR type became `DateOnly`. |
| `20261013153819_AddPatientSavedCenters` | `PatientSavedCenters` table. |
| `20261019185323_AddExternalLoginsAndNullablePasswordHash` | `ExternalLogins` table, nullable `Users.PasswordHash`. |
| `20261030170957_AdminRelated` | Empty `Up`/`Down` — placeholder migration from the admin-DTO commit; it changes no schema. |
| `20261031121612_AddCenterPatients` | `CenterPatients` join table. |
| `20261102110402_AddTherapistArabicNameAndBio` | `Therapists.FullNameAr`, `Therapists.BioAr`. |
| `20261111222442_AddAdminProfileImage` | `Admins.ProfileImageUrl`. |
| `20261112114431_AddServiceImage` | `Services.ImageUrl`. |
| `20261112164016_AddCenterAddressArabic` | `Centers.AddressArabic`. |

`ApplicationDbContextModelSnapshot.cs` was regenerated by those migrations.

---

## 4. Authentication and Google sign-in

### `Controllers/AuthController.cs` — `api/Auth` (`[ApiController]`, anonymous by default)

| Route | What it does |
| --- | --- |
| `POST api/Auth/register/patient` | Creates a patient account once the email is free; hashes the password and opens a session. |
| `POST api/Auth/register/center` | Creates a centre account with its `User` in one transaction, status pending approval. |
| `POST api/Auth/login?role=patient\|center\|admin` | Role-scoped login. An account that belongs to another portal gets the same `401 unauthenticated` body as a wrong password, so the portal an email uses is never revealed; the response carries `patientId`/`centerId`/`therapistId`/`adminId` for the role. |
| `POST api/Auth/refresh` | Exchanges a stored refresh token for a new access token. |
| `POST api/Auth/logout` | Revokes the refresh token (JWTs themselves stay stateless). |
| `POST api/Auth/change-password` | Changes the password of the signed-in account. |
| `forgot-password`, `reset-password` | Kept in the file for reference but marked `[NonAction]`: password recovery is disabled and neither method is reachable over HTTP. |

### `Controllers/GoogleAuthController.cs` — `api/Auth/google` (added, anonymous, rate-limited)

| Route | What it does |
| --- | --- |
| `POST challenge` | Issues a server challenge id, nonce and 5-minute expiry bound to the browser's verifier. |
| `POST sign-in` | Validates the Google ID token against the nonce; existing accounts get a session, unknown emails get a one-time registration ticket. |
| `POST register/patient` | Completes a patient sign-up that started with Google, using the ticket and verifier. |
| `POST register/center` | Same for a centre sign-up (pending approval, no password). |

### Supporting pieces (all added in `61688f7`)

| File | What it does |
| --- | --- |
| `Services/GoogleNonceStore.cs` | In-memory, thread-safe store of pending challenges: 32-byte ids/nonces, tab binding hashed with SHA-256, constant-time comparison, 5-minute expiry, hard cap of 1024 entries (`GoogleFlowCapacityException`). Single-instance only by design. |
| `Services/GoogleRegistrationTicketStore.cs` | One-time registration tickets (ticket + verifier → `GoogleRegistrationIdentity`), also in memory and time-limited. |
| `Services/GoogleTokenValidator.cs` | `IGoogleTokenValidator`/`GoogleTokenValidator`: verifies the Google ID token signature, audience and nonce with `Google.Apis.Auth`, returning `null` for anything invalid. |
| `Services/PasswordService.cs` | `VerifyPassword` now accepts a nullable hash and returns `false` instead of throwing for Google-only accounts. |
| `DTOs/Auth/GoogleAuthDtos.cs` | `GoogleChallengeDto`, `GoogleSignInDto`, `GoogleRegistrationDto` and the patient/centre register variants. |

---

## 5. Admin portal API

### `Controllers/AdminController.cs` — `api/admin/center-applications` (admin only, `23bc7c5`)

- `GET /` — centre applications, optionally filtered by `status`.
- `GET /{id}` — one application with everything the review dialog shows.
- `POST /{id}/review` — approve/reject an application and notify the applicant.

### `Controllers/AdminManagementController.cs` — `api/admin` (added; the whole admin portal, `261fbd6`)

| Area | Routes |
| --- | --- |
| Dashboard | `GET /dashboard?year=` (KPIs, monthly series, recent activity), `GET /activity` (paged, newest first), `GET /notifications` (unread admin bell). |
| Centres | `GET /centers`, `GET /centers/{id}`, `PUT /centers/{id}`, `PUT /centers/{id}/status`, `DELETE /centers/{id}` (also removes its accounts). |
| Patients | `GET /patients` (paged, filtered, computed age), `GET /patients/{id}`, `PUT /patients/{id}`, `PUT /patients/{id}/status`, `DELETE /patients/{id}` (with their appointments). |
| Therapists | `GET /physiotherapists`, `POST /physiotherapists` (creates the login inside an approved centre), `GET /physiotherapists/{id}`, `PUT /physiotherapists/{id}`, `PUT /physiotherapists/{id}/status`, `DELETE /physiotherapists/{id}` (with their schedule). |
| Appointments | `GET /appointments`, `GET /appointments/options` (patients, therapists, services, times for the forms), `POST /appointments`, `PUT /appointments/{id}`, `DELETE /appointments/{id}`. |
| Reviews | `GET /reviews`, `POST /reviews/{id}/moderate` (hide / restore / remove). |
| Settings | `GET /settings`, `PUT /settings`, `POST /settings/image` (profile photo upload). |

### `DTOs/Admin/AdminDtos.cs` (added)

`UpdateStatusDto`, `AdminCreateTherapistDto`, `AdminCreateAppointmentDto`, `AdminUpdatePatientDto`,
`AdminUpdateTherapistDto`, `AdminUpdateAppointmentDto`, `AdminUpdateCenterDto`, `ModerateReviewDto`,
`AdminSettingsDto`, `AdminNotificationSettingsDto`.

---

## 6. Centre API

### `Controllers/CenterWorkspaceController.cs` — `api/center` (centre role)

Sixteen routes backing the centre portal:

- `GET /workspace` — the whole dashboard payload: profile, staff, patients, services, appointments, notifications, conversations, specialties catalogue and today's schedule.
- Profile: `PUT /workspace/center`, `PUT /workspace/hours`, `POST /workspace/images`, `DELETE /workspace/images/{imageId}`, `DELETE /workspace/images?url=`.
- Services: `POST /workspace/services`, `PUT /workspace/services/{serviceId}` (image and price included).
- Therapists: `PUT /workspace/therapists/{therapistId}` (professional fields only).
- Patients: `POST /workspace/patients` (user + patient in one transaction, linked to the centre), `PUT /workspace/patients/{patientId}`, `POST /workspace/patients/{patientId}/notes`.
- Appointments and reviews: `PUT /workspace/appointments/{appointmentId}/status`, `POST /workspace/reviews/{reviewId}/reply`.
- Preferences: `GET /workspace/preferences`, `PUT /workspace/preferences`.

### `Controllers/CenterController.cs` — `api/Center`

- `POST api/Center/therapists` — the only therapist-creation path: creates the `User` login and the
  `Therapist` row for the signed-in centre in one transaction and notifies the admins.

### `Controllers/CentersController.cs` — `api/centers` (anonymous public discovery)

- `GET api/centers?city=` — approved centres only, mapped with `CenterMapper`.
- `GET api/centers/{centerId}/slots?therapistId=&serviceId=&date=` — bookable slots, using the same
  rules as booking (centre hours, therapist schedule, service duration, taken slots, no past times).

### DTOs and mappers

| File | Status | What it does |
| --- | --- | --- |
| `DTOs/Center/CenterWorkspaceDtos.cs` | modified | `UpdateCenterInfoDto`, `UpdateWorkingHoursDto`, `AddCenterImageDto`, `SaveServiceDto`, `CreateCenterPatientDto`, `UpdateCenterPatientDto`, `UpdateAppointmentStatusDto`, `AddPatientNoteDto`, `UpdateTherapistDto`, `ReplyToReviewDto` — validation for every workspace write. |
| `Services/CenterMapper.cs` | modified | Public centre payload now carries `addressArabic`, `websiteUrl`, `latitude`, `longitude`, `locationConfirmed`, service `imageUrl`, therapist `nameAr`/`bioAr`; the centre's own photo wins over the first gallery image. |
| `Services/AppointmentMapper.cs` | modified | Adds `therapistNameAr` to the appointment payload. |

---

## 7. Therapist API

| File | Routes | What it does |
| --- | --- | --- |
| `Controllers/TherapistController.cs` (`api/Therapist`) | `GET profile`, `PUT profile` | Reads and edits the signed-in therapist's own profile, including the new Arabic name/bio and the profile photo. |
| `Controllers/TherapistWorkspaceController.cs` (`api/therapist`) | `POST workspace/images`, `GET workspace`, `GET/PUT workspace/preferences` | Therapist dashboard (own profile, patients, appointments, messages), photo upload and the notification switches behind the bell. |
| `DTOs/Therapist/CreateTherapistDto.cs` | — | Centre-side therapist creation fields, now including `phoneNumber` and account credentials. |
| `DTOs/Therapist/UpdateTherapistProfileDto.cs` | — | Self-service profile edit fields (Arabic name/bio, image, experience, specialty). |

---

## 8. Patient API

`Controllers/PatientController.cs` — `api/patient` (patient role):

| Route | What it does |
| --- | --- |
| `GET appointments` | Paged appointment history of the signed-in patient. |
| `GET messaging-workspace?since=` | Conversations and messages, with a `since` cursor for polling. |
| `GET account` | Profile plus the notification switches. |
| `PUT account/profile` | Saves the editable profile fields (name, phone, birthdate, city, gender, image). |
| `PUT account/preferences` | Saves the notification switches. |
| `POST account/image` | Stores a new profile photo and returns its URL. |
| `POST reviews` | Adds a review to the patient's newest unreviewed visit. |
| `GET saved-centers` | Favourite centres. |
| `PUT saved-centers/{centerId}` | Adds a centre to the favourites (idempotent). |
| `DELETE saved-centers/{centerId}` | Removes it. |

Supporting files: `DTOs/Patient/PatientProfileDto.cs`, `SavedCenterDto.cs`, `CreatePatientReviewDto.cs`
(added), `PatientAccountDtos.cs` and `UpdatePatientProfileDto.cs` (modified, with `Validate` rules for
birthdate/city/image).

---

## 9. Appointments and pain assessments

| File | Routes | What it does |
| --- | --- | --- |
| `Controllers/AppointmentsController.cs` (`api/appointments`) | `POST request` | Patient booking: patient-only role check, slot validation, booking and a notification to the centre. |
| `Controllers/AssessmentController.cs` (`api/assessment`, patient role) | `POST /`, `GET history`, `GET {assessmentId}` | Scores a submitted pain assessment, stores it for the patient, and returns saved history/details with the result and advice. |

---

## 10. Messaging and notifications

| File | Status | What it does |
| --- | --- | --- |
| `Controllers/ConversationsController.cs` (`api/conversations`) | modified | `POST /` opens (or returns) a conversation, `GET /{id}/messages?since=` reads it, `POST /{id}/messages` stores a message and pushes it to the other participant, `POST /{id}/read` marks it read. Opening a conversation requires the patient role; the rest require any signed-in user. |
| `DTOs/Messaging/MessageDtos.cs` | modified | `SendMessageDto` (2000-character limit, 2 MB attachments), `OpenConversationDto`, `MarkMessagesReadDto`, `MarkNotificationReadDto`. |
| `Hubs/MessagingHub.cs` | added | `[Authorize]` SignalR hub at `/hubs/messages`. It exposes no client-callable methods: connections only receive, keyed by the JWT `NameIdentifier`; sending stays REST. |
| `Services/NotificationService.cs` | added | `NotifyAsync` (one user or many, one save and one push), optional duplicate suppression for unread rows, and `NotifyAdminsAsync` for admin-wide events. Failures are logged and never roll back the action that triggered them. |

---

## 11. Removed endpoints

| File | Status | Why |
| --- | --- | --- |
| `Controllers/AvailabilityController.cs` | deleted | Slot rules moved to `GET api/centers/{centerId}/slots`. |
| `Controllers/CenterAppointmentsController.cs` | deleted | Centre appointment writes now live in `PUT api/center/workspace/appointments/{id}/status`. |

Both removals are in `9e13beb` ("remove unused controllers and methods from the project").

---

## 12. In-file purpose comments (working tree, not committed)

Every file in this document's scope received one-line comments above its members — routes named with
verb and path, services, mappers, caches, models and startup blocks. Members that already carried a
comment (Arabic notes, JSDoc, route descriptions) were left alone.

**23 files, 125 added lines, 0 deletions.** Nothing is staged or committed: `HEAD` is still `cf2dbf9`,
the only modified files are those 23 `.cs` files, and this document is still untracked.

| File | Comments | Examples of what was documented |
| --- | --- | --- |
| `Controllers/AdminController.cs` | 6 | Three routes, the admin-only policy, the two helpers. |
| `Controllers/AdminManagementController.cs` | 33 | All 29 routes plus the shared query/serialisation helpers. |
| `Controllers/AppointmentsController.cs` | 2 | Patient-booking route, notification dependency. |
| `Controllers/AssessmentController.cs` | 5 | Submit, history and detail routes, scoring helper. |
| `Controllers/AuthController.cs` | 3 | Role-scoped login, disabled recovery actions. |
| `Controllers/CenterWorkspaceController.cs` | 16 | Every workspace write, image handling, patient linking. |
| `Controllers/CentersController.cs` | 1 | Public approved-centres route. |
| `Controllers/ConversationsController.cs` | 3 | Open/read/send/read-receipt routes, realtime push. |
| `Controllers/GoogleAuthController.cs` | 13 | The four Google routes and the shared error wrapper. |
| `Controllers/PatientController.cs` | 9 | Account, saved centres, reviews, messaging workspace. |
| `Controllers/TherapistController.cs` | 1 | Profile update route. |
| `Controllers/TherapistWorkspaceController.cs` | 4 | Workspace, image and preferences routes. |
| `DTOs/Patient/UpdatePatientProfileDto.cs` | 1 | `Validate`. |
| `DTOs/Service/CreateServiceDto.cs` | 1 | `Validate`. |
| `Data/ApplicationDbContext.cs` | 2 | New relationship blocks. |
| `Data/DbSeeder.cs` | 7 | Seed sections and helpers. |
| `Options/GoogleAuthOptions.cs` | 1 | `HasValidWebClientId`. |
| `Program.cs` | 1 | Migration block. |
| `Services/AppointmentMapper.cs` | 1 | Arabic therapist-name projection. |
| `Services/CenterMapper.cs` | 2 | Image priority and gallery mapping. |
| `Services/GoogleNonceStore.cs` | 7 | Issue/consume/expiry helpers and the capacity exception. |
| `Services/GoogleRegistrationTicketStore.cs` | 5 | Issue/take helpers. |
| `Services/GoogleTokenValidator.cs` | 1 | `ValidateAsync`. |

Build after the comment pass:

```bash
dotnet build "graduatio project/graduatio project.csproj" --nologo -v q
# Build succeeded.
#     0 Warning(s)
#     0 Error(s)
```

---

## 13. Verifying this document

```bash
# the commits covered here
git log --oneline e185ebf..HEAD          # 41 commits
git diff --stat e185ebf..HEAD            # 78 files, +26949 / -423

# the uncommitted comment pass
git diff --shortstat                     # 23 files changed, 125 insertions(+)
git status --porcelain                   # only those 23 .cs files are modified

# one change per section, e.g. Google auth or realtime messaging
git show --stat 61688f7
git show --stat 87eb000
```

The comment pass is **local only** — no commit, stage, push or history change was made.

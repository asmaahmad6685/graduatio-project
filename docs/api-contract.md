# PhysioGuide API contract

Everything below is implemented and covered by an end-to-end HTTP check suite
(163 assertions). The generated OpenAPI document lives next to this file at
[`openapi.json`](openapi.json), and Swagger UI is published in **every**
environment at `/swagger`.

---

## 1. Environments and base URL

| Environment | Base URL |
|---|---|
| Production | `https://physio-guide.runasp.net` |
| Local | `http://127.0.0.1:5222` (see `Properties/launchSettings.json`) |

All paths in this document are relative to the base URL. Swagger JSON is at
`GET /swagger/v1/swagger.json`.

---

## 2. Auth

* **Scheme:** `Authorization: Bearer <accessToken>` on every non-public
  endpoint. No cookie, no `X-` header.
* **Access token lifetime:** 60 minutes (`Jwt:ExpireMinutes`). The login and
  refresh responses report it in `expiresIn` (**seconds**, `3600`).
* **Refresh token:** opaque, **7 days** (`Jwt:RefreshDays`), **single use** —
  every `/refresh` rotates it and the previous value stops working.
* **Logout** revokes the presented refresh token. The access token stays
  valid until it expires; state lives in the refresh token, not the JWT.

### `role` values

| Where | Values |
|---|---|
| Login / register **responses** | `patient`, `center`, `physiotherapist`, `admin` (always lowercase) |
| JWT `Role` claim (drives `[Authorize(Roles=…)]`) | `Patient`, `Center`, `Therapist`, `Admin` |

### Which 401 means what

| `code` | Meaning | Frontend action |
|---|---|---|
| `unauthenticated` | No/invalid token | Redirect to login |
| `token_expired` | Access token past `exp` | Call `POST /api/Auth/refresh`, retry once |
| `invalid_refresh_token` | Refresh token expired/revoked/reused | Clear session → login |
| `forbidden` (403) | Authenticated but wrong role | Show "no permission" |

Refresh tokens are revoked when the user changes their password, so that flow
forces a fresh login on every other device.

---

## 3. Error contract (one shape for every endpoint)

**Validation — 400** (produced by `[ApiController]`, keys are the **C#
property names**, unchanged from what you already map):

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "FullName": ["FullName is required."],
    "Email": ["The Email field is not a valid e-mail address."],
    "Gender": ["Gender must be either male or female."]
  }
}
```

For **query-string** parameters (only `GET /api/centers/{id}/slots`) the keys
are the query parameter names: `date`, `therapistId`, `serviceId`.

**Every other non-2xx** carries a stable machine-readable `code`:

```json
{ "code": "duplicate_email", "message": "Email is already registered.",
  "errors": { "Email": ["An account with this email already exists."] } }
```

| Status | Codes |
|---|---|
| 400 | `invalid_upload`, `file_too_large`, `unsupported_media_type` |
| 401 | `unauthenticated`, `token_expired`, `invalid_refresh_token` |
| 403 | `forbidden`, `account_not_active`, `account_rejected`, `account_suspended`, `center_profile_missing` |
| 404 | `not_found` |
| 409 | `duplicate_email`, `slot_unavailable` |
| 429 | `rate_limited` (+ `Retry-After` header, seconds) |
| 500 | `internal_error` — never leaks stack traces or SQL |

* **Bad credentials → 401 with no `errors` key at all.** That absence is the
  signal for "email or password is incorrect".
* **Duplicate email → 409** with `errors.Email`, so the form can attach it to
  the field.
* **Gender** (`male`/`female`) and **PhoneNumber** are validated on register.

---

## 4. Rate limits

| Policy | Routes | Limit |
|---|---|---|
| `auth-login` | `POST /api/Auth/login` | 10 / minute |
| `auth-register` | `POST /api/Auth/register/*` | 5 / minute |
| `auth-forgot` | `POST /api/Auth/forgot-password` | 3 / minute |

Over the limit → `429` + `Retry-After` + `{"code":"rate_limited",…}`.

---

## 5. Cross-cutting decisions

| Concern | Decision |
|---|---|
| Dates | `YYYY-MM-DD` |
| Times | `HH:MM`, 24-hour |
| Durations | minutes (int) |
| **IDs** | **Strings in responses** (`"id": "12"`). Requests accept **either** a string or a number — `FlexibleIntConverter` parses both. |
| Pagination | **None — full lists**, no truncation. No `{items,total}` envelope yet. |
| Messaging delivery | **Polling.** `GET /api/patient/messaging-workspace?since=<ISO-8601>` and `GET /api/conversations/{id}/messages?since=<ISO-8601>` return only what changed. `since` is UTC. A typical cadence is 5–10 s while a thread is open. |
| Localization | Plain strings. Read endpoints return one string, **English preferred, Arabic fallback**. Write endpoints accept both `nameEnglish`/`nameArabic` (the center workspace reads and writes both so nothing is lost). |
| Uploads | `multipart/form-data`, field name **`file`**, max **2 MB**, types **jpg / jpeg / png / webp / gif**. Response is `{"url":"/uploads/<guid>.<ext>"}` — a path **relative to the API origin**, served by static files. |
| Timezone | Scheduling values (`date`, `time`, `StartDateTime`) are naive server-local wall clock. Timestamps (`createdAt`, `sentAt`) are UTC. Set the host clock/timezone to the clinic's zone. |

---

## 6. Endpoints

### 6.1 Auth

| Method | Path | Auth |
|---|---|---|
| POST | `/api/Auth/register/patient` | public |
| POST | `/api/Auth/register/center` | public |
| POST | `/api/Auth/login` | public (rate limited) |
| POST | `/api/Auth/refresh` | public |
| POST | `/api/Auth/logout` | bearer |
| POST | `/api/Auth/change-password` | bearer |
| POST | `/api/Auth/forgot-password` | public (rate limited) |
| POST | `/api/Auth/reset-password` | public |

**`POST /api/Auth/login?role=patient`** — request
`{"email":"patient@physioguide.dev","password":"Patient123!"}`

The optional `role` query names the portal the login page belongs to:
`patient`, `center` (therapists sign in there too) or `admin`. An account that
belongs to another portal receives the same `401 unauthenticated` body as a
wrong password, so this endpoint never reveals which portal an email uses.
Omitting `role` keeps the legacy behaviour.

```json
{
  "message": "Login successful.",
  "accessToken": "eyJhbGciOi…",
  "refreshToken": "9f2c…",
  "expiresIn": 3600,
  "userId": 2,
  "patientId": "2",
  "centerId": null,
  "therapistId": null,
  "fullName": "Sara Nablus",
  "email": "patient@physioguide.dev",
  "role": "patient"
}
```

`centerId` / `therapistId` are strings for the matching role and `null`
otherwise, so one endpoint routes every portal.

* `401 {"code":"unauthenticated","message":"Invalid email or password."}`
  (no `errors`) for bad email **or** bad password **or** an account signing in
  from another portal's login page — identical bodies.
* `403` `account_not_active` / `account_rejected` / `account_suspended` for a
  centre that is pending, rejected or suspended.
* `429` after 10 attempts in a minute.

**`POST /api/Auth/refresh`** — `{"refreshToken":"9f2c…"}` →
`200 {"message","accessToken","refreshToken","expiresIn"}`; the returned
`refreshToken` **replaces** the one you sent.
`401 {"code":"invalid_refresh_token",…}` when it is expired, revoked, or
already rotated.

**`POST /api/Auth/logout`** — `{"refreshToken":"9f2c…"}` (optional) → `204`.
Idempotent: an unknown token still returns `204`.

**`POST /api/Auth/change-password`** — `{"currentPassword","newPassword"}` →
`200 {"message":"Password changed successfully."}`; `400` with
`errors.CurrentPassword` if the current password is wrong. Revokes every
refresh token for the user.

**`POST /api/Auth/register/patient`** → `200`
`{"message","userId","patientId","email","role"}` (unchanged contract).
`409` `duplicate_email` with `errors.Email`; `400` validation keyed
`FullName`/`Email`/`PhoneNumber`/`Password`/`Gender`.

**`POST /api/Auth/register/center`** → `200`
`{"message","userId","centerId","email","role","status":"Pending"}`.

> Note: `POST /api/Auth/forgot-password` still echoes `resetToken` in the
> response body. It is a development affordance and should be removed (or
> switched to real email delivery) before production.

### 6.2 Discovery & booking

| Method | Path | Auth |
|---|---|---|
| GET | `/api/centers?city=Nablus` | public |
| GET | `/api/centers/{id}` | public |
| GET | `/api/centers/{centerId}/slots?therapistId&serviceId&date` | public |
| POST | `/api/appointments/request` | bearer (patient) |
| GET | `/api/patient/appointments?status&from&to` | bearer (patient) |

**Center object** (exactly the fields the UI renders):

```json
{
  "id": "1",
  "name": "Nablus Physio Center",
  "imageUrl": "https://images.unsplash.com/…",
  "gallery": ["…", "…"],
  "about": "A specialised physiotherapy and rehabilitation centre…",
  "address": "شارع الزهراء، نابلس",
  "city": "Nablus",
  "hours": {"weekStart":"08:00","weekEnd":"17:00",
            "fridayStart":"08:00","fridayEnd":"13:00"},
  "phone": "+970599000004",
  "email": "center@physioguide.dev",
  "services": [{
    "id": "1", "name": "Sports Massage", "description": "…",
    "price": 150, "duration": 30, "imageUrl": null, "specialty": "Sports Injury"
  }],
  "therapists": [{
    "id": "1", "name": "Dr. Lina Khoury", "imageUrl": "…",
    "specialty": "Sports Injury", "experience": 7, "about": "…",
    "rating": 4.5, "serviceIds": ["1","2"], "availability": "available",
    "credentials": ["BPT", "Certified Sports Physiotherapist"],
    "languages": ["Arabic","English","French"]
  }],
  "reviews": [{
    "id": "1", "rating": 5, "text": "…", "date": "2026-10-03",
    "reply": "Thank you Sara…", "author": "Sara Nablus", "authorImageUrl": "…"
  }],
  "rating": 4.5
}
```

Only **approved** centres are listed; a pending/rejected id returns
`404 not_found`. Only **active** services and **active** therapists are
included. Reviews with `IsVisible = false` are excluded. `rating` is the
average of visible reviews, `null` when there are none; a therapist's `rating`
comes from the reviews attached to **their** appointments. `availability` is
`"available"` only when the therapist is available **and** has a schedule for
today.

**`GET /api/centers/{centerId}/slots`** → `200 ["09:00","09:30", …]`

Availability is owned entirely by the backend and is the **same code path**
used to validate a booking:

1. centre must have working hours that day (`IsClosed = false`),
2. therapist must have an `IsAvailable` schedule that day,
3. the slot is clipped to the intersection of both windows,
4. stepped on a **30-minute grid**, each slot as long as the service,
5. never in the past,
6. never overlapping a non-cancelled/non-rejected appointment.

Errors: `400` with `errors.date` / `errors.therapistId` / `errors.serviceId`;
`404 not_found` for an unapproved centre, a therapist outside it, a service it
does not offer, or a therapist who does not provide that service. An empty
array means "closed / unavailable that day" — not an error.

**`POST /api/appointments/request`**

```json
{ "centerId": "1", "therapistId": "1", "serviceId": "1",
  "date": "2026-10-28", "time": "10:00", "notes": "Optional, ≤1000 chars" }
```

→ `201`

```json
{
  "id": "7", "patientId": "2", "therapistId": "1", "serviceId": "1",
  "date": "2026-10-28", "time": "10:00", "status": "pending",
  "duration": 30, "therapistImage": "…", "therapistAvailable": true,
  "centerId": "1", "centerName": "Nablus Physio Center",
  "serviceName": "Sports Massage", "therapistName": "Dr. Lina Khoury"
}
```

* `400` `errors.Date` / `errors.Time` for malformed input,
* `404 not_found` for an unapproved centre / foreign therapist / foreign or
  inactive service / a therapist who does not offer it,
* `409 {"code":"slot_unavailable"}` when the slot has gone — including when
  the requested time is in the past. The booking re-runs the exact slot
  computation above, so a slot is never double-booked.

**`GET /api/patient/appointments`** → `200 [appointment, …]` scoped to the
signed-in patient only. Optional `?status=pending|approved|…`,
`?from=YYYY-MM-DD`, `?to=YYYY-MM-DD`. Statuses come back lowercase
(`pending approved rejected cancelled completed`).

### 6.3 Patient portal

| Method | Path | Body |
|---|---|---|
| GET | `/api/patient/messaging-workspace?since=<ISO>` | – |
| GET | `/api/patient/account` | – |
| PUT | `/api/patient/account/profile` | see below |
| PUT | `/api/patient/account/preferences` | see below |
| POST | `/api/patient/account/image` | multipart `file` |

**`GET /api/patient/messaging-workspace`**

```json
{
  "patientProfile": {"id":"2","name":"Sara Nablus","email":"…","phone":"…",
                     "gender":"female","dateOfBirth":"1996-04-12",
                     "imageUrl":"…","status":"active"},
  "notificationPreferences": {"emailEnabled":true,"inAppEnabled":true,
                              "appointmentReminders":true,"marketing":false,
                              "language":"en"},
  "patients":     [{"id","name","imageUrl","gender","dateOfBirth"}],
  "therapists":   [{"id","name","imageUrl","specialty","centerId","centerName",
                    "experience","about","availability"}],
  "conversations":[{"id","patientId","therapistId","therapistName",
                    "therapistImage","centerId","centerName","lastMessage",
                    "lastMessageAt","unreadCount","messages":[…]}],
  "notifications":[{"id","type","title","message","isRead","date","relatedId"}]
}
```

`messages` items: `{"id","conversationId","senderId","senderName","text",
"attachment","isRead","sentAt"}` — `sentAt` is `yyyy-MM-ddTHH:mm:ssZ` (UTC),
`attachment` is a `/uploads/…` URL or `null`.

With `?since=`, `messages` and `notifications` contain only newer items while
`unreadCount` still counts over **all** messages, so polling does not inflate
or reset the badge.

**`GET /api/patient/account`** → `{"profile":{…},"preferences":{…}}`.

**`PUT /api/patient/account/profile`**
`{"fullName","phoneNumber","gender","dateOfBirth","profileImageUrl"}` →
`200 {"profile":{…}}`; `400` keyed `FullName` / `PhoneNumber` / `Gender` /
`DateOfBirth`.

**`PUT /api/patient/account/preferences`**
`{"emailEnabled","inAppEnabled","appointmentReminders","marketing","language"}`
with `language ∈ {en, ar}` → `200 {"preferences":{…}}`. Upserts the row.

**`POST /api/patient/account/image`** — `multipart/form-data`, field `file`,
≤ 2 MB, `jpg|jpeg|png|webp|gif` → `200 {"url":"/uploads/abc.png"}`.
`400 file_too_large` / `400 unsupported_media_type` / `400 invalid_upload`
(with `errors.file`).

### 6.4 Messaging & notifications

| Method | Path | Body | Result |
|---|---|---|---|
| GET | `/api/conversations/{id}/messages?since=<ISO>` | – | `200 {"messages":[…]}` |
| POST | `/api/conversations/{id}/messages` | `{"text":"…","attachment":"/uploads/x.png"}` | `200 message` |
| POST | `/api/conversations/{id}/read` | `{"messageIds":["12","13"]}` | `204` |
| POST | `/api/notifications/read` | `{"id":"4"}` | `204` |

Both conversation routes are open to **either side only** (the patient who
owns it or the therapist in it); anyone else gets `404 not_found`, so
conversation ids are not enumerable. `messageIds` not belonging to that
conversation are ignored. `/api/notifications/read` filters by the caller, so
a foreign id is `404 not_found`, never a silent success.

### 6.5 Admin — centre onboarding

| Method | Path | Auth |
|---|---|---|
| GET | `/api/admin/center-applications?status=pending\|approved\|rejected` | admin |
| GET | `/api/admin/center-applications/{id}` | admin |
| POST | `/api/admin/center-applications/{id}/review` | admin |

```json
{
  "id": "3", "name": "Khalili Rehab Center",
  "email": "pending-center@physioguide.dev", "phone": "+970599000003",
  "location": {
    "address": "Main Street, Hebron",
    "coordinates": {"latitude": 31.9038, "longitude": 35.2064},
    "confirmed": false
  },
  "approvalStatus": "pending",
  "submittedAt": "2026-10-20T09:14:00Z",
  "status": "pending",
  "reviewedAt": null,
  "reviewReason": null
}
```

**`POST …/review`** — `{"decision":"approved"}` or
`{"decision":"rejected","reason":"…"}` → `200` with the updated application
(so the list can update in place).

* `400` `errors.Reason` when rejecting without a reason,
* `400` `errors.Decision` for anything else,
* `404 not_found` for an unknown id,
* `403 forbidden` for a non-admin.

Approving stamps `ApprovedAt`/`ReviewedAt`/`ApprovedByAdminId`; that is the
only change that lets the centre sign in and appear in `/api/centers`.

### 6.6 Centre workspace (phase 2)

All under `/api/center`, `Roles = "Center"`.

| Method | Path | Body |
|---|---|---|
| GET | `/workspace` | – |
| PUT | `/workspace/center` | partial `nameEnglish nameArabic descriptionEnglish descriptionArabic city address profileImageUrl websiteUrl latitude longitude locationConfirmed` |
| PUT | `/workspace/hours` | `{"weekStart","weekEnd","fridayStart","fridayEnd"}` (`HH:MM`) |
| POST | `/workspace/images` | multipart `file` + `isPrimary` **or** `imageUrl` + `isPrimary` |
| DELETE | `/workspace/images/{imageId}` | – → `204` |
| POST | `/workspace/services` | `SaveServiceDto` → `201` |
| PUT | `/workspace/services/{serviceId}` | `SaveServiceDto` → `200` |
| DELETE | `/workspace/services/{serviceId}` | – → `204` (deactivates) |
| POST | `/therapists` | `CreateTherapistDto` (adds `phoneNumber`) → `200`; creates the login account |
| PUT | `/workspace/therapists/{therapistId}` | `UpdateTherapistDto` |
| DELETE | `/workspace/therapists/{therapistId}` | – → `204` (deactivates) |
| GET/POST | `/workspace/patients` | `CreateCenterPatientDto` → `201` on POST |
| PUT | `/workspace/patients/{patientId}` | `UpdateCenterPatientDto` → `200` |
| GET/POST | `/workspace/patients/{patientId}/notes` | `{"text":"…"}` |
| PUT | `/workspace/appointments/{appointmentId}/status` | `{"status":"approved\|rejected\|cancelled\|completed\|pending","reason"}` |
| POST | `/workspace/reviews/{reviewId}/reply` | `{"reply":"…"}` |
| GET/PUT | `/workspace/preferences` | same shape as patient preferences |

`GET /workspace` returns
`{center, therapists, services, patients, appointments, reviews,
notifications, conversations, specialties, today}` where `center` is the public
center object **plus** `nameArabic`, `descriptionArabic`, `email`, `phone`,
`approvalStatus`, `reviewedAt`, `reviewReason`, `specialties` is the active
`{id, nameEnglish, nameArabic}` catalogue the creation forms choose from, and
`today` is `{date, appointmentCount, appointments}` for the server's current
date.

`POST /workspace/patients` creates a `User` (`Role = Patient`, hashed password)
and a `Patient` in one transaction, then links both to the centre, so the new
record appears in `GET /workspace` immediately. It returns `201` with
`{id, name, email, phone, gender, dateOfBirth, imageUrl, status,
appointmentCount}`; a duplicate email returns `409 duplicate_email` with
`errors.Email`. `PUT /workspace/patients/{patientId}` updates the same fields
(no password) and refuses patients that do not belong to the centre. `POST
/therapists` (`/api/center/therapists`) is the only therapist creation path
because it also creates the staff login account.

`DELETE` on services/therapists is a **soft delete** (`IsActive = false`):
both can be referenced by existing appointments and a login account, so the
row is hidden from public endpoints rather than dropped.

Password change is shared: `POST /api/Auth/change-password`.

### 6.7 Therapist workspace (phase 2)

All under `/api/therapist`, `Roles = "Therapist"`.

| Method | Path | Body |
|---|---|---|
| GET | `/workspace` | – |
| PUT | `/workspace/schedule` | `{"dayOfWeek":"monday","startTime":"09:00","endTime":"16:00","isAvailable":true}` |
| PUT | `/workspace/appointments/{appointmentId}/status` | `{"status":"completed","reason"}` |
| GET | `/workspace/patients` | – |
| GET/PUT | `/workspace/preferences` | same shape as patient preferences |

`GET /workspace` returns
`{therapist, center, schedule, services, patients, appointments,
notifications, conversations, preferences, today}`.

Schedule errors: `400 errors.DayOfWeek` for an unknown day,
`400 errors.StartTime`/`errors.EndTime` for `HH:MM`, `400 errors.EndTime`
when `endTime ≤ startTime`.

`GET /api/Therapist/profile` and `PUT /api/Therapist/profile` (pre-existing)
still work unchanged.

---

## 7. Seed data

Seeded on startup — get-or-create, guarded by the admin email, so it runs on an
empty database **and** on one that already has accounts.

**Test accounts**

| Role | Email | Password |
|---|---|---|
| Admin | `admin@physioguide.dev` | `Admin123!` |
| Patient | `patient@physioguide.dev` | `Patient123!` |
| Centre | `center@physioguide.dev` | `Center123!` |
| Centre (2nd) | `center2@physioguide.dev` | `Center123!` |
| Centre (pending) | `pending-center@physioguide.dev` | `Center123!` |
| Therapist | `lina.khoury@physioguide.dev` | `Therapist123!` |
| Therapist | `omar.haddad@physioguide.dev` | `Therapist123!` |
| Therapist | `rania.saleh@physioguide.dev` | `Therapist123!` |
| Therapist | `khaled.amer@physioguide.dev` | `Therapist123!` |

**Content:** 4 specialties · 2 approved centres with 4 images, working hours
(Sat–Thu 08–17, Fri 08–13) and full about/contact blocks · 6 services ·
4 therapists with schedules (Sat–Thu 09–16) and service links · 1 approved
future appointment · 1 pending appointment · 5 completed appointments with 5
visible reviews · 1 conversation with 4 messages · 2 notifications · default
notification preferences.

---

## 8. Deployment cadence

Each group is independently live; flip `VITE_DATA_MODE` per feature as they
land. **All groups below are implemented in this repository** — the remaining
step is publishing the API host.

- [x] **Auth** — register/login/refresh/logout/change-password + error contract
- [x] **Discovery** — centres, centre detail, slots
- [x] **Booking** — request appointment, patient appointment list
- [x] **Patient** — account, preferences, image upload, messaging workspace
- [x] **Messaging** — send / read / polling with `since`
- [x] **Admin** — centre application list / detail / review
- [x] **Centre workspace** — full CRUD
- [x] **Therapist workspace** — workspace, schedule, statuses, preferences
- [x] **OpenAPI** — `/swagger` everywhere + `docs/openapi.json`

---

## 9. Verification

`163` end-to-end assertions covering: every success shape above; the 400/401/
403/404/409/429 branches; per-role route guards; `errors` key casing; refresh
rotation and logout revocation; double booking; past-slot rejection; upload
size/type limits; static file serving; CORS preflight; and the 429
`Retry-After` header. The one branch not exercised is the `500` handler —
forcing a genuine server fault in a test would be contrived; it logs the
exception and returns `{"code":"internal_error"}` with no detail.
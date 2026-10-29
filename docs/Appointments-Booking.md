# Appointments / Booking

Appointments use the existing `Appointment` and `AppointmentStatusHistory` entities. This feature does not change the schema, apply migrations, create bookings from availability reads, or implement Notifications, Reviews, Admin APIs, or Therapist management.

## Authentication and ownership

Send `Authorization: Bearer <JWT>` on every endpoint. Patient routes require role `Patient`; Center routes require role `Center`. Therapist accounts cannot call either set of routes.

The JWT `NameIdentifier` is a **UserId**, not a PatientId or CenterId. Each operation resolves the current domain profile through its `UserId`, verifies the current User exists and is active, rechecks that its stored role still matches the endpoint role, and restricts reads/writes to that profile. A stale JWT role does not override the current stored role. Center endpoints also require the Center's status to be exactly `Active`, using ordinal, case-sensitive comparison. `active` and `ACTIVE` do not qualify.

Missing/foreign appointments return a generic 404. Response data contains appointment details and the linked entities' IDs and names; it does not include account credentials or unrelated contact/profile information.

## Endpoints

| Method | Route | Role | Request | Success |
|---|---|---|---|---|
| POST | `/api/Appointments` | Patient | `CreateAppointmentDto` | 201; appointment response and Location header |
| GET | `/api/Appointments` | Patient | None | 200; own appointment responses |
| GET | `/api/Appointments/{appointmentId}` | Patient | None | 200; own appointment response |
| PATCH | `/api/Appointments/{appointmentId}/cancel` | Patient | Optional `CancellationReasonDto` | 200; updated appointment |
| PATCH | `/api/Appointments/{appointmentId}/reschedule` | Patient | `RescheduleAppointmentDto` | 200; updated appointment |
| GET | `/api/CenterAppointments` | Center | None | 200; own Center's appointment responses |
| GET | `/api/CenterAppointments/{appointmentId}` | Center | None | 200; own Center's appointment response |
| PATCH | `/api/CenterAppointments/{appointmentId}/confirm` | Center | None | 200; updated appointment |
| PATCH | `/api/CenterAppointments/{appointmentId}/reject` | Center | Optional `RejectionReasonDto` | 200; updated appointment |
| PATCH | `/api/CenterAppointments/{appointmentId}/cancel` | Center | Optional `CancellationReasonDto` | 200; updated appointment |

Errors use 400 for invalid request data, 401 for missing/invalid authentication, 403 for a disallowed role or inactive/ineligible account, 404 for unavailable resources, and 409 for appointment conflicts, invalid transitions, or a concurrency conflict. Reload availability/appointment state after a conflict. Framework model-validation errors use validation-problem responses.

## Exact write contracts

All write DTOs reject unrecognized JSON members. In particular, clients cannot submit `PatientId`, `PriceAtBooking`, `EndDateTime`, or `Status` when creating an appointment. The `CenterId` selected for booking identifies the requested destination; it never establishes ownership of Center-management actions.

### CreateAppointmentDto

| Property | Requirement |
|---|---|
| `centerId` | Required positive integer |
| `serviceId` | Required positive integer |
| `therapistId` | Required positive integer |
| `startDateTime` | Required local Asia/Hebron ISO date/time without UTC suffix or offset |
| `patientNotes` | Optional nullable string; trimmed; maximum 1,000 characters |

```json
{
  "centerId": 2,
  "serviceId": 8,
  "therapistId": 5,
  "startDateTime": "2026-10-20T09:00:00",
  "patientNotes": "Please note my previous appointment."
}
```

The example date is illustrative: a real request must use a currently bookable slot within the current horizon.

### RescheduleAppointmentDto

Only `therapistId` (required positive integer) and `startDateTime` (required local ISO date/time) are accepted.

```json
{
  "therapistId": 6,
  "startDateTime": "2026-10-21T10:00:00"
}
```

### CancellationReasonDto / RejectionReasonDto

The respective optional nullable string is trimmed and limited to 500 characters. An empty request body or `{}` is allowed when no reason is supplied.

```json
{ "cancellationReason": "My plans changed." }
```

```json
{ "rejectionReason": "The requested appointment cannot be accepted." }
```

## Response contract

All item endpoints return the same `AppointmentResponseDto`. List endpoints return arrays of that DTO. Scheduling timestamps have no offset; audit timestamps use UTC.

```json
{
  "appointmentId": 17,
  "patient": { "patientId": 3, "fullName": "Patient name" },
  "center": { "centerId": 2, "nameArabic": "اسم المركز", "nameEnglish": "Center name" },
  "therapist": { "therapistId": 5, "fullName": "Therapist name" },
  "service": { "serviceId": 8, "nameArabic": "اسم الخدمة", "nameEnglish": "Service name" },
  "startDateTime": "2026-10-20T09:00:00",
  "endDateTime": "2026-10-20T09:50:00",
  "reservedUntil": "2026-10-20T10:00:00",
  "priceAtBooking": 120.00,
  "patientNotes": "Please note my previous appointment.",
  "status": "Pending",
  "rejectionReason": null,
  "cancellationReason": null,
  "createdAt": "2026-10-18T10:00:00Z",
  "updatedAt": null,
  "timeZone": "Asia/Hebron"
}
```

English entity names, notes, reasons, and `updatedAt` may be null. `PriceAtBooking` is read from the Service at creation and is an immutable price snapshot for the actions in this feature. Later Service price changes and rescheduling do not update it.

## Booking validation and time rules

Creation resolves PatientId from the authenticated User and creates a `Pending` appointment. The backend calculates `EndDateTime = StartDateTime + 50 minutes`; the 10-minute break gives a normal occupied interval `[StartDateTime, StartDateTime + 60 minutes)`.

The selected Center must be exact `Active`. The Therapist must belong to that Center and be both active and available. The Service must belong to that Center and be active, and a `TherapistService` assignment must link the selected Therapist and Service.

Creation and rescheduling reuse the existing Availability/Schedules calculation. The requested start must exactly match a generated slot: starts occur every 60 minutes, anchored to each available weekly schedule window. Multiple windows are supported. The complete 60-minute occupied block must fit both its Therapist window and the Center's nonclosed working hours. `Service.DurationMinutes` does not control duration.

Scheduling uses **Asia/Hebron** local Palestine time. `Appointment.StartDateTime` and `EndDateTime`, weekly schedules, and Center hours are local wall-clock values, not UTC. Request scheduling timestamps must omit both `Z` and numeric offsets. `CreatedAt`, `UpdatedAt`, and history `ChangedAt` use UTC.

The booking horizon is local **today through today.AddMonths(1), inclusive**. This is one calendar month with normal month-end clamping, not a fixed number of days. Same-day future starts are permitted; past starts are rejected.

Existing DST protections remain in force: no actually occupied ambiguous/invalid local time is allowed. The occupied endpoint is exclusive. For the Asia/Hebron October 24, 2026 transition, a treatment from `00:00` to `00:50` with `reservedUntil = 01:00` occupies `[00:00, 01:00)` and remains valid even if the exclusive endpoint is ambiguous.

## Conflicts and atomic writes

`Pending` and `Confirmed` block; `Rejected`, `Cancelled`, and `Completed` do not. Blocking-status checks retain the existing case-insensitive convention for legacy stored values. New statuses are written with the canonical casing shown here.

Conflicts apply across **all services for the same Therapist**, and across **all Centers, Therapists, and Services for the same Patient**. Both use half-open occupied intervals: `existingStart < requestedReservedUntil && requestedStart < existingOccupiedEnd`. Touching interval endpoints are allowed. Existing legacy appointments reserve through `max(EndDateTime, StartDateTime + 50 minutes) + 10 minutes`, consistent with SchedulingRules.

An availability response is not a reservation. Appointment writes re-read current eligibility, schedules, working hours, and conflicts inside a serializable transaction immediately before saving. Creation writes the appointment and its initial history atomically.

`SchedulingWriteCoordinator` shares the existing process-wide write gate between appointment writes and schedule edits. On SQL Server, it acquires transaction-owned exclusive `sp_getapplock` resources. Therapist resources retain the existing name `PhysioGuide.TherapistSchedules.{therapistId}` so booking and schedule edits coordinate with each other. Patient resources use `PhysioGuide.PatientAppointments.{patientId}` so overlapping bookings with different Therapists also serialize. Multiple resources are acquired in a deterministic order, including old/new Therapists for rescheduling.

The transaction keeps validation, appointment/history changes, and commit together. Lock failures or detected concurrency failures return a conflict rather than silently accepting an overlapping booking. Existing schedule protection against invalidating blocking appointments remains in place.

## Status transitions and history

| Trigger | Allowed transition | Conditions |
|---|---|---|
| Patient creation | No prior status -> `Pending` | Booking validation passes |
| Patient cancel | `Pending` -> `Cancelled` | Own appointment; start has not occurred |
| Center confirm | `Pending` -> `Confirmed` | Own Center's appointment |
| Center reject | `Pending` -> `Rejected` | Own Center's appointment |
| Center cancel | `Pending`/`Confirmed` -> `Cancelled` | Own Center's appointment |
| Request-driven completion | `Confirmed` -> `Completed` | Treatment `EndDateTime` is strictly earlier than current local scheduling time |

Every actual transition writes `AppointmentStatusHistory`, with `OldStatus`, `NewStatus`, and UTC `ChangedAt`. Initial creation has a null old status. Authenticated actions record their UserId; automatic completion uses null `ChangedByUserId`. Later transitions update Appointment `UpdatedAt` in UTC. Appointments are retained rather than deleted.

Completion runs during relevant appointment operations/list/detail processing and is scoped to the current Patient or Center. Creation also processes the current Patient's expired Confirmed appointments. Completion commits before the requested target mutation, so attempting to cancel an expired Confirmed appointment cannot override its automatic completion. There is no background worker or new completion endpoint. Completed is historical and does not block availability.

Center actions have no additional time cutoff beyond the approved status rules and completion processing. Confirming an already-ended Pending appointment records `Pending -> Confirmed` followed by `Confirmed -> Completed` in the same transaction and returns Completed; it does not leave a newly confirmed expired appointment waiting for a subsequent request.

## Rescheduling

Only the owning Patient can reschedule a future `Pending` appointment. It keeps its AppointmentId, PatientId, CenterId, ServiceId, status, and original PriceAtBooking. The Patient may select another eligible Therapist in the same Center and a new slot for the same Service.

The new slot passes all current eligibility, service-assignment, schedule/hour, horizon, DST, Therapist-conflict, and Patient-conflict checks atomically. Both conflict checks exclude the appointment being moved, so keeping its own current slot does not conflict with itself. `UpdatedAt` changes in UTC; no status-history row is added solely for a reschedule because the status stays Pending.

## Known limitations and verification

There is no slot/hold table, reservation expiry, persisted idempotency key, automatic retry contract, background completion worker, notification dispatch, or new database constraint. A duplicate request after a successful booking is subject to normal conflict validation. All participating scheduling/booking writers must use the shared coordinator; direct database writes do not participate in its application-lock protocol.

The cross-instance application-lock protocol is SQL Server specific. The existing test style uses isolated SQLite databases with real JWTs and the shared process gate; it does not itself demonstrate SQL Server `sp_getapplock` execution. SQL Server integration verification remains a separate environment-dependent check.

Run the backend build, dedicated Appointments suite, and inherited Availability/Schedules suite before committing. Tests do not require applying migrations to the configured project database. No migration or database update is part of this feature.

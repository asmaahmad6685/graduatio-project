# Availability and weekly therapist schedules

This feature uses the existing TherapistSchedule, CenterWorkingHour, Therapist,
Center, Service, TherapistService, and Appointment models. It introduces no
model changes, slot table, migration, or database update.

## Authorization and endpoints

| Method | Route | Role | Success |
|---|---|---|---|
| GET | `/api/Schedules/therapists/{therapistId}` | Center | 200, weekly ScheduleWindowDto array |
| POST | `/api/Schedules/therapists/{therapistId}` | Center | 201, created ScheduleWindowDto |
| PUT | `/api/Schedules/therapists/{therapistId}/{scheduleId}` | Center | 200, updated ScheduleWindowDto |
| DELETE | `/api/Schedules/therapists/{therapistId}/{scheduleId}` | Center | 204 |
| GET | `/api/Availability/therapists/{therapistId}/slots?serviceId={serviceId}&date={yyyy-MM-dd}` | Patient | 200, AvailabilityResponseDto |

All endpoints require a valid JWT. Therapist-role callers cannot manage their
own schedules through these APIs. Routes follow the existing `api/[controller]`
convention and do not modify existing Therapist or Center controllers.

For management, JWT NameIdentifier is **UserId**, not CenterId. The service finds
the Center through `Center.UserId`, then checks the therapist's CenterId. The
request never supplies CenterId. A foreign/missing therapist or schedule returns
404. Missing Center profiles return 404; malformed/missing positive UserId claims
return 401. Availability reads require the Patient role and a valid positive
UserId claim; no PatientId is needed to read therapist/service slot data.

## DTO contract

### ScheduleWindowRequestDto (POST and PUT)

```json
{
  "dayOfWeek": 0,
  "startTime": "09:00:00",
  "endTime": "12:00:00",
  "isAvailable": true
}
```

DayOfWeek is the existing .NET numeric convention: Sunday 0 through Saturday 6.
DayOfWeek, StartTime, and EndTime are required. IsAvailable defaults to true.
Unknown JSON properties and malformed values are rejected. PUT replaces all
fields of the window; clients should include the intended IsAvailable value.

### ScheduleWindowDto

```json
{
  "scheduleId": 15,
  "therapistId": 301,
  "dayOfWeek": 0,
  "startTime": "09:00:00",
  "endTime": "12:00:00",
  "isAvailable": true
}
```

GET weekly schedule returns an array ordered by weekday, start time, and ID.
The IDs in these examples are illustrative; no development data is seeded.

### AvailabilityQueryDto and AvailabilityResponseDto

Example request:

`GET /api/Availability/therapists/301/slots?serviceId=601&date=2026-10-18`

Both query fields are required; ServiceId must be positive. Example response,
assuming a local current date of 2026-10-17 and the above Sunday schedule:

```json
{
  "therapistId": 301,
  "serviceId": 601,
  "date": "2026-10-18",
  "timeZone": "Asia/Hebron",
  "sessionMinutes": 50,
  "breakMinutes": 10,
  "bookableThrough": "2026-11-17",
  "slots": [
    {
      "startDateTime": "2026-10-18T09:00:00",
      "endDateTime": "2026-10-18T09:50:00",
      "reservedUntil": "2026-10-18T10:00:00"
    },
    {
      "startDateTime": "2026-10-18T10:00:00",
      "endDateTime": "2026-10-18T10:50:00",
      "reservedUntil": "2026-10-18T11:00:00"
    },
    {
      "startDateTime": "2026-10-18T11:00:00",
      "endDateTime": "2026-10-18T11:50:00",
      "reservedUntil": "2026-10-18T12:00:00"
    }
  ]
}
```

AvailabilitySlotDto contains local StartDateTime, treatment EndDateTime, and
ReservedUntil including the break. Values intentionally have no UTC suffix.

## Schedule validation and existing appointments

- Multiple windows per therapist/day are allowed. Intervals are half-open:
  adjacent windows are valid, overlapping/duplicate windows are rejected.
  This also applies to rows whose IsAvailable is false.
- StartTime must be earlier than EndTime. Overnight windows are unsupported.
- The Center must have exactly one valid, nonclosed working-hours row for that
  weekday, with nonnull OpeningTime/ClosingTime and OpeningTime < ClosingTime.
- The entire new/updated window must fit inside those hours.
- Updates/deletes examine all Pending/Confirmed appointments on the affected
  old/new weekdays. There is no horizon cutoff for protecting existing bookings,
  including historical rows that still have a blocking status.
- Each blocking appointment's full occupied interval must remain contained in
  one available resulting schedule window. Its hourly grid position need not
  match a newly moved window's starting point; containment protects existing
  appointments without changing their recorded times.
- Occupied end is `max(EndDateTime, StartDateTime + 50 minutes) + 10 minutes`.
  This preserves longer legacy appointments and never reserves less than the
  required treatment/break period. Overflowing legacy ends are treated
  conservatively as occupied through DateTime.MaxValue.
- Rejected and Cancelled appointments do not prevent changes. No appointment
  is cancelled, edited, or created by this feature.

Management validation and writes run together in a serializable transaction.
A process semaphore serializes writes, and SQL Server additionally takes a
transaction-owned `sp_getapplock` keyed by TherapistId. Competing schedule edits
therefore cannot both pass an overlap check before inserting conflicting rows.
The SQLite test provider uses the process lock and serializable transactions.

## Availability calculation

1. Capture the current Asia/Hebron time once. Validate the requested date.
2. Require Therapist and Service to exist; otherwise return 404.
3. Return an empty Slots array unless Therapist.IsActive and IsAvailable are
   true, Center.Status is Active, Service.IsActive is true, its Center matches
   the therapist, and a matching TherapistService assignment exists.
4. Require valid, open CenterWorkingHours for the requested weekday; otherwise
   return no slots. Read available weekly therapist windows for that day.
5. Starting at each window's StartTime, generate starts every 60 minutes. Each
   full 60-minute occupied period must fit both the schedule window and center
   hours. Treatment ends after 50 minutes; the next 10 minutes are reserved.
   Service.DurationMinutes is not used. A short trailing interval is omitted.
6. Exclude starts earlier than the captured current local time. A start exactly
   equal to the captured time is allowed.
7. Read blocking appointments for this therapist across **all services**,
   including previous-day appointments whose treatment/break reaches this day.
   Pending and Confirmed are matched case-insensitively. Reject any candidate
   whose full occupied interval overlaps an existing occupied interval.
8. Exclude blocks containing local DST-invalid/ambiguous occupied time or an
   offset transition within `[startDateTime, reservedUntil)`. ReservedUntil is
   exclusive: a valid block ending exactly at a transition boundary remains
   available even if that endpoint is ambiguous or invalid. For October 24,
   2026 in Asia/Hebron, 00:00-00:50 treatment with a break until 01:00 is valid.
   The existing local-only appointment model cannot disambiguate repeated
   wall-clock time actually inside a block.
9. Sort, deduplicate, and conservatively remove mutually overlapping candidates
   from any malformed/overlapping legacy schedule windows.

No new appointment statuses are defined. Only Pending and Confirmed block;
Rejected and Cancelled do not. Center matching requires exact ordinal,
case-sensitive `"Active"`; `"active"`, `"ACTIVE"`, Pending, Rejected, Suspended,
and null/unknown values do not expose slots. No
Center status or Admin approval workflow is changed by this implementation.

Availability responses are observations, not reservations. Without a booking
write, two clients can read the same free slot. The future Appointment API must
revalidate eligibility, schedule, and conflicts atomically when inserting an
appointment, and coordinate with the same therapist lock/transaction strategy.
No booking endpoint or hold/reservation system is introduced here.

## Local scheduling clock and horizon

SchedulingClock is the single source of current scheduling time. It converts
TimeProvider.GetUtcNow() into Asia/Hebron. Weekly times and existing Appointment
StartDateTime/EndDateTime are interpreted as local Palestine wall-clock values,
as confirmed by the project owner. They are not silently converted as if UTC.
Existing UTC audit timestamp conventions remain unchanged.

The allowed date range is **today through today.AddMonths(1), inclusive**, using
the Asia/Hebron calendar date. This is one calendar month, not a fixed 30 days.
Month ends clamp to the destination month's last day: January 31 → February
28 (or 29 in a leap year). Past dates and dates after this inclusive endpoint
return 400. Same-day past starts are removed from otherwise valid responses.

## Status codes

| Status | Meaning |
|---|---|
| 200 | Schedule/availability read or schedule update; ineligible availability returns empty Slots |
| 201 | Schedule window created |
| 204 | Schedule window deleted |
| 400 | Invalid/missing fields, invalid time bounds/day, outside/missing/closed center hours, invalid date range |
| 401 | Missing/invalid authentication or malformed UserId identity |
| 403 | Wrong role |
| 404 | Missing Center/Therapist/Service/window, or foreign-owned management resource |
| 409 | Overlap, existing booking would be invalidated, or concurrent/related-data write conflict |

Validation responses use ValidationProblemDetails; other errors use the
existing `{ "message": "..." }` style.

## Shared files and dependencies

Only Program.cs is modified among existing files, adding registrations for
TimeProvider, SchedulingClock, ScheduleService, and AvailabilityService. Its
existing encoding and unrelated content are preserved. ApplicationDbContext,
models, migrations, snapshot, project/solution files, authentication, Therapist
profile/account/management code, and Center implementation are unchanged.

This feature requires existing CenterWorkingHour data and TherapistService
assignments. It does not introduce their management APIs or seed development
data. Empty/missing/closed hours correctly prevent schedule creation and slots.
Therapist account/profile lifecycle and flags remain teammate-owned dependencies.
There are no holiday/leave/dated-exception, appointment creation, Reviews,
Notifications, Admin, or messaging implementations in this feature.

## Build and automated tests

```powershell
dotnet build "graduatio project/graduatio project.csproj" --no-restore --nologo
dotnet test "tests/PhysioGuide.Availability.Tests/PhysioGuide.Availability.Tests.csproj" --no-restore --verbosity minimal
```

The new test project uses actual JWT middleware and isolated in-memory SQLite,
with a replaceable TimeProvider. It never opens, seeds, migrates, or updates the
configured development database. SQL Server's application-lock path requires
separate SQL Server integration verification; the automated provider is SQLite.

Results after the exact-status and exclusive-DST-end fixes: backend build
succeeded with 0 errors and one NU1900 warning. The full API suite passed
121/121, and the focused Center-status/DST rerun passed 11/11, with no failures
or skips. NU1900 reports that the optional NuGet vulnerability feed was
unreachable; dependency restoration had succeeded.
No development database was accessed and no migration command was run.

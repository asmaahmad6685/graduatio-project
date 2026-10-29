# Pain Assessment API

Implemented on `feature/assessment`. This feature uses the existing Assessment,
AssessmentQuestion, AssessmentOption, and AssessmentAnswer entities and tables.
No Therapist or Center implementation was changed.

## Endpoints and identity

Every endpoint requires a valid Bearer JWT with role `Patient` and a linked
Patient profile. `ClaimTypes.NameIdentifier` is parsed as **UserId**; the service
queries `Patients.UserId` and uses the resulting **PatientId**. Clients do not
submit PatientId or database question/option IDs.

| Method | Route | Success |
|---|---|---|
| GET | `/api/Assessment/definition` | 200, five bilingual question definitions |
| POST | `/api/Assessment` | 201, result and Location header pointing to details |
| GET | `/api/Assessment/history` | 200, own result array, newest first; empty array if none |
| GET | `/api/Assessment/{assessmentId:int}` | 200, own result/details |

Ownership is filtered in the database query for both history and details.
Missing and other-patient assessment IDs both return 404.

## Submission

```json
{
  "painLocation": "LowerBack",
  "painSeverity": 5,
  "duration": "OneToFourWeeks",
  "painDescriptions": ["Aching", "Stiffness", "Other"],
  "previousInjurySurgery": "None",
  "otherDescription": "  Discomfort after sitting  "
}
```

The five required answer fields must be present. Codes are case-sensitive:

| Field | Approved values |
|---|---|
| painLocation | Neck, Shoulder, UpperBack, LowerBack, ArmHand, Hip, Knee, LegFoot, MultipleAreas |
| painSeverity | JSON integer 0 through 10; numeric strings and fractions rejected |
| duration | Today, TwoToSevenDays, OneToFourWeeks, OneToThreeMonths, MoreThanThreeMonths |
| painDescriptions | At least one of Aching, Stiffness, Sharp, Burning, NumbnessTingling, PainWithMovement, Other; no duplicates |
| previousInjurySurgery | None, PreviousInjury, PreviousSurgery, RecentInjury |

OtherDescription is optional even when Other is selected. Supplied text requires
Other, is trimmed, and is limited to 500 characters after trimming. Blank text
with Other becomes null. Description selections are returned in catalog order.
Unknown JSON fields, unknown codes, wrong types, null/missing required answers,
and malformed JSON return 400 with validation details. Single-selection fields
accept one string, not an array.

## Response shapes

Example POST/details response (illustrative ID/time):

```json
{
  "assessmentId": 42,
  "createdAt": "2026-10-12T15:00:00Z",
  "isPainAssessment": true,
  "answers": {
    "painLocation": "LowerBack",
    "painSeverity": 5,
    "duration": "OneToFourWeeks",
    "painDescriptions": ["Aching", "Stiffness", "Other"],
    "previousInjurySurgery": "None",
    "otherDescription": "Discomfort after sitting"
  },
  "severityInterpretation": "Moderate",
  "suggestedCare": {
    "code": "SpinalPhysiotherapy",
    "nameEnglish": "Neck and back physiotherapy assessment",
    "nameArabic": "تقييم العلاج الطبيعي للرقبة والظهر",
    "explanation": "Location: Lower back. Severity: 5/10 (Moderate). Duration: One to four weeks. Descriptions: aching, stiffness, other. Previous injury / surgery: None. The reported location guides the suggested physiotherapy care category.",
    "followUp": "Discuss an appropriate care plan with a qualified physiotherapist if symptoms persist or affect daily activities. Discuss the additional symptom description during that evaluation."
  },
  "suggestedSpecialtyId": null,
  "suggestedSpecialtyName": null,
  "disclaimer": "This assessment is for guidance only and does not provide a medical diagnosis or replace evaluation by a qualified healthcare professional."
}
```

History is an array of that same result shape. A legacy/inconsistent assessment
remains readable as metadata with `isPainAssessment: false` and null
`answers`, `severityInterpretation`, and `suggestedCare`; no pain answers or care
recommendation are invented. Existing specialty metadata is retained.

Definition is `{ "questions": [...], "disclaimer": "..." }`. Each question has
`code`, `textEnglish`, `textArabic`, `inputType`, `displayOrder`, `options`,
`minValue`, and `maxValue`. Each option has `code`, `textEnglish`, `textArabic`.
The question codes are PainLocation, PainSeverity, Duration, PainDescription,
PreviousInjurySurgery. Severity uses inputType `integer`, range 0–10 and zero
options. Other questions use `singleSelect` or `multiSelect`. Exactly 25 fixed
options exist: 9 location, 5 duration, 7 descriptions, and 4 injury/surgery.

Important status codes: 400 invalid input, 401 missing/invalid JWT or malformed
UserId claim, 403 non-Patient role, 404 missing Patient profile or unavailable
assessment, 409 conflicting existing approved question/option definitions.

## Deterministic Suggested Care rules

The first matching rule wins:

1. Severity 7–10, RecentInjury, or NumbnessTingling → ProfessionalReview:
   healthcare professional review before selecting physiotherapy.
2. Severity 0 → GeneralPhysiotherapyGuidance.
3. PreviousSurgery → PostSurgicalRehabilitation assessment.
4. MoreThanThreeMonths → PersistentPainPhysiotherapy assessment.
5. Otherwise use location: Neck/UpperBack/LowerBack → SpinalPhysiotherapy;
   Shoulder/ArmHand → UpperLimbPhysiotherapy; Hip/Knee/LegFoot →
   LowerLimbPhysiotherapy; MultipleAreas → GeneralPhysiotherapy.

Severity interpretation is NoPain (0), Mild (1–3), Moderate (4–6), Severe (7–10).
Explanations include all five answers. Regional follow-up mentions persistence
for OneToThreeMonths, previous injury when selected, and additional symptoms
when Other is selected. Free text is stored, not interpreted or diagnosed.
Care is calculated from the stored answers on retrieval; no care-text column,
AI/LLM integration, Center search, or Therapist recommendations are introduced.

These are deterministic product guidance categories, not a validated clinical
triage system. The five questions cannot establish urgency or exclude a health
problem. Professional-evaluation wording was informed by general
[NHS back-pain guidance](https://www.nhs.uk/conditions/back-pain/) and
[NHS injury guidance](https://www.nhs.uk/conditions/sprains-and-strains/);
the application's exact rule precedence is defined above.

## Specialty catalog

The existing SuggestedSpecialtyId relationship is set only for one unambiguous,
active specialty matching an explicitly recognized English/Arabic name
(trimmed, case-insensitive). Missing, inactive, or ambiguous matches leave it
null while Suggested Care remains available. Specialty records are never
created or changed by Assessment.

The configured development Specialty catalog was empty on inspection. The six
optional mapping groups therefore have no matching entries:

| Category | Recognized catalog names |
|---|---|
| PostSurgicalRehabilitation | Post-Surgical Rehabilitation; Postoperative Rehabilitation; التأهيل بعد الجراحة |
| PersistentPainPhysiotherapy | Persistent Pain Physiotherapy; Pain Management Physiotherapy; العلاج الطبيعي للألم المستمر |
| SpinalPhysiotherapy | Spinal Physiotherapy; Neck and Back Physiotherapy; العلاج الطبيعي للرقبة والظهر |
| UpperLimbPhysiotherapy | Upper Limb Physiotherapy; العلاج الطبيعي للطرف العلوي |
| LowerLimbPhysiotherapy | Lower Limb Physiotherapy; العلاج الطبيعي للطرف السفلي |
| GeneralPhysiotherapy | Musculoskeletal Physiotherapy; Orthopedic Physiotherapy; العلاج الطبيعي العضلي الهيكلي |

ProfessionalReview and GeneralPhysiotherapyGuidance intentionally have no
specialty mapping. A populated Specialty catalog is optional, not required for
the four Assessment APIs to return guidance.

## Persistence, migration, and initialization

Migration `20261012144759_AddAssessmentAnswerDetails` has exactly two Up
operations on AssessmentAnswers:

- `NumericValue int NULL` stores severity on its own question's answer with
  OptionId null. NumericValue is null for option-based answers.
- `OtherDescription nvarchar(500) NULL` stores text only on the Other description
  answer. Each selected pain description has a separate AssessmentAnswer row.

All existing tables, columns, relationships, and old migrations are preserved.
The migration's Down reverses only these two additions; it was not executed.

The migration **was applied** to the inspected local development database
`(localdb)\MSSQLLocalDB` / `PhysioGuideDB`. Only InitialCreate had previously been
applied. To avoid applying teammate changes, the EF-generated script between
the Oct7 migration and the new Assessment migration was inspected and executed
alone inside a transaction, with target, history, and original answer-schema
preflight checks. Answer row counts were preserved.

The database history now contains InitialCreate and AddAssessmentAnswerDetails.
**AddCenterCoordinates and AddTherapistAccountAndMessagingChanges remain
pending.** This history gap is intentional. Ordinary EF `database update` or
rollback commands can execute those pending migrations; coordinate them with
the teammate before running such commands. The script used is included in
`Assessment-only-migration.sql` for review; it is not a generic bootstrap script.

Initialization runs lazily on definition loading and submission. It uses the
fixed bilingual catalog, a process semaphore, a serializable transaction, and a
transaction-owned SQL Server `sp_getapplock` across application instances.
Existing compatible records are reused; only missing approved definitions are
inserted. Duplicate/conflicting matching records produce 409 instead of being
edited or removed. Unrelated definitions and existing option SpecialtyId/Score
are preserved. The API returns static codes independent of generated IDs.

The actual development initializer was run twice successfully: **5 questions,
25 options, unchanged identities** both times. No unrelated seed records,
patients, users, or specialties were created.

## Changed files and verification

New backend files: Controllers/AssessmentController.cs;
DTOs/Assessment/AssessmentSubmissionDto.cs, AssessmentDefinitionDto.cs,
AssessmentResultDto.cs; Services/AssessmentCatalog.cs, AssessmentCareRules.cs,
AssessmentDefinitionInitializer.cs, AssessmentService.cs; the new migration and
its Designer.cs companion.

Existing modified files: Models/AssessmentAnswer.cs; Data/ApplicationDbContext.cs
(only 500-character OtherDescription mapping); Program.cs (only scoped
AssessmentService registration); Migrations/ApplicationDbContextModelSnapshot.cs
(only the two properties). These are all shared-file changes. No configuration,
existing API, existing migration, Therapist/Center model, or relationship was
modified.

New tests are under tests/PhysioGuide.Assessment.Tests; the original solution
and application project files are unchanged.

```powershell
dotnet build "graduatio project/graduatio project.csproj" --no-restore
dotnet test "tests/PhysioGuide.Assessment.Tests/PhysioGuide.Assessment.Tests.csproj" --no-restore --verbosity minimal
```

Verification: build 0 errors/0 warnings; 75 automated tests passed, none skipped;
EF reports no pending model changes. API tests exercise real JWT middleware
against isolated in-memory SQLite, including all requested validation,
authorization, ownership, seeding, specialty, and legacy cases. SQL Server
migration application and repeat initialization were verified separately on
the configured development database. Full SQL Server API CRUD was not tested;
automated API persistence tests use SQLite.

No commit, push, merge, branch switch, or next feature was performed.

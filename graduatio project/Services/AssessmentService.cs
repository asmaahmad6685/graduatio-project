using System.ComponentModel.DataAnnotations;
using graduatio_project.Data;
using graduatio_project.DTOs.Assessment;
using graduatio_project.Models;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Services
{
    public sealed class AssessmentService
    {
        private readonly ApplicationDbContext _context;
        private readonly AssessmentDefinitionInitializer _initializer;

        public AssessmentService(ApplicationDbContext context)
        {
            _context = context;
            _initializer = new AssessmentDefinitionInitializer(context);
        }

        public Task<int?> ResolvePatientIdAsync(int userId, CancellationToken cancellationToken = default)
        {
            return _context.Patients.AsNoTracking().Where(patient => patient.UserId == userId)
                .Select(patient => (int?)patient.PatientId).SingleOrDefaultAsync(cancellationToken);
        }

        public async Task<AssessmentDefinitionDto> GetDefinitionAsync(CancellationToken cancellationToken = default)
        {
            await _initializer.EnsureInitializedAsync(cancellationToken);
            return new AssessmentDefinitionDto(AssessmentCatalog.Questions, AssessmentCatalog.Disclaimer);
        }

        public async Task<AssessmentResultDto> SubmitAsync(int patientId, AssessmentSubmissionDto submission,
            CancellationToken cancellationToken = default)
        {
            var answers = Normalize(submission);
            Validate(answers);
            var definition = await _initializer.EnsureInitializedAsync(cancellationToken);
            var care = AssessmentCareRules.Calculate(answers);
            var specialty = await FindExistingSpecialtyAsync(care.Code, cancellationToken);
            var assessment = new Assessment
            {
                PatientId = patientId,
                SuggestedSpecialtyId = specialty?.SpecialtyId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Assessments.Add(assessment);
            AddOptionAnswer(assessment, definition, "PainLocation", answers.PainLocation!);
            _context.AssessmentAnswers.Add(new AssessmentAnswer
            {
                Assessment = assessment,
                QuestionId = definition.Questions["PainSeverity"],
                NumericValue = answers.PainSeverity
            });
            AddOptionAnswer(assessment, definition, "Duration", answers.Duration!);
            foreach (var description in answers.PainDescriptions!)
            {
                AddOptionAnswer(assessment, definition, "PainDescription", description,
                    description == "Other" ? answers.OtherDescription : null);
            }
            AddOptionAnswer(assessment, definition, "PreviousInjurySurgery", answers.PreviousInjurySurgery!);

            // One SaveChanges atomically inserts the assessment and all answer records.
            await _context.SaveChangesAsync(cancellationToken);
            return CreateResult(assessment, answers, specialty);
        }

        public async Task<IReadOnlyList<AssessmentResultDto>> GetHistoryAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            var assessments = await _context.Assessments.AsNoTracking()
                .Where(assessment => assessment.PatientId == patientId)
                .Include(assessment => assessment.SuggestedSpecialty)
                .OrderByDescending(assessment => assessment.CreatedAt)
                .ThenByDescending(assessment => assessment.AssessmentId)
                .ToListAsync(cancellationToken);

            var answerRecords = await _context.AssessmentAnswers.AsNoTracking()
                .Where(answer => answer.Assessment.PatientId == patientId)
                .Include(answer => answer.Question).Include(answer => answer.Option)
                .ToListAsync(cancellationToken);
            var answersByAssessment = answerRecords.ToLookup(answer => answer.AssessmentId);

            return assessments.Select(assessment => CreateResult(assessment,
                TryReadPainAnswers(answersByAssessment[assessment.AssessmentId].ToList()),
                assessment.SuggestedSpecialty)).ToList();
        }

        public async Task<AssessmentResultDto?> GetDetailsAsync(int patientId, int assessmentId,
            CancellationToken cancellationToken = default)
        {
            var assessment = await _context.Assessments.AsNoTracking()
                .Where(assessment => assessment.PatientId == patientId && assessment.AssessmentId == assessmentId)
                .Include(assessment => assessment.SuggestedSpecialty)
                .SingleOrDefaultAsync(cancellationToken);
            if (assessment == null)
            {
                return null;
            }

            var answerRecords = await _context.AssessmentAnswers.AsNoTracking()
                .Where(answer => answer.AssessmentId == assessmentId && answer.Assessment.PatientId == patientId)
                .Include(answer => answer.Question).Include(answer => answer.Option)
                .ToListAsync(cancellationToken);
            return CreateResult(assessment, TryReadPainAnswers(answerRecords), assessment.SuggestedSpecialty);
        }

        private void AddOptionAnswer(Assessment assessment, AssessmentDefinitionIds definition,
            string questionCode, string optionCode, string? otherDescription = null)
        {
            _context.AssessmentAnswers.Add(new AssessmentAnswer
            {
                Assessment = assessment,
                QuestionId = definition.Questions[questionCode],
                OptionId = definition.Options[questionCode][optionCode],
                OtherDescription = otherDescription
            });
        }

        private async Task<Specialty?> FindExistingSpecialtyAsync(string careCode, CancellationToken cancellationToken)
        {
            var names = AssessmentCareRules.GetSpecialtyNames(careCode);
            if (names.Count == 0)
            {
                return null;
            }

            var specialties = await _context.Specialties.AsNoTracking().Where(specialty => specialty.IsActive)
                .ToListAsync(cancellationToken);
            var matches = specialties.Where(specialty => names.Any(name =>
                string.Equals(specialty.NameEnglish?.Trim(), name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(specialty.NameArabic.Trim(), name, StringComparison.OrdinalIgnoreCase))).ToList();

            // Preserve the existing catalog; an absent or ambiguous match remains a care category without a specialty FK.
            return matches.Count == 1 ? matches[0] : null;
        }

        private static AssessmentSubmissionDto Normalize(AssessmentSubmissionDto submission)
        {
            var descriptions = submission.PainDescriptions;
            if (descriptions != null && descriptions.All(code => AssessmentCatalog.IsValidOption("PainDescription", code)))
            {
                descriptions = descriptions.OrderBy(code => AssessmentCatalog.GetQuestion("PainDescription")
                    .Options.ToList().FindIndex(option => option.Code == code)).ToList();
            }

            var otherDescription = submission.OtherDescription?.Trim();
            if (otherDescription?.Length == 0 && descriptions?.Contains("Other", StringComparer.Ordinal) == true)
            {
                otherDescription = null;
            }
            return new AssessmentSubmissionDto
            {
                PainLocation = submission.PainLocation,
                PainSeverity = submission.PainSeverity,
                Duration = submission.Duration,
                PainDescriptions = descriptions,
                PreviousInjurySurgery = submission.PreviousInjurySurgery,
                OtherDescription = otherDescription
            };
        }

        private static void Validate(AssessmentSubmissionDto submission)
        {
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(submission, new ValidationContext(submission), results, true))
            {
                var errors = results.SelectMany(result => (result.MemberNames.Any()
                        ? result.MemberNames : new[] { string.Empty })
                    .Select(member => new { Member = member, Message = result.ErrorMessage ?? "Invalid assessment answer." }))
                    .GroupBy(error => error.Member).ToDictionary(group => group.Key,
                        group => group.Select(error => error.Message).Distinct().ToArray());
                throw new AssessmentSubmissionValidationException(errors);
            }
        }

        private static AssessmentSubmissionDto? TryReadPainAnswers(IReadOnlyList<AssessmentAnswer> records)
        {
            var byCode = new Dictionary<string, List<AssessmentAnswer>>(StringComparer.Ordinal);
            foreach (var answer in records)
            {
                var question = AssessmentCatalog.Questions.SingleOrDefault(definition =>
                    definition.TextEnglish == answer.Question.QuestionEnglish);
                if (question == null || answer.QuestionId != answer.Question.QuestionId)
                {
                    return null;
                }

                if (!byCode.TryGetValue(question.Code, out var questionAnswers))
                {
                    questionAnswers = new List<AssessmentAnswer>();
                    byCode[question.Code] = questionAnswers;
                }
                questionAnswers.Add(answer);
            }

            if (byCode.Count != AssessmentCatalog.Questions.Count
                || AssessmentCatalog.Questions.Any(question => !byCode.ContainsKey(question.Code))
                || byCode.Values.Any(answers => answers.Select(answer => answer.QuestionId).Distinct().Count() != 1))
            {
                return null;
            }

            string? ReadSingle(string code)
            {
                return byCode[code].Count == 1 ? ReadOption(code, byCode[code][0]) : null;
            }

            string? ReadOption(string code, AssessmentAnswer answer)
            {
                if (answer.OptionId == null || answer.Option == null
                    || answer.Option.OptionId != answer.OptionId || answer.Option.QuestionId != answer.QuestionId
                    || answer.NumericValue != null)
                {
                    return null;
                }

                var optionCode = AssessmentCatalog.GetQuestion(code).Options.SingleOrDefault(option =>
                    option.TextEnglish == answer.Option.TextEnglish)?.Code;
                if (optionCode == null || (answer.OtherDescription != null && (code != "PainDescription" || optionCode != "Other")))
                {
                    return null;
                }

                return optionCode;
            }

            var severityAnswers = byCode["PainSeverity"];
            if (severityAnswers.Count != 1 || severityAnswers[0].OptionId != null
                || severityAnswers[0].NumericValue == null || severityAnswers[0].OtherDescription != null)
            {
                return null;
            }

            var descriptionCodes = byCode["PainDescription"].Select(answer => ReadOption("PainDescription", answer)).ToList();
            if (descriptionCodes.Any(code => code == null))
            {
                return null;
            }

            var painAnswers = new AssessmentSubmissionDto
            {
                PainLocation = ReadSingle("PainLocation"),
                PainSeverity = severityAnswers[0].NumericValue,
                Duration = ReadSingle("Duration"),
                PainDescriptions = descriptionCodes.Select(code => code!).ToList(),
                PreviousInjurySurgery = ReadSingle("PreviousInjurySurgery"),
                OtherDescription = byCode["PainDescription"].FirstOrDefault(answer =>
                    ReadOption("PainDescription", answer) == "Other")?.OtherDescription
            };

            // Legacy or inconsistent rows remain readable as metadata, without inventing answers or recommendations.
            var validationResults = new List<ValidationResult>();
            return Validator.TryValidateObject(painAnswers, new ValidationContext(painAnswers), validationResults, true)
                ? Normalize(painAnswers) : null;
        }

        private static AssessmentResultDto CreateResult(Assessment assessment, AssessmentSubmissionDto? answers,
            Specialty? specialty)
        {
            return new AssessmentResultDto
            {
                AssessmentId = assessment.AssessmentId,
                CreatedAt = DateTime.SpecifyKind(assessment.CreatedAt, DateTimeKind.Utc),
                IsPainAssessment = answers != null,
                Answers = answers,
                SeverityInterpretation = answers?.PainSeverity is int severity
                    ? AssessmentCareRules.InterpretSeverity(severity) : null,
                SuggestedCare = answers == null ? null : AssessmentCareRules.Calculate(answers),
                SuggestedSpecialtyId = assessment.SuggestedSpecialtyId,
                SuggestedSpecialtyName = string.IsNullOrWhiteSpace(specialty?.NameEnglish)
                    ? specialty?.NameArabic : specialty.NameEnglish,
                Disclaimer = AssessmentCatalog.Disclaimer
            };
        }
    }

    public sealed class AssessmentSubmissionValidationException : Exception
    {
        public AssessmentSubmissionValidationException(IDictionary<string, string[]> errors)
            : base("The assessment contains invalid or missing answers.")
        {
            Errors = errors;
        }

        public IDictionary<string, string[]> Errors { get; }
    }
}

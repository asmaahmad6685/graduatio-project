using System.Data;
using graduatio_project.Data;
using graduatio_project.Models;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Services
{
    public sealed class AssessmentDefinitionInitializer
    {
        private static readonly SemaphoreSlim InitializationLock = new(1, 1);
        private readonly ApplicationDbContext _context;

        public AssessmentDefinitionInitializer(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<AssessmentDefinitionIds> EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            await InitializationLock.WaitAsync(cancellationToken);
            try
            {
                return await InitializeAsync(cancellationToken);
            }
            finally
            {
                InitializationLock.Release();
            }
        }

        private async Task<AssessmentDefinitionIds> InitializeAsync(CancellationToken cancellationToken)
        {
            // The transaction-owned application lock serializes initialization across SQL Server app instances.
            // No schema changes, unrelated seed data, or edits to existing definitions happen here.
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            if (_context.Database.IsSqlServer())
            {
                await _context.Database.ExecuteSqlRawAsync(
                    """
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock
                        @Resource = N'PhysioGuide.Assessment.ApprovedDefinition.v1',
                        @LockMode = N'Exclusive',
                        @LockOwner = N'Transaction',
                        @LockTimeout = 15000;
                    IF @result < 0
                        THROW 51000, 'Assessment definition initialization lock could not be acquired.', 1;
                    """, cancellationToken);
            }

            var existingQuestions = await _context.AssessmentQuestions.ToListAsync(cancellationToken);
            var approvedQuestions = new Dictionary<string, AssessmentQuestion>(StringComparer.Ordinal);
            foreach (var definition in AssessmentCatalog.Questions)
            {
                var matches = existingQuestions.Where(question => string.Equals(
                    question.QuestionEnglish.Trim(), definition.TextEnglish, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matches.Count > 1)
                {
                    throw new AssessmentDefinitionConflictException(
                        $"More than one existing question matches '{definition.Code}'. Resolve the existing definitions before initialization.");
                }

                var question = matches.SingleOrDefault();
                if (question != null && (!string.Equals(question.QuestionEnglish, definition.TextEnglish, StringComparison.Ordinal)
                    || !string.Equals(question.QuestionArabic, definition.TextArabic, StringComparison.Ordinal)
                    || question.DisplayOrder != definition.DisplayOrder || !question.IsActive))
                {
                    throw new AssessmentDefinitionConflictException(
                        $"The existing '{definition.Code}' question conflicts with the approved definition. No existing data was changed.");
                }

                approvedQuestions[definition.Code] = question ?? new AssessmentQuestion
                {
                    QuestionEnglish = definition.TextEnglish,
                    QuestionArabic = definition.TextArabic,
                    DisplayOrder = definition.DisplayOrder,
                    IsActive = true
                };
            }

            var existingQuestionIds = approvedQuestions.Values.Where(question => question.QuestionId != 0)
                .Select(question => question.QuestionId).ToList();
            var existingOptions = await _context.AssessmentOptions
                .Where(option => existingQuestionIds.Contains(option.QuestionId))
                .ToListAsync(cancellationToken);

            var approvedOptions = new Dictionary<string, Dictionary<string, AssessmentOption>>(StringComparer.Ordinal);
            foreach (var definition in AssessmentCatalog.Questions)
            {
                var question = approvedQuestions[definition.Code];
                var questionOptions = existingOptions.Where(option => option.QuestionId == question.QuestionId).ToList();
                var options = new Dictionary<string, AssessmentOption>(StringComparer.Ordinal);

                foreach (var existingOption in questionOptions)
                {
                    if (!definition.Options.Any(option => string.Equals(
                        existingOption.TextEnglish.Trim(), option.TextEnglish, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new AssessmentDefinitionConflictException(
                            $"The existing '{definition.Code}' question contains an unapproved option. No existing data was changed.");
                    }
                }

                foreach (var optionDefinition in definition.Options)
                {
                    var matches = questionOptions.Where(option => string.Equals(
                        option.TextEnglish.Trim(), optionDefinition.TextEnglish, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (matches.Count > 1)
                    {
                        throw new AssessmentDefinitionConflictException(
                            $"More than one existing option matches '{definition.Code}/{optionDefinition.Code}'. No existing data was changed.");
                    }

                    var option = matches.SingleOrDefault();
                    if (option != null && (!string.Equals(option.TextEnglish, optionDefinition.TextEnglish, StringComparison.Ordinal)
                        || !string.Equals(option.TextArabic, optionDefinition.TextArabic, StringComparison.Ordinal)))
                    {
                        throw new AssessmentDefinitionConflictException(
                            $"The existing '{definition.Code}/{optionDefinition.Code}' option conflicts with the approved definition. No existing data was changed.");
                    }

                    options[optionDefinition.Code] = option ?? new AssessmentOption
                    {
                        Question = question,
                        TextEnglish = optionDefinition.TextEnglish,
                        TextArabic = optionDefinition.TextArabic
                    };
                }

                approvedOptions[definition.Code] = options;
            }

            // Validate the entire existing definition before adding any records.
            foreach (var question in approvedQuestions.Values.Where(question => question.QuestionId == 0))
            {
                _context.AssessmentQuestions.Add(question);
            }

            foreach (var option in approvedOptions.Values.SelectMany(options => options.Values).Where(option => option.OptionId == 0))
            {
                _context.AssessmentOptions.Add(option);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new AssessmentDefinitionIds(
                approvedQuestions.ToDictionary(pair => pair.Key, pair => pair.Value.QuestionId, StringComparer.Ordinal),
                approvedOptions.ToDictionary(pair => pair.Key,
                    pair => (IReadOnlyDictionary<string, int>)pair.Value.ToDictionary(
                        option => option.Key, option => option.Value.OptionId, StringComparer.Ordinal), StringComparer.Ordinal));
        }
    }

    public sealed record AssessmentDefinitionIds(
        IReadOnlyDictionary<string, int> Questions,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Options);

    public sealed class AssessmentDefinitionConflictException : Exception
    {
        public AssessmentDefinitionConflictException(string message) : base(message)
        {
        }
    }
}

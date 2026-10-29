using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using graduatio_project.Data;
using graduatio_project.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PhysioGuide.Assessment.Tests;

public sealed class AssessmentApiTests : IAsyncLifetime
{
    private readonly AssessmentApiFactory _factory = new();

    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private static Dictionary<string, object?> ValidRequest() => new()
    {
        ["painLocation"] = "LowerBack",
        ["painSeverity"] = 5,
        ["duration"] = "OneToFourWeeks",
        ["painDescriptions"] = new[] { "Aching" },
        ["previousInjurySurgery"] = "None"
    };

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> SubmitAsync(HttpClient client, Dictionary<string, object?>? payload = null)
    {
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload ?? ValidRequest());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return await ReadJsonAsync(response);
    }

    [Fact]
    public async Task DefinitionContainsExactlyFiveQuestionsAndApprovedOptionsWithoutDuplicates()
    {
        using var client = _factory.CreateAuthenticatedClient();
        using var firstResponse = await client.GetAsync("/api/Assessment/definition");
        using var secondResponse = await client.GetAsync("/api/Assessment/definition");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var first = await ReadJsonAsync(firstResponse);
        var second = await ReadJsonAsync(secondResponse);
        Assert.Equal(first.GetRawText(), second.GetRawText());

        var expectedOptions = new Dictionary<string, string[]>
        {
            ["PainLocation"] = new[] { "Neck", "Shoulder", "UpperBack", "LowerBack", "ArmHand", "Hip", "Knee", "LegFoot", "MultipleAreas" },
            ["PainSeverity"] = Array.Empty<string>(),
            ["Duration"] = new[] { "Today", "TwoToSevenDays", "OneToFourWeeks", "OneToThreeMonths", "MoreThanThreeMonths" },
            ["PainDescription"] = new[] { "Aching", "Stiffness", "Sharp", "Burning", "NumbnessTingling", "PainWithMovement", "Other" },
            ["PreviousInjurySurgery"] = new[] { "None", "PreviousInjury", "PreviousSurgery", "RecentInjury" }
        };
        var questions = first.GetProperty("questions").EnumerateArray().ToArray();
        Assert.Equal(expectedOptions.Keys, questions.Select(q => q.GetProperty("code").GetString()));
        foreach (var question in questions)
        {
            var code = question.GetProperty("code").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(question.GetProperty("textEnglish").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(question.GetProperty("textArabic").GetString()));
            var options = question.GetProperty("options").EnumerateArray().ToArray();
            Assert.Equal(expectedOptions[code], options.Select(o => o.GetProperty("code").GetString()));
            foreach (var option in options)
            {
                Assert.False(string.IsNullOrWhiteSpace(option.GetProperty("textEnglish").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(option.GetProperty("textArabic").GetString()));
            }
        }
        Assert.Equal(0, questions[1].GetProperty("minValue").GetInt32());
        Assert.Equal(10, questions[1].GetProperty("maxValue").GetInt32());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(5, await db.AssessmentQuestions.CountAsync());
        Assert.Equal(25, await db.AssessmentOptions.CountAsync());
    }

    [Fact]
    public async Task ConcurrentDefinitionRequestsDoNotDuplicateInitialization()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => client.GetAsync("/api/Assessment/definition")));
        foreach (var response in responses)
        {
            using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(5, await db.AssessmentQuestions.CountAsync());
        Assert.Equal(25, await db.AssessmentOptions.CountAsync());
    }

    [Fact]
    public async Task DefinitionInitializationPreservesUnrelatedExistingQuestionsAndOptions()
    {
        int existingQuestionId;
        int existingOptionId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var question = new AssessmentQuestion
            {
                QuestionEnglish = "Existing unrelated assessment question",
                QuestionArabic = "سؤال تقييم سابق",
                DisplayOrder = 99,
                IsActive = true
            };
            var option = new AssessmentOption { Question = question, TextEnglish = "Existing option", TextArabic = "خيار سابق" };
            db.AssessmentOptions.Add(option);
            await db.SaveChangesAsync();
            existingQuestionId = question.QuestionId;
            existingOptionId = option.OptionId;
        }
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync("/api/Assessment/definition");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, (await ReadJsonAsync(response)).GetProperty("questions").GetArrayLength());
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("Existing unrelated assessment question", (await verificationDb.AssessmentQuestions
            .SingleAsync(q => q.QuestionId == existingQuestionId)).QuestionEnglish);
        Assert.Equal("Existing option", (await verificationDb.AssessmentOptions
            .SingleAsync(o => o.OptionId == existingOptionId)).TextEnglish);
        Assert.Equal(6, await verificationDb.AssessmentQuestions.CountAsync());
        Assert.Equal(26, await verificationDb.AssessmentOptions.CountAsync());
    }

    [Fact]
    public async Task ConflictingExistingDefinitionReturnsConflictWithoutChangingExistingData()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var question = new AssessmentQuestion
            {
                QuestionEnglish = graduatio_project.Services.AssessmentCatalog.GetQuestion("PainLocation").TextEnglish,
                QuestionArabic = "نص سابق مختلف",
                DisplayOrder = 99,
                IsActive = true
            };
            db.AssessmentQuestions.Add(question);
            await db.SaveChangesAsync();
        }
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync("/api/Assessment/definition");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = Assert.Single(await verificationDb.AssessmentQuestions.ToListAsync());
        Assert.Equal("نص سابق مختلف", existing.QuestionArabic);
        Assert.Equal(99, existing.DisplayOrder);
        Assert.Equal(0, await verificationDb.AssessmentOptions.CountAsync());
    }

    [Fact]
    public async Task ValidSubmissionStoresNumericSeverityAndResolvesPatientFromJwtUserId()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var result = await SubmitAsync(client);
        Assert.True(result.GetProperty("isPainAssessment").GetBoolean());
        Assert.Equal("Moderate", result.GetProperty("severityInterpretation").GetString());
        Assert.Equal("LowerBack", result.GetProperty("answers").GetProperty("painLocation").GetString());
        Assert.Contains("does not provide a medical diagnosis", result.GetProperty("disclaimer").GetString());
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("suggestedCare").GetProperty("nameEnglish").GetString()));

        var id = result.GetProperty("assessmentId").GetInt32();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var assessment = await db.Assessments.SingleAsync(a => a.AssessmentId == id);
        Assert.Equal(201, assessment.PatientId);
        var answers = await db.AssessmentAnswers.Where(a => a.AssessmentId == id).ToListAsync();
        Assert.Equal(5, answers.Count);
        var severity = Assert.Single(answers, a => a.NumericValue.HasValue);
        Assert.Equal(5, severity.NumericValue);
        Assert.Null(severity.OptionId);
        Assert.All(answers.Where(a => !a.NumericValue.HasValue), a => Assert.NotNull(a.OptionId));
        // An absent specialty catalog must not cause unrelated seed changes.
        Assert.Equal(0, await db.Specialties.CountAsync());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("suggestedSpecialtyId").ValueKind);
    }

    [Theory]
    [InlineData(0, "NoPain")]
    [InlineData(1, "Mild")]
    [InlineData(3, "Mild")]
    [InlineData(4, "Moderate")]
    [InlineData(6, "Moderate")]
    [InlineData(7, "Severe")]
    [InlineData(10, "Severe")]
    public async Task SeverityBoundsAndInterpretationAreAccepted(int severity, string interpretation)
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["painSeverity"] = severity;
        var result = await SubmitAsync(client, payload);
        Assert.Equal(severity, result.GetProperty("answers").GetProperty("painSeverity").GetInt32());
        Assert.Equal(interpretation, result.GetProperty("severityInterpretation").GetString());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task SeverityOutsideZeroToTenIsRejected(int severity)
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["painSeverity"] = severity;
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FractionalSeverityIsRejected()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["painSeverity"] = 2.5;
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SeverityEncodedAsAStringIsRejected()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["painSeverity"] = "5";
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MultiplePainDescriptionsPersistAsSeparateAnswersAndReturnInDetails()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["painDescriptions"] = new[] { "Aching", "Stiffness", "PainWithMovement" };
        var result = await SubmitAsync(client, payload);
        Assert.Equal(new[] { "Aching", "Stiffness", "PainWithMovement" },
            result.GetProperty("answers").GetProperty("painDescriptions").EnumerateArray().Select(x => x.GetString()));
        var id = result.GetProperty("assessmentId").GetInt32();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(7, await db.AssessmentAnswers.CountAsync(a => a.AssessmentId == id));
        using var response = await client.GetAsync($"/api/Assessment/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await ReadJsonAsync(response);
        Assert.Equal(result.GetProperty("answers").GetRawText(), details.GetProperty("answers").GetRawText());
    }

    [Fact]
    public async Task DuplicatePainDescriptionsAreRejectedWithoutSavingAssessment()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["painDescriptions"] = new[] { "Aching", "Aching" };
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Assessments.CountAsync());
    }

    [Theory]
    [InlineData("  Pain after activity  ", "Pain after activity")]
    [InlineData(null, null)]
    [InlineData("   ", "")]
    public async Task OtherSelectionAllowsOptionalTrimmedDescription(string? description, string? expected)
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["painDescriptions"] = new[] { "Aching", "Other" };
        payload["otherDescription"] = description;
        var result = await SubmitAsync(client, payload);
        var returned = result.GetProperty("answers").GetProperty("otherDescription");
        if (string.IsNullOrEmpty(expected))
            Assert.True(returned.ValueKind == JsonValueKind.Null || returned.GetString() == string.Empty);
        else
            Assert.Equal(expected, returned.GetString());
        var id = result.GetProperty("assessmentId").GetInt32();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var answers = await db.AssessmentAnswers.Where(a => a.AssessmentId == id).Include(a => a.Option).ToListAsync();
        var other = Assert.Single(answers, a => a.Option?.TextEnglish == "Other");
        if (!string.IsNullOrEmpty(expected)) Assert.Equal(expected, other.OtherDescription);
        Assert.All(answers.Where(a => a != other), a => Assert.Null(a.OtherDescription));
    }

    [Theory]
    [InlineData("Other pain")]
    [InlineData("   ")]
    [InlineData("")]
    public async Task OtherDescriptionWithoutOtherIsRejected(string description)
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["otherDescription"] = description;
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(500, HttpStatusCode.Created)]
    [InlineData(501, HttpStatusCode.BadRequest)]
    public async Task OtherDescriptionHasMaximumLengthAfterTrimming(int length, HttpStatusCode expectedStatus)
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["painDescriptions"] = new[] { "Other" };
        payload["otherDescription"] = "  " + new string('x', length) + "  ";
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload);
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData("painLocation")]
    [InlineData("painSeverity")]
    [InlineData("duration")]
    [InlineData("painDescriptions")]
    [InlineData("previousInjurySurgery")]
    public async Task EveryRequiredAnswerMustBePresent(string field)
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload.Remove(field);
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmptyPainDescriptionSelectionIsRejected()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["painDescriptions"] = Array.Empty<string>();
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("painLocation", "Head")]
    [InlineData("painLocation", "neck")]
    [InlineData("duration", "Forever")]
    [InlineData("previousInjurySurgery", "Unknown")]
    public async Task UnknownOrIncorrectlyCasedCodesAreRejected(string field, string value)
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload[field] = value;
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("aching")]
    public async Task UnknownPainDescriptionIsRejected(string value)
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["painDescriptions"] = new[] { value };
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownSubmissionPropertiesAreRejected()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var payload = ValidRequest();
        payload["patientId"] = 202;
        using var response = await client.PostAsJsonAsync("/api/Assessment", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HistoryAndDetailsIncludeOnlyAuthenticatedPatientsAssessments()
    {
        using var firstClient = _factory.CreateAuthenticatedClient("101");
        using var secondClient = _factory.CreateAuthenticatedClient("102");
        var own = await SubmitAsync(firstClient);
        var other = await SubmitAsync(secondClient);
        var ownId = own.GetProperty("assessmentId").GetInt32();
        var otherId = other.GetProperty("assessmentId").GetInt32();

        using var historyResponse = await firstClient.GetAsync("/api/Assessment/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await ReadJsonAsync(historyResponse);
        var entry = Assert.Single(history.EnumerateArray());
        Assert.Equal(ownId, entry.GetProperty("assessmentId").GetInt32());

        using var detailsResponse = await firstClient.GetAsync($"/api/Assessment/{ownId}");
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        Assert.Equal(ownId, (await ReadJsonAsync(detailsResponse)).GetProperty("assessmentId").GetInt32());
        using var otherResponse = await firstClient.GetAsync($"/api/Assessment/{otherId}");
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
        using var missingResponse = await firstClient.GetAsync("/api/Assessment/2147483647");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task LegacyAssessmentWithNoPainAnswersIsReadableWithoutInventingRecommendation()
    {
        int id;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var legacy = new graduatio_project.Models.Assessment { PatientId = 201 };
            db.Assessments.Add(legacy);
            await db.SaveChangesAsync();
            id = legacy.AssessmentId;
        }
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/Assessment/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadJsonAsync(response);
        Assert.False(result.GetProperty("isPainAssessment").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("answers").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("suggestedCare").ValueKind);
        using var history = await client.GetAsync("/api/Assessment/history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
    }

    [Fact]
    public async Task UniqueActiveMatchingSpecialtyUsesExistingForeignKey()
    {
        int specialtyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var specialty = new Specialty
            {
                NameEnglish = "Spinal Physiotherapy",
                NameArabic = "اختصاص موجود للاختبار",
                IsActive = true
            };
            db.Specialties.Add(specialty);
            await db.SaveChangesAsync();
            specialtyId = specialty.SpecialtyId;
        }
        using var client = _factory.CreateAuthenticatedClient();
        var result = await SubmitAsync(client);
        Assert.Equal(specialtyId, result.GetProperty("suggestedSpecialtyId").GetInt32());
        var id = result.GetProperty("assessmentId").GetInt32();
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(specialtyId, (await verificationDb.Assessments.SingleAsync(a => a.AssessmentId == id)).SuggestedSpecialtyId);
        Assert.Equal(1, await verificationDb.Specialties.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InactiveOrAmbiguousSpecialtyCatalogDoesNotAssignArbitraryForeignKey(bool ambiguous)
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Specialties.Add(new Specialty
            {
                NameEnglish = "Spinal Physiotherapy",
                NameArabic = "اختصاص اختبار أول",
                IsActive = ambiguous
            });
            if (ambiguous) db.Specialties.Add(new Specialty
            {
                NameEnglish = "Neck and Back Physiotherapy",
                NameArabic = "اختصاص اختبار ثاني",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
        using var client = _factory.CreateAuthenticatedClient();
        var result = await SubmitAsync(client);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("suggestedSpecialtyId").ValueKind);
        Assert.Equal("SpinalPhysiotherapy", result.GetProperty("suggestedCare").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("/api/Assessment/definition")]
    [InlineData("/api/Assessment/history")]
    [InlineData("/api/Assessment/1")]
    [InlineData("/api/Assessment")]
    public async Task UnauthenticatedRequestsAreRejected(string route)
    {
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using var response = route == "/api/Assessment"
            ? await client.PostAsJsonAsync(route, ValidRequest())
            : await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/Assessment/definition", "Center")]
    [InlineData("/api/Assessment/history", "Therapist")]
    [InlineData("/api/Assessment/1", "Admin")]
    [InlineData("/api/Assessment", "Therapist")]
    public async Task NonPatientRolesAreForbidden(string route, string role)
    {
        using var client = _factory.CreateAuthenticatedClient(role: role);
        using var response = route == "/api/Assessment"
            ? await client.PostAsJsonAsync(route, ValidRequest())
            : await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/Assessment/definition")]
    [InlineData("/api/Assessment/history")]
    [InlineData("/api/Assessment/1")]
    [InlineData("/api/Assessment")]
    public async Task PatientRoleWithoutPatientProfileReturnsNotFound(string route)
    {
        using var client = _factory.CreateAuthenticatedClient("103");
        using var response = route == "/api/Assessment"
            ? await client.PostAsJsonAsync(route, ValidRequest())
            : await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("not-an-integer")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task MalformedUserIdentityIsUnauthorized(string userId)
    {
        using var client = _factory.CreateAuthenticatedClient(userId);
        using var response = await client.GetAsync("/api/Assessment/history");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

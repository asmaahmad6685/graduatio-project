BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012144759_AddAssessmentAnswerDetails'
)
BEGIN
    ALTER TABLE [AssessmentAnswers] ADD [NumericValue] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012144759_AddAssessmentAnswerDetails'
)
BEGIN
    ALTER TABLE [AssessmentAnswers] ADD [OtherDescription] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012144759_AddAssessmentAnswerDetails'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012144759_AddAssessmentAnswerDetails', N'8.0.31');
END;
GO

COMMIT;
GO


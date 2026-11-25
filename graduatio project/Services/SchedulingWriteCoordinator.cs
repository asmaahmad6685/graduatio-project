using System.Data;
using graduatio_project.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Services;

public sealed class SchedulingWriteCoordinator(ApplicationDbContext context)
{
    // Share the existing process-level gate between schedule edits and appointment writes.
    // SQL Server application locks provide coordination across application instances.
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public async Task<T> ExecuteAsync<T>(IEnumerable<int> therapistIds, IEnumerable<int> patientIds,
        Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        var lockNames = patientIds.Distinct().Order()
            .Select(id => $"PhysioGuide.PatientAppointments.{id}")
            .Concat(therapistIds.Distinct().Order()
                .Select(id => $"PhysioGuide.TherapistSchedules.{id}"))
            .ToArray();

        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            if (context.Database.IsSqlServer())
            {
                // Every caller acquires patient locks first, then therapist locks, in ID order.
                foreach (var lockName in lockNames)
                {
                    await context.Database.ExecuteSqlInterpolatedAsync($"""
                        DECLARE @result int;
                        EXEC @result = sys.sp_getapplock
                            @Resource = {lockName},
                            @LockMode = N'Exclusive',
                            @LockOwner = N'Transaction',
                            @LockTimeout = 15000;
                        IF @result < 0
                            THROW 51001, 'Scheduling write lock could not be acquired.', 1;
                        """, cancellationToken);
                }
            }

            var result = await operation();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SchedulingConflictException("The schedule changed concurrently. Reload the schedule and try again.");
        }
        catch (DbUpdateException)
        {
            throw new SchedulingConflictException("The schedule could not be saved because related data changed. Reload the schedule and try again.");
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 2601 or 2627 or 3960 or 51001)
        {
            throw new SchedulingConflictException("The schedule could not be changed concurrently. Reload the schedule and try again.");
        }
        finally
        {
            WriteLock.Release();
        }
    }
}

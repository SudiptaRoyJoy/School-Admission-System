using Microsoft.EntityFrameworkCore;
using Admission.Api.Data;

namespace Admission.Api.Infrastructure;

public sealed class HoldExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<HoldExpiryWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
                var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    WITH due AS (
                        SELECT id
                        FROM holds
                        WHERE status = 'HELD'
                          AND expires_at <= statement_timestamp()
                        ORDER BY expires_at
                        LIMIT {BatchSize}
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE holds AS h
                    SET status = 'EXPIRED'
                    FROM due
                    WHERE h.id = due.id
                      AND h.status = 'HELD'
                      AND h.expires_at <= statement_timestamp()
                    """, stoppingToken);

                if (updated > 0)
                    logger.LogInformation("Marked {Count} expired admission holds.", updated);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The expiry cleanup cycle failed. It will retry on the next interval.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

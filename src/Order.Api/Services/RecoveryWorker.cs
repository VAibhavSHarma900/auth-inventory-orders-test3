using Order.Api.Repositories;
namespace Order.Api.Services;

public sealed class RecoveryWorker(IServiceScopeFactory scopes, ILogger<RecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
                var processor = scope.ServiceProvider.GetRequiredService<OperationProcessor>();
                foreach (var id in await repository.PendingAsync(stoppingToken))
                {
                    try { await processor.ProcessAsync(id, stoppingToken); }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogError("Recovery operation {OperationId} will be retried; exception {ExceptionType}", id, ex.GetType().Name); }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogError("Recovery database unavailable; exception {ExceptionType}", ex.GetType().Name); }
        }
    }
}

using Order.Api.Repositories;
using Test3.Common;
namespace Order.Api.Services;

public sealed class OperationProcessor(IOrderRepository repository, IInventoryClient inventory, ILogger<OperationProcessor> logger)
{
    public async Task ProcessAsync(Guid operationId, CancellationToken ct)
    {
        await using var write = await repository.BeginOperationAsync(operationId, ct);
        if (write is null) return; // Another request/worker owns it, it is finished, or its retry is not due.
        var operation = write.Operation;
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = operation.CorrelationId });
        try
        {
            var result = await inventory.ExecuteAsync(operation, ct);
            await write.FinishAsync(result, ct);
            logger.LogInformation("Operation {OperationId} for order {OrderId}: {Outcome}", operationId, operation.OrderId, result.Outcome);
        }
        catch (ApiException ex) when (ex.StatusCode == 503)
        {
            await write.RetryAsync(ct);
            logger.LogWarning("Operation {OperationId}, order {OrderId} remains PENDING; safe retry scheduled", operationId, operation.OrderId);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError("Operation {OperationId}, order {OrderId} remains recoverable after {ExceptionType}", operationId, operation.OrderId, ex.GetType().Name);
            throw;
        }
        // A crash or local commit error rolls back this operation transaction. Its durable PENDING row survives.
    }
}

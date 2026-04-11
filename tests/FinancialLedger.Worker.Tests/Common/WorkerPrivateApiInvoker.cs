using System.Reflection;
using FinancialLedger.Worker.Workers;
using StackExchange.Redis;

namespace FinancialLedger.Worker.Tests.Common;

internal static class WorkerPrivateApiInvoker
{
    public static async Task<bool> HandleEntryAsync(TradeEventConsumerWorker worker, StreamEntry entry, CancellationToken cancellationToken)
    {
        var method = typeof(TradeEventConsumerWorker).GetMethod(
            "HandleEntryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("TradeEventConsumerWorker.HandleEntryAsync");

        var result = method.Invoke(worker, [entry, cancellationToken])
            ?? throw new InvalidOperationException("HandleEntryAsync returned null task");

        return await (Task<bool>)result;
    }

    public static object? ParseEvent(TradeEventConsumerWorker worker, StreamEntry entry)
    {
        var method = typeof(TradeEventConsumerWorker).GetMethod(
            "ParseEvent",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("TradeEventConsumerWorker.ParseEvent");

        return method.Invoke(worker, [entry]);
    }

    public static string BuildSnapshotTriggerTransactionId(object evt, string fallbackEntryId)
    {
        var evtType = evt.GetType();
        var method = typeof(TradeEventConsumerWorker).GetMethod(
            "BuildSnapshotTriggerTransactionId",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("TradeEventConsumerWorker.BuildSnapshotTriggerTransactionId");

        return (string)(method.Invoke(null, [evt, fallbackEntryId])
            ?? throw new InvalidOperationException("BuildSnapshotTriggerTransactionId returned null"));
    }

    public static string? ParseOrderSideFromTransactionId(string? transactionId)
    {
        var method = typeof(TradeEventConsumerWorker).GetMethod(
            "ParseOrderSideFromTransactionId",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("TradeEventConsumerWorker.ParseOrderSideFromTransactionId");

        return (string?)method.Invoke(null, [transactionId]);
    }
}

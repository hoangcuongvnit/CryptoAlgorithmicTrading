using FinancialLedger.Worker.Configuration;
using FinancialLedger.Worker.Domain;
using FinancialLedger.Worker.Hubs;
using FinancialLedger.Worker.Tests.Common;
using FinancialLedger.Worker.Workers;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace FinancialLedger.Worker.Tests.Unit;

public sealed class TradeEventConsumerWorkerPrivateTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ParseEvent_ShouldReturnNull_WhenTypeMissing()
    {
        var worker = CreateWorker();
        var entry = new StreamEntry("1-0", [new NameValueEntry("amount", "100")]);

        var parsed = WorkerPrivateApiInvoker.ParseEvent(worker, entry);

        parsed.Should().BeNull();
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ParseEvent_ShouldApplyDefaults_WhenOptionalFieldsMissing()
    {
        var worker = CreateWorker();
        var entry = new StreamEntry("2-0",
        [
            new NameValueEntry("type", "sell_cash_in"),
            new NameValueEntry("amount", "42.15"),
        ]);

        var parsed = WorkerPrivateApiInvoker.ParseEvent(worker, entry);

        parsed.Should().NotBeNull();
        parsed!.GetType().GetProperty("Environment")!.GetValue(parsed)!.Should().Be("TESTNET");
        parsed.GetType().GetProperty("AlgorithmName")!.GetValue(parsed)!.Should().Be("TEST_ALGO");
        parsed.GetType().GetProperty("Type")!.GetValue(parsed)!.Should().Be(LedgerEntryTypes.SellCashIn);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void BuildSnapshotTriggerTransactionId_ShouldNormalizeCommissionTransaction()
    {
        var worker = CreateWorker();
        var entry = new StreamEntry("3-0",
        [
            new NameValueEntry("type", "COMMISSION"),
            new NameValueEntry("amount", "-1.2"),
            new NameValueEntry("transactionId", "abc123:COMMISSION:BUY"),
        ]);

        var parsed = WorkerPrivateApiInvoker.ParseEvent(worker, entry);

        var triggerId = WorkerPrivateApiInvoker.BuildSnapshotTriggerTransactionId(parsed!, "3-0");

        triggerId.Should().Be("ORDER:abc123:BUY");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ParseOrderSideFromTransactionId_ShouldDetectBuySellMarkers()
    {
        WorkerPrivateApiInvoker.ParseOrderSideFromTransactionId("x:COMMISSION:BUY").Should().Be(EquityEventTypes.Buy);
        WorkerPrivateApiInvoker.ParseOrderSideFromTransactionId("x:COMMISSION:SELL").Should().Be(EquityEventTypes.Sell);
        WorkerPrivateApiInvoker.ParseOrderSideFromTransactionId("x:REALIZED_PNL").Should().BeNull();
    }

    private static TradeEventConsumerWorker CreateWorker()
    {
        var redis = new Mock<IConnectionMultiplexer>();
        var scopeFactory = new Mock<IServiceScopeFactory>();

        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.SetupGet(x => x.All).Returns(clientProxy.Object);

        var hub = new Mock<IHubContext<LedgerHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);

        return new TradeEventConsumerWorker(
            redis.Object,
            scopeFactory.Object,
            new LedgerSettings
            {
                DefaultEnvironment = "TESTNET",
                DefaultAlgorithmName = "TEST_ALGO",
            },
            hub.Object,
            Mock.Of<ILogger<TradeEventConsumerWorker>>());
    }
}

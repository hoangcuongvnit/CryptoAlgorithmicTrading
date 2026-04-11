# Test Projects Overview

Tai lieu nay tong hop toan bo test project hien co trong repository.

## Danh sach test project

Hien tai repository co **1 test project**:

1. `tests/FinancialLedger.Worker.Tests/FinancialLedger.Worker.Tests.csproj`
   - Muc tieu: Kiem thu cho service `FinancialLedger.Worker`
   - Framework: `net10.0`
   - Test stack: `xUnit`, `FluentAssertions`, `Moq`
   - Integration stack: `Testcontainers`, `PostgreSql`, `Redis`

## Cau truc thu muc test

```text
tests/
└─ FinancialLedger.Worker.Tests/
   ├─ Common/
   │  ├─ TestCategories.cs
   │  ├─ WorkerPrivateApiInvoker.cs
   │  ├─ LedgerTestSchema.cs
   │  └─ HttpMessageHandlerStub.cs
   ├─ Fixtures/
   │  └─ LedgerIntegrationFixture.cs
   ├─ Unit/
   │  └─ TradeEventConsumerWorkerPrivateTests.cs
   ├─ Integration/
   │  ├─ TradeEventConsumerWorkerIntegrationTests.cs
   │  └─ LedgerRepositoryIntegrationTests.cs
   └─ Smoke/
      └─ FinancialLedgerSyncSmokeTests.cs
```

## Phan loai test theo Category

Category duoc dinh nghia tai `Common/TestCategories.cs`:

- `Unit`
- `Integration`
- `Smoke`

Tat ca test hien tai deu duoc gan trait theo mau:

```csharp
[Trait("Category", TestCategories.Unit)]
```

## Mo ta pham vi test hien tai

### Unit tests

- File: `Unit/TradeEventConsumerWorkerPrivateTests.cs`
- Kiem tra logic noi bo cua `TradeEventConsumerWorker`:
  - Parse event khi thieu truong bat buoc
  - Gia tri mac dinh cho field tuy chon
  - Chuan hoa transaction id cho snapshot trigger
  - Parse order side (BUY/SELL) tu transaction id

### Integration tests

- File: `Integration/TradeEventConsumerWorkerIntegrationTests.cs`
  - Kiem thu xu ly stream entry va mapping field xuong DB
  - Kiem thu fallback session ve active session khi session id khong ton tai

- File: `Integration/LedgerRepositoryIntegrationTests.cs`
  - Kiem thu `InsertLedgerEntryAsync`
  - Dam bao unique `(session_id, binance_transaction_id)` hoat dong dung (chong duplicate)

### Smoke tests

- File: `Smoke/FinancialLedgerSyncSmokeTests.cs`
  - Gia lap luong su kien buy/commission/sell/realized pnl
  - Kiem tra so du cuoi cung khop voi ket qua ky vong
  - Kiem tra so dong persisted trong `ledger_entries`

## Fixture va ha tang test

`Fixtures/LedgerIntegrationFixture.cs` su dung Testcontainers de khoi tao:

- PostgreSQL container: `postgres:16-alpine`
- Redis container: `redis:7-alpine`

Fixture tu tao schema test thong qua `Common/LedgerTestSchema.cs` va cung cap:

- Reset data giua cac test
- Seed active session + initial funding
- Tao `ServiceProvider` de resolve cac service can test

## Cach chay test

### Chay toan bo test project

```bash
dotnet test tests/FinancialLedger.Worker.Tests/FinancialLedger.Worker.Tests.csproj
```

### Chay theo Category

```bash
# Unit
dotnet test tests/FinancialLedger.Worker.Tests/FinancialLedger.Worker.Tests.csproj --filter "Category=Unit"

# Integration
dotnet test tests/FinancialLedger.Worker.Tests/FinancialLedger.Worker.Tests.csproj --filter "Category=Integration"

# Smoke
dotnet test tests/FinancialLedger.Worker.Tests/FinancialLedger.Worker.Tests.csproj --filter "Category=Smoke"
```

### Chay co thu thap coverage

```bash
dotnet test tests/FinancialLedger.Worker.Tests/FinancialLedger.Worker.Tests.csproj /p:CollectCoverage=true
```

## Yeu cau moi truong

De chay Integration/Smoke tests, can:

- Docker daemon dang chay (Testcontainers can Docker)
- .NET SDK 10.0 (theo `global.json`)

Neu Docker khong san sang, Unit tests van co the chay doc lap.

## Ghi chu cap nhat

Khi them test project moi, vui long cap nhat file nay theo checklist:

1. Them ten project vao "Danh sach test project"
2. Them cau truc thu muc va test files chinh
3. Them huong dan chay test theo category/bo loc phu hop
4. Bo sung yeu cau moi truong dac thu (neu co)

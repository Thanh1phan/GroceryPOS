# GroceryPOS

Phần mềm bán hàng & quản lý cửa hàng tạp hóa (.NET 8 WinForms).
Point-of-sale and store management app for a Vietnamese grocery store.

> Status: early development – see [ROADMAP.md](ROADMAP.md) and [CHANGELOG.md](CHANGELOG.md).

## Architecture (Clean Architecture)

```
src/
  GroceryPOS.Domain          Entities, value objects, domain rules (no dependencies)
  GroceryPOS.Application     Use cases (auth, checkout/void, returns, shifts, POS lookup, inventory, goods receipts, supplier payments & returns, reports & dashboard), DTOs, interfaces, Result pattern, FluentValidation validators, `AddApplication()` DI
  GroceryPOS.Infrastructure  EF Core 8 DbContext (SQLite default / SQL Server), migrations per provider, repositories, audit log, BCrypt, start-up seeding, Serilog file logging
  GroceryPOS.UI.Controls     Reusable themed WinForms component library                       (planned)
  GroceryPOS.WinForms        App shell, MVP views & presenters, DI host                        (planned)
tests/
  GroceryPOS.Domain.Tests    Domain tests (identity, catalog, partners, sales, returns, shifts, stock ledger, purchasing)
  GroceryPOS.Application.Tests  Application tests (auth, checkout, returns, shifts, POS lookup, inventory, purchasing, reports, formatting)
  GroceryPOS.Infrastructure.Tests  Persistence tests on in-memory SQLite (migrations, round-trips, model/migration drift, DI composition, logging)
```

Conventions:
- UI text in Vietnamese; code, identifiers and comments in English.
- Currency is VND, formatted with `vi-VN` (`125.000 ₫`) via `GroceryPOS.Application.Formatting.Vnd`.
- Use cases return `Result` / `Result<T>` for expected failures; `DomainException` signals broken invariants.
- Every stock change goes through `Domain.Inventory.StockMovement.Apply`, which updates the product and returns a ledger row (thẻ kho), so the ledger always explains the current stock.
- Cost price is a moving weighted average: completing a goods receipt (`PN…`) re-averages each product's cost with the landed cost (after line and receipt discounts).
- Supplier debt only changes through documents: completed receipts (`PN…`, unpaid part), payments (`PC…`, cancellable with a reason), and supplier returns (`TN…`, value not refunded in money is offset against debt). The supplier debt ledger is rebuilt from these documents backwards from the current balance. Goods returned to a supplier leave stock at the current average cost; a return against a receipt is capped at the received quantity minus earlier returns and at the receipt line's landed cost.
- Customer returns (`TH…`) refund each line's share of its final (discounted) amount; the last units of a line refund exactly what is left. An invoice with returns can no longer be voided.
- Shifts (`CA…`) are per user: the drawer's expected cash is the float plus the user's cash sales (net of change), minus void cash and cash refunds, plus/minus manual pay-ins/outs. Closing freezes the totals; what is left in the drawer pre-fills the next shift's float.
- Reports work in store days (UTC+7, `Common.StoreCalendar`): a sale counts on the day it was completed, voided invoices are left out, and a customer return lowers revenue, VAT and profit on the day it was taken (restocked goods give their cost back, written-off goods do not), so a closed day's figures never change.
- Input forms are pre-validated with FluentValidation (`Application.Validation`, e.g. `CheckoutRequestValidator`, `CloseShiftRequestValidator`, `ChangePasswordInputValidator`); `ToFieldErrors()` gives per-field Vietnamese messages for dialogs and `ToResult()` plugs into the Result pattern. Services still enforce every rule themselves.
- Authorization is permission-based (`Domain.Identity.Permissions`); built-in roles: Admin, Quản lý, Thu ngân, Thủ kho.

## Build & run

Requirements: .NET 8 SDK (the WinForms app will require Windows to run).

```bash
dotnet build GroceryPOS.sln
dotnet test GroceryPOS.sln
```

## Database

The store database is configured in the `Database` section of appsettings (`DatabaseOptions`):

```json
"Database": { "Provider": "Sqlite", "ConnectionString": "Data Source=grocerypos.db" }
"Database": { "Provider": "SqlServer", "ConnectionString": "Server=.;Database=GroceryPOS;Trusted_Connection=True;TrustServerCertificate=True" }
```

`services.AddInfrastructure(databaseOptions, seedOptions)` registers `GroceryDbContext` (also as `IUnitOfWork`) for the chosen provider,
all repositories, `IAuditService`, `IPasswordHasher` (BCrypt) and `IDatabaseInitializer`. Call
`IDatabaseInitializer.InitializeAsync()` in a scope at start-up: it applies migrations and seeds roles, the default admin
and a default category (safe to run on every start).
Each provider has its own context subclass and migration history (`Persistence/Migrations/Sqlite`, `.../SqlServer`).
On SQLite, `DateTimeOffset` is stored as a sortable integer so period filters run in the database; money/quantities are `decimal(18,3)`.
Created/updated time and user of auditable rows are stamped by `AuditableEntityInterceptor`.

After changing the model, add a migration for **both** providers (the drift tests fail otherwise):

```bash
dotnet tool restore
cd src/GroceryPOS.Infrastructure
dotnet ef migrations add <Name> --context SqliteGroceryDbContext -o Persistence/Migrations/Sqlite
dotnet ef migrations add <Name> --context SqlServerGroceryDbContext -o Persistence/Migrations/SqlServer
```

## Composition & logging

```csharp
services.AddApplication(salesOptions)                 // use cases, session (ICurrentUser), validators
        .AddInfrastructure(databaseOptions, seedOptions)
        .AddGroceryLogging(loggingOptions);           // ILogger<T> → Serilog
```

Logging is configured in the `AppLogging` section (`LoggingOptions`):

```json
"AppLogging": { "Directory": null, "MinimumLevel": "Information", "FrameworkMinimumLevel": "Warning",
                "RetainedFileCountLimit": 30, "FileSizeLimitMegabytes": 10, "WriteErrorLog": true }
```

Files go to `%LocalAppData%\GroceryPOS\logs` by default (relative `Directory` = next to the app): `grocerypos-yyyyMMdd.log`
(daily, rolls over by size, 30 days) and `errors-yyyyMMdd.log` (Error/Fatal only, 90 days). Every line carries the signed-in
user, e.g. `[INF] (admin) …AuthService: User admin signed in as Admin`. Business actions are additionally kept in the
database audit log (`IAuditService`).

## Default account

On a new database the initializer creates `admin` / `admin123` (role Admin) and forces a password change on first login.
Override it in the `Seed` section of appsettings (`AdminUsername`, `AdminPassword`, `AdminFullName`, `DefaultCategoryName`).

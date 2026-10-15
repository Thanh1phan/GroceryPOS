# GroceryPOS

Phần mềm bán hàng & quản lý cửa hàng tạp hóa (.NET 8 WinForms).
Point-of-sale and store management app for a Vietnamese grocery store.

> Status: early development – see [ROADMAP.md](ROADMAP.md) and [CHANGELOG.md](CHANGELOG.md).

## Architecture (Clean Architecture)

```
src/
  GroceryPOS.Domain          Entities, value objects, domain rules (no dependencies)
  GroceryPOS.Application     Use cases (auth, checkout/void, returns, shifts, POS lookup, inventory, goods receipts, supplier payments & returns, reports & dashboard), DTOs, interfaces, Result pattern
  GroceryPOS.Infrastructure  EF Core 8 (SQLite / SQL Server), repositories, BCrypt, Serilog   (planned)
  GroceryPOS.UI.Controls     Reusable themed WinForms component library                       (planned)
  GroceryPOS.WinForms        App shell, MVP views & presenters, DI host                        (planned)
tests/
  GroceryPOS.Testing         Dependency-free xUnit-compatible harness (temporary, see below)
  GroceryPOS.Domain.Tests    Domain tests (identity, catalog, partners, sales, returns, shifts, stock ledger, purchasing)
  GroceryPOS.Application.Tests  Application tests (auth, checkout, returns, shifts, POS lookup, inventory, purchasing, reports, formatting)
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
- Authorization is permission-based (`Domain.Identity.Permissions`); built-in roles: Admin, Quản lý, Thu ngân, Thủ kho.

## Build & run

Requirements: .NET 8 SDK (the WinForms app will require Windows to run).

```bash
dotnet build GroceryPOS.sln

# Run tests (each test project is a small console runner; optional name filter as last arg)
dotnet run --project tests/GroceryPOS.Domain.Tests
dotnet run --project tests/GroceryPOS.Application.Tests -- AuthService
```

If nuget.org is unreachable (offline sandbox), build with an empty local feed – the projects have no package dependencies yet:

```bash
mkdir -p /tmp/emptyfeed && dotnet build GroceryPOS.sln -p:RestoreSources=/tmp/emptyfeed
```

Tests use `[Fact]` / `[Theory]` / `Assert` from namespace `Xunit`, served by the in-repo harness
`tests/GroceryPOS.Testing` because the CI sandbox cannot reach nuget.org yet. Moving to real xUnit
only requires swapping the project reference for the xunit packages and deleting each `Program.cs`.

## Default account

Seeding will create `admin` / `admin123` (must be changed on first login) once the Infrastructure layer lands.

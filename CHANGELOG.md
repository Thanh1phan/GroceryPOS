# Changelog

## 2026-10-09
- Added the POS checkout use case (`Application.Sales.CheckoutService`): permission checks (`sales.create`, `sales.discount`), daily invoice numbers `HD{yyMMdd}-{0001}` in store time (UTC+7), line/invoice discounts, split payments, stock deduction (negative stock configurable via `SalesOptions`), loyalty redeem/earn, low-stock alerts on the receipt, audit entry. All checks run before any entity is touched, so a failed checkout leaves nothing half-applied.
- Added void sale (`sales.void`): returns stock, reverses spend/earned points and refunds redeemed points (`Customer.ReverseSale`), audited with reason.
- Added `PosLookupService`: scan by barcode or product code, quick search (active products only), find member by phone in any VN format.
- Added repository abstractions `IProductRepository`, `ICustomerRepository`, `ISaleRepository`; 25 new tests (Application 52, Domain 68 – all passing).

## 2026-10-08
- Added the sales domain (`Domain.Sales`): `Sale` aggregate with line and invoice discounts (percent/amount), loyalty points redemption, split payments (cash/transfer/card), change calculation, completion/void rules, per-rate VAT breakdown and profit.
- Added `Money` helpers (VAT extraction, largest-remainder allocation so invoice discounts spread exactly across lines) and `Discount` value object.
- Added test projects `GroceryPOS.Domain.Tests` (67 tests) and `GroceryPOS.Application.Tests` (28 tests, incl. `AuthService` lockout/change-password with in-memory fakes).
- Added `GroceryPOS.Testing`, a dependency-free xUnit-compatible harness (`[Fact]`, `[Theory]`, `Assert`), because nuget.org is still blocked in the build sandbox; test files are source-compatible with xUnit.

## 2026-10-07
- Created solution foundation: `GroceryPOS.sln`, `Directory.Build.props` (nullable, warnings-as-errors), `.editorconfig`, `.gitignore`, `ROADMAP.md`.
- Added `GroceryPOS.Domain`: identity (User with lockout, Role, permission catalog, 4 system roles), catalog (Category, Product with EAN check-digit validation, VAT 0/5/8/10%, stock rules), partners (Supplier, Customer with loyalty points), audit log.
- Added `GroceryPOS.Application`: Result/Error pattern, repository/UoW/password-hasher/current-user/audit abstractions, VND formatter/parser (vi-VN).
- Added `AuthService`: login with lockout (5 failures → 15 min), change password with password policy, logout, all audited.
- Note: nuget.org was blocked in the build sandbox, so only package-free projects were added this run.

# Changelog

## 2026-10-11
- Added the purchasing domain (`Domain.Purchasing.GoodsReceipt`, phiếu nhập hàng): draft lines with quantity, supplier price, line discount and optional new shelf price; receipt-level discount allocated exactly to lines (largest remainder) to get each line's landed cost; paid amount vs. debt. Completing checks every rule first, then moves each product's cost to the moving weighted average (`Product.ApplyPurchaseCost`), applies new shelf prices and writes `GoodsReceipt` ledger entries at the landed cost.
- Added `GoodsReceiptService` (`purchases.manage`): save/rewrite draft, complete (supplier debt via `Supplier.AddDebt`, price-change and receipt audit entries, below-cost warnings), cancel draft, get, search by period/supplier/status, and reorder suggestions from low-stock products.
- `Supplier` now tracks `DebtBalance` (`AddDebt` / `PayDebt`); `StockMovement.Apply` accepts an explicit unit cost; daily document numbering extracted to `Common.DailyCode` (shared by invoices `HD…` and receipts `PN…`).
- 30 new tests (Domain 98, Application 78 – all passing); test harness gained `Assert.All`.

## 2026-10-10
- Added the stock ledger (`Domain.Inventory.StockMovement`, thẻ kho): every stock change records type, signed quantity, balance after, unit cost, reference code and user; movement types enforce their direction and adjustment reasons (hư hỏng, hết hạn, mất mát, dùng nội bộ, phát hiện thừa, sửa sai...) enforce theirs.
- Checkout and void now write `Sale` / `SaleVoid` ledger entries referencing the invoice code (`CheckoutService` takes an `IStockMovementRepository`).
- Added `InventoryService`: manual adjustment (`inventory.adjust`, never below zero, audited with before/after and cost value), stock count `KK{yyMMdd-HHmmss}` that sets counted quantities and reports shortage/surplus value, stock card for a period with opening/closing balance, and low-stock list (`inventory.view`).
- 28 new tests (Domain 85, Application 63 – all passing).

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

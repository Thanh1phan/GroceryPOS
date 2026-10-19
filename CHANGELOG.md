# Changelog

## 2026-10-19 (data grid)
- Added `ModernDataGrid` to `GroceryPOS.UI.Controls`: one list control for every screen – search box (300 ms debounce, Esc clears, Ctrl+F, typing in the grid starts a search) with a toolbar slot for screen buttons, themed flat grid (hover row, brand-tinted selection, empty-state text, DPI-aware row heights) and a pager (summary "Hiển thị 21–40 / 1.235 dòng", first/prev/page links/next/last, 20/50/100/200 rows per page, Ctrl+PageUp/PageDown).
- Two data modes: `SetItems(...)` searches/sorts/pages in memory (accent-insensitive search over what the user sees, e.g. "32.000 ₫"; header click cycles ascending → descending → unsorted; Vietnamese collation; search, sort, page and selection are kept on reload), or `DataProvider` loads each page from the application layer (stale responses dropped, `LoadFailed` or global handler on errors).
- Column helpers `AddTextColumn`, `AddMoneyColumn` (`#,##0 ₫`), `AddNumberColumn`, `AddDateColumn`, `AddCheckColumn` with dotted property paths (`Category.Name`); events `RowActivated` (Enter/double-click), `SelectionChanged`, `QueryChanged`, `PageLoaded`.
- New pure grid core (`Grids/Core`): `GridQuery`, `GridPage<T>`, `GridDataView<T>`, `PageLinks` (1 … 4 5 6 … 20), `GridPalette` (WCAG-checked), `PropertyPath` – 40 new tests (UI.Controls 198; Domain 135, Application 142, Infrastructure 33 – all passing).

## 2026-10-19
- Added input controls to `GroceryPOS.UI.Controls`: `ModernNumericBox` (vi-VN grouping while typing with a stable caret, min/max clamp on leave, ↑/↓ stepping with Shift ×10, keypad decimal key → decimal comma, nullable via `AllowEmpty`), `ModernCurrencyBox` (whole đồng, `₫` suffix, `25k` → 25.000, steps of 1.000 đ) and `ModernDatePicker` (dd/MM/yyyy, accepts `19/10`, `191026`, `hôm nay`, `hôm qua`, `+3`/`-7`; ↑/↓ day, PageUp/PageDown month, F4/Alt+↓ calendar drop-down; min/max with Vietnamese error messages).
- Added `ModernComboBox`: same rounded box, icon, placeholder and validation line as the text box, owner-drawn themed items, `DataSource`/`DisplayMember`/`ValueMember`, and in DropDown style an accent-insensitive filter-as-you-type list (best matches first, `LimitToList` for lookups vs. free text).
- `ModernTextBox` gained `SuffixText` (unit such as `₫`, `kg`, `%`) and a clickable `TrailingGlyph` (`TrailingIconClick`); box, icon and message painting moved to a shared `InputChrome` so all inputs look identical.
- New pure input core (`Inputs/Core`): `NumericFormat`/`NumericText` (parse "1.250.000 ₫", "25k", "1,5"/"1.5" kg; format; regroup with caret), `DateText`, `DatePresets` (Hôm nay … Năm nay, Monday-based weeks) with `DateRange`, and `TextMatch` (accent-folding search) – compiled into the Linux test project.
- 107 new tests (Domain 135, Application 142, Infrastructure 33, UI.Controls 158 – all passing).

## 2026-10-18
- Started the component library `GroceryPOS.UI.Controls` (net8.0-windows, builds on Linux via `EnableWindowsTargeting`): design tokens (`ColorTokens`, `TypographyTokens`, `SpacingTokens`) with built-in light ("Sáng") and dark ("Tối") themes, and `ThemeManager` (`SetMode`/`ToggleMode`, `ThemeChanged`, `Apply`/`Track` for plain WinForms controls, `IThemeable` for self-styled ones) plus `ThemeFonts` cache (Segoe UI; Segoe Fluent Icons → MDL2 fallback).
- Added `ModernButton`: rounded, owner-drawn `Button` with Primary/Secondary/Danger/Ghost variants, hover/pressed/disabled states, keyboard focus ring and optional icon glyph (`Icons.Glyphs`); keeps `AcceptButton`/`DialogResult` behaviour.
- Added `ModernTextBox`: rounded input with leading icon, placeholder, password mode, read-only look, validation state (error/warning/valid) and a reserved message line (`SetError`, `SetWarning`, `ClearValidation`, `HelperText`), plus `EnterPressed` for barcode scanners.
- State → color mapping (`ButtonPalette`, `InputPalette`) and `ColorMath` are pure and covered by the new `GroceryPOS.UI.Controls.Tests`, including WCAG AA contrast checks for every theme and button state.
- 51 new tests (Domain 135, Application 142, Infrastructure 33, UI.Controls 51 – all passing).

## 2026-10-17
- Added Serilog logging (`Infrastructure.Logging`): daily rolling, size-capped `grocerypos-*.log` plus an error-only `errors-*.log`, every line stamped with the signed-in user; levels, folder and retention come from the `AppLogging` section. `services.AddGroceryLogging(options)` routes `ILogger<T>` (and EF Core) to it.
- `AuthService` now logs sign-in, failed logins, lockouts, inactive accounts, password changes and sign-out (structured, never the password).
- Added FluentValidation validators (`Application.Validation`) for checkout/discounts, customer returns, stock adjustments and counts, goods receipts, supplier payments and returns, shift open/pay-in/out/close (count by denomination must match the typed total) and the new `LoginInput` / `ChangePasswordInput`; helpers `ToFieldErrors()`, `ToResult()`, `Check()` and shared money rules (whole đồng, not negative).
- Added `services.AddApplication()` registering every use case, the singleton desktop session (`IUserSessionStore` = `ICurrentUser`), store settings and all validators; a test builds the full container (`ValidateOnBuild`) and logs in end-to-end.
- 26 new tests (Domain 135, Application 142, Infrastructure 33 – all passing).

## 2026-10-16 (2)
- Added EF Core repositories for every application abstraction (users with role + permissions, roles, categories, products, customers, suppliers, sales, customer returns, stock ledger, goods receipts, supplier payments/returns, shifts): period filters and document-number lookups run in SQL, aggregates load with their lines.
- Product quick search is accent- and case-insensitive (`sua tuoi` finds "Sữa tươi", `duong` finds "Đường") via the new `Application.Common.SearchText`; exact code/barcode hits rank first, inactive products are skipped.
- Added `AuditService` (entries saved in the same unit of work as the change, stamped with the signed-in user), `BCryptPasswordHasher` (BCrypt.Net-Next, work factor 11) and `IDatabaseInitializer` (migrate + idempotent seed: 4 system roles, new permissions granted to Admin, default `admin` / `admin123` that must be changed, one default category).
- `services.AddInfrastructure(databaseOptions, seedOptions)` registers persistence, repositories, audit, hashing and seeding in one call.
- 16 new tests incl. an end-to-end DI login against a seeded SQLite file (Domain 135, Application 125, Infrastructure 24 – all passing).

## 2026-10-16
- Moved all tests to real xUnit 2.9 (`dotnet test`); removed the temporary in-repo harness now that NuGet is reachable.
- Added `GroceryPOS.Infrastructure` with EF Core 8: `GroceryDbContext` (implements `IUnitOfWork`) and entity configurations for identity, catalog, partners, sales/returns, stock ledger, purchasing and shifts (`Discount` as a complex type, shift totals as owned columns, restricted foreign keys, unique document codes/usernames/product codes/barcodes).
- SQLite (default) and SQL Server are switchable via `DatabaseOptions` / `AddPersistence`; each provider has its own context subclass and `InitialCreate` migration. On SQLite, dates are stored as sortable integers so period queries run in SQL.
- `AuditableEntityInterceptor` stamps created/updated time and user (`system` when nobody is signed in).
- New `GroceryPOS.Infrastructure.Tests` (13 tests on in-memory SQLite: migrations, aggregate round-trips, date-range filters across UTC offsets, uniqueness, audit stamps, model-vs-migration drift for both providers). Totals: Domain 135, Application 120, Infrastructure 13 – all passing.

## 2026-10-15
- Added `ReportService` (`reports.view`): daily revenue report (`GetRevenueAsync`, one row per store day incl. empty days + totals: invoices, voided count, gross/discount/sales, returns & refunds, net revenue, VAT, revenue excl. VAT, cost of goods, gross profit & margin, write-off cost, cash/transfer/card collected, average basket), best sellers ranked by quantity, revenue or profit with category filter and revenue share, and profit by category.
- Report rules: sales count on their completion day in store time (UTC+7, new `Common.StoreCalendar`), voided invoices are excluded, customer returns are deducted on the day they were taken; restocked returns reverse their cost, written-off returns do not. Periods are limited to 366 days.
- Added inventory value report (`reports.view` or `inventory.view`): cost value, shelf value incl./excl. VAT and potential profit per category, with out-of-stock, low-stock and negative-stock counts (negative stock adds no value; inactive products optional).
- Added dashboard query (`dashboard.view`): today's net revenue, invoices, average basket and refunds vs. yesterday (% change), 7-day trend, sales by hour, top 5 products, low-stock alerts; gross profit only shown with `reports.view`.
- New `ICategoryRepository`, `ISaleRepository.ListCompletedInPeriodAsync`, `ISaleReturnRepository.ListInPeriodAsync`; 9 new tests (Domain 135, Application 120 – all passing).

## 2026-10-14
- Added supplier payments (`Domain.Purchasing.SupplierPayment`, phiếu chi `PC{yyMMdd}-{0001}`): cash or bank transfer, whole-đồng amounts up to the current debt, debt before/after on the slip; cancelling requires a reason and puts the debt back. New permission `suppliers.pay` (Admin, Quản lý).
- Added supplier returns (`Domain.Purchasing.SupplierReturn`, `TN{yyMMdd}-{0001}`): against a completed goods receipt (max received − already returned, price ≤ landed cost, default landed cost) or without one (default current cost); stock leaves through `SupplierReturn` ledger entries at average cost; value is refunded in money and/or offset against debt (`Supplier.OffsetDebt`), all rules checked before anything changes.
- Added `SupplierPaymentService` (pay, cancel, payables list, payment search, supplier debt ledger with opening/running/closing balances) and `SupplierReturnService` (load returnable receipt, create, get, search), audited as `purchase.supplier_paid`, `purchase.supplier_payment_cancelled`, `purchase.supplier_returned`.
- Domain error messages now format VND as `1.250.000 ₫` via `Money.Format`, independent of the OS culture.
- 19 new tests (Domain 135, Application 111 – all passing).

## 2026-10-13
- Added cashier shifts & cash drawer (`Domain.Shifts.Shift`, ca bán hàng `CA{yyMMdd}-{0001}`): opening float, pay-in/pay-out movements with mandatory reason, closing with counted cash (typed or by VND denomination via `CashCount`), expected cash = float + cash sales − void cash − cash refunds + pay-ins − pay-outs, variance (thiếu/thừa) with a note required from `SalesOptions.ShiftVarianceNoteThreshold` (5.000 ₫), cash left in drawer vs. handed over. Totals (`ShiftTotals`) are frozen at closing.
- Added `ShiftService`: current shift with live report and suggested float (what the last shift left in the drawer), open (one per user), pay-in/out (pay-out never exceeds drawer cash), summary, close, history. Counter totals come from the shift user's sales (by tender), voids and returns in the shift window.
- New permission `shifts.supervise` (Admin, Quản lý) to view and force-close other users' shifts; audit actions `shift.opened`, `shift.closed`, `shift.cash_movement`.
- Repository additions: `IShiftRepository`, `ISaleRepository.ListCompletedByCashierAsync/ListVoidedByAsync`, `ISaleReturnRepository.ListByUserAsync`; test harness gained `Assert.Same` and predicate `Assert.Contains`.
- 24 new tests (Domain 124, Application 103 – all passing).

## 2026-10-12
- Added customer returns (`Domain.Sales.SaleReturn`, phiếu trả hàng `TH{yyMMdd}-{0001}`): several partial returns per invoice, each line refunding its share of the final amount after line/invoice/points discounts (the last units refund exactly what is left); per-line restock or write-off (hư hỏng) with cost values; earned points reversed in proportion to the refunded share, redeemed points given back only when the whole invoice is returned. All rules are checked before the sale is touched.
- `SaleLine` tracks `ReturnedQuantity` / `RefundedAmount`; `Sale` exposes `RefundedTotal`, `NetTotal`, `IsFullyReturned`, and refuses `Void` once it has returns (it would restock twice).
- Added `ReturnService` (`returns.create`): load an invoice by code with remaining quantities, create a return (`CustomerReturn` ledger entries at the sale's cost, customer spend/points, audit `sale.returned`), list returns of an invoice. Returns older than `SalesOptions.ReturnWindowDays` (7 store days) need `sales.void`.
- 27 new tests (Domain 113, Application 90 – all passing).

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

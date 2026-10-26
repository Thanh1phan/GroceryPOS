# GroceryPOS

Phần mềm bán hàng & quản lý cửa hàng tạp hóa (.NET 8 WinForms).
Point-of-sale and store management app for a Vietnamese grocery store.

> Status: early development – see [ROADMAP.md](ROADMAP.md) and [CHANGELOG.md](CHANGELOG.md).

## Architecture (Clean Architecture)

```
src/
  GroceryPOS.Domain          Entities, value objects, domain rules (no dependencies)
  GroceryPOS.Application     Use cases (auth, users & roles, catalog, customers & suppliers, checkout/void, returns, shifts, POS lookup, inventory, goods receipts, supplier payments & returns, reports & dashboard), DTOs, interfaces, Result pattern, FluentValidation validators, `AddApplication()` DI
  GroceryPOS.Infrastructure  EF Core 8 DbContext (SQLite default / SQL Server), migrations per provider, repositories, audit log, BCrypt, start-up seeding, Serilog file logging
  GroceryPOS.UI.Controls     Themed WinForms component library: design tokens, light/dark ThemeManager, ModernButton, ModernTextBox, ModernNumericBox/ModernCurrencyBox, ModernDatePicker, ModernComboBox, ModernDataGrid, ModernCard/StatCard, ModernBadge, SidebarNav, TopBar, ModernTabControl (in progress)
  GroceryPOS.App             WinForms app: DI host, login/change password, shell (sidebar + top bar), screens for categories, products, customers (+ loyalty history), suppliers, users, roles & permissions (matrix), POS counter (Bán hàng), shifts & cash drawer (Ca làm việc)
tests/
  GroceryPOS.Domain.Tests    Domain tests (identity, catalog, partners, sales, returns, shifts, stock ledger, purchasing)
  GroceryPOS.Application.Tests  Application tests (auth, checkout, POS cart, returns, shifts, POS lookup, inventory, purchasing, reports, formatting)
  GroceryPOS.UI.Controls.Tests  Theme & input core tests (color math, WCAG contrast, state colors, number/date parsing, date presets, accent-insensitive matching, grid search/sort/paging, pager links, badge/avatar/trend/surface colors, sidebar menu filtering & layout, tab strip overflow)
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
- **Ca làm việc** screen: open shift (float pre-filled from what the last shift left), pay-in/pay-out with quick reasons, live KPIs, mid-shift report (BÁO CÁO GIỮA CA), close with a count sheet by denomination or a typed total (variance badge, note required from 5.000 ₫, cash left for the next shift, hand-over amount) and a printable end-of-shift report (BÁO CÁO KẾT CA, 80/58 mm) with signature lines; shift history with date filter, supervisors see everyone and can close another cashier's shift (Kết ca hộ). The count sheet state is the tested `Application.Shifts.CashCountSheet`, the report layout `ShiftReportFormatter`.
- Checkout requires an open shift (`SalesOptions.RequireOpenShift`, on by default): the POS asks the cashier to open one on entry and keeps Thanh toán disabled until then.
- Reports work in store days (UTC+7, `Common.StoreCalendar`): a sale counts on the day it was completed, voided invoices are left out, and a customer return lowers revenue, VAT and profit on the day it was taken (restocked goods give their cost back, written-off goods do not), so a closed day's figures never change.
- Input forms are pre-validated with FluentValidation (`Application.Validation`, e.g. `CheckoutRequestValidator`, `CloseShiftRequestValidator`, `ChangePasswordInputValidator`); `ToFieldErrors()` gives per-field Vietnamese messages for dialogs and `ToResult()` plugs into the Result pattern. Services still enforce every rule themselves.
- UI colors, fonts and spacing come only from the current theme (`GroceryPOS.UI.Controls.Theming.ThemeManager.Current`); forms call `ThemeManager.Track(this)` and modern controls repaint themselves when the theme changes. Theme core files (`Theming/Core`) must stay free of WinForms types so they can be tested on Linux; the same goes for `Inputs/Core` (number/date parsing and formatting, date presets, `TextMatch`) `Grids/Core` (`GridQuery`, `GridPage<T>`, `GridDataView<T>`, `PageLinks`), `Display/Core` (`BadgePalette`, `Avatar`, `Trend`, `SurfacePalette`) and `Navigation/Core` (`NavMenu`, `NavCursor`, `SidebarLayout`, `TabStripLayout`).
- Inputs: amounts use `ModernCurrencyBox` (whole đồng, `₫` suffix, `25k` = 25.000, ↑/↓ ±1.000), other numbers `ModernNumericBox` (`Format = NumericFormat.Quantity/Percent/…`), dates `ModernDatePicker` (dd/MM/yyyy; also accepts `19/10`, `191026`, `hôm nay`, `-7`; ↑/↓ ±1 day, F4 calendar), lists `ModernComboBox` (`DataSource` + `DisplayMember`/`ValueMember`; `DropDownStyle = DropDown` filters as you type without accents). Report filters use `DatePresets` (Hôm nay, Tuần này, Tháng trước…; weeks start on Monday).
- Lists: use `ModernDataGrid`. Add columns with `AddTextColumn("Name", "Tên hàng")` (width 0 = fill), `AddMoneyColumn`, `AddNumberColumn`, `AddDateColumn`, `AddCheckColumn` (dotted paths like `Category.Name` work), put screen buttons in `ToolbarItems`, then either `SetItems(rows)` for in-memory search/sort/paging or set `DataProvider = (query, ct) => …` to return a `GridPage<object>` per `GridQuery` for large tables. Handle `RowActivated` to open the edit dialog.
- Shell: `SidebarNav` takes a `NavMenu` of `NavSection`s/`NavItem`s (key, Vietnamese text, glyph, required permissions – any one is enough, optional shortcut hint); set `PermissionFilter = currentUser.HasPermission` after sign-in so items the user may not open are hidden, handle `ItemSelected` to open the screen, `SetBadge("inventory", lowStockCount, BadgeKind.Warning)` for counts. `TopBar.SetPage(title, breadcrumb)` and `SetUser(fullName, roleName, username)`; give it a `UserMenu` (Đổi mật khẩu, Đăng xuất) and put screen buttons in `Actions`. Its theme toggle calls `ThemeManager.ToggleMode()` and raises `ThemeToggled` so the shell can save the choice.
- Surfaces: `ModernCard` (title/subtitle/icon header, children docked below it) for grouped content, `StatCard` for dashboard KPIs (`Value = Money.Format(x)`, `SetTrend(dto.RevenueChangePercent, "hôm qua")`, `HigherIsBetter = false` for refunds), `ModernBadge.Set("Hết hàng", BadgeKind.Danger)` for statuses, `ModernTabControl` with `ModernTabPage`s (`BadgeCount`, cancelable `Selecting` for unsaved edits).
- Master data codes: customers get `KH000001`, suppliers `NCC0001` (or a typed code) via `Common.SequentialCode`; codes never change after creation. A customer's phone is unique (it is the member lookup at the counter). A supplier with open debt cannot be set to "ngừng giao dịch".
- POS counter (**Bán hàng**): all state lives in the UI-free `Application.Sales.PosCart` (lines, discounts, member + points, tender, quick-cash suggestions, `BuildRequest()`); the screen only renders it. Keys: F2 quét mã · F3 tìm sản phẩm · F4 số lượng · F5 khách hàng (SĐT) · F6 giảm giá dòng · F7 giảm giá hóa đơn · F8 khách đưa · F9 thanh toán · +/− số lượng · Del xóa dòng · Ctrl+P in lại hóa đơn · Esc hủy đơn. Typing `3*<mã>` sells 3 units (`1,5*GAO01` for weighed goods).
- Receipts (**hóa đơn bán lẻ**): `Application.Sales.Receipts.ReceiptService` snapshots a sale (store info, cashier, items, discounts, tenders, change, VAT per rate, member points with a masked phone) and `ReceiptFormatter` lays it out as fixed-width text – 48 columns on 80 mm rolls, 32 on 58 mm. The app shows a print preview after each checkout (Enter prints, Esc skips) and Ctrl+P reprints any invoice; reprints are marked **BẢN IN LẠI**, show the print time and are audited (`sale.receipt_reprinted`).
- Authorization is permission-based (`Domain.Identity.Permissions`, grouped by area in `Permissions.Groups`); built-in roles: Admin, Quản lý, Thu ngân, Thủ kho. Admin always holds every permission; other roles (and new custom roles) are edited in the **Phân quyền** matrix. Permission changes apply at the next sign-in.
- Accounts: an administrator creates users with a temporary password (the user must change it at the first sign-in) and can reset passwords, unlock and stop accounts; accounts are never deleted. A user can only assign or manage roles whose permissions they all hold, nobody can change their own role or stop their own account, and the last active account able to manage roles is protected. Usernames are 3–50 unaccented letters, digits, `.`, `_`, `-`.

## Build & run

Requirements: .NET 8 SDK. The WinForms projects build anywhere (`EnableWindowsTargeting`) but the app runs on Windows only.

```bash
dotnet build GroceryPOS.sln
dotnet test GroceryPOS.sln
dotnet run --project src/GroceryPOS.App      # Windows
```

First start creates `grocerypos.db` (SQLite) next to the app and the default account **`admin` / `admin123`**, which must be changed at the first sign-in.

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

## Store & receipt settings

The `Store` section of appsettings (`StoreProfile`) holds what is printed on receipts: `Name`, `Address`, `Phone`, `TaxCode` (MST, optional), `ReceiptFooter` (`\n` for several lines), `ReceiptPaperWidthMm` (80 or 58) and `PreviewReceiptAfterCheckout`.

## Default account

On a new database the initializer creates `admin` / `admin123` (role Admin) and forces a password change on first login.
Override it in the `Seed` section of appsettings (`AdminUsername`, `AdminPassword`, `AdminFullName`, `DefaultCategoryName`).

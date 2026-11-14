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
  GroceryPOS.UI.Controls     Themed WinForms component library: design tokens, light/dark ThemeManager, ModernButton, ModernTextBox, ModernNumericBox/ModernCurrencyBox, ModernDatePicker, ModernComboBox, ModernSegmentedControl, ModernDataGrid, ModernCard/StatCard, ModernBadge, BarChart, SidebarNav, TopBar, ModernTabControl, Toaster (toasts), ModernDialog (themed message box), LoadingOverlay, base classes BaseForm / BaseUserControl / BaseListView<T> / BaseEditDialog
  GroceryPOS.Presentation    MVP presenters and view interfaces (`Mvp.Presenter<TView>`, `Mvp.IView`; UI-free, unit-tested) – screens are migrated to it one by one (Danh mục, Sản phẩm, Nhà cung cấp, Khách hàng, Người dùng so far)
  GroceryPOS.App             WinForms app: DI host, login/change password, shell (sidebar + top bar), dashboard (Tổng quan: KPIs, 7-day and hourly charts, top products, low stock), screens for categories, products, customers (+ loyalty history), suppliers, users, roles & permissions (matrix), POS counter (Bán hàng), customer returns (Trả hàng), shifts & cash drawer (Ca làm việc), inventory (Tồn kho: stock list, low-stock alerts, adjustments, stock card, stock count), goods receipts (Nhập hàng: list, editor with scanning, completion summary), supplier payables (Công nợ NCC: payables, payments, debt ledger, supplier returns), reports (Báo cáo: revenue, best sellers, profit by category, inventory value; Excel export, A4 print preview and PDF), audit log viewer (Nhật ký hệ thống), settings (Cấu hình: store, receipt with live preview, loyalty, sales rules, theme), backup & restore (Sao lưu dữ liệu)
tests/
  GroceryPOS.Domain.Tests    Domain tests (identity, catalog, partners, sales, returns, shifts, stock ledger, purchasing)
  GroceryPOS.Application.Tests  Application tests (auth, checkout, POS cart, returns, shifts, POS lookup, inventory, purchasing, reports, formatting)
  GroceryPOS.UI.Controls.Tests  Theme & input core tests (color math, WCAG contrast, state colors, number/date parsing, date presets, accent-insensitive matching, grid search/sort/paging, pager links, badge/avatar/trend/surface colors, sidebar menu filtering & layout, tab strip overflow, toast queue & layout, dialog specs, busy-overlay timing, load sequencing, dirty tracking, edit-dialog field grid, list command rules)
  GroceryPOS.Presentation.Tests  Presenter tests with fake views and a real DI container (permissions, add/edit flow, stop/resume feedback, validation, closed-view safety)
  GroceryPOS.Infrastructure.Tests  Persistence tests on in-memory SQLite (migrations, round-trips, model/migration drift, DI composition, logging)
.github/workflows/ci.yml     CI: Linux build + tests, Windows publish + installer artifacts
build/                       publish.ps1 / publish.sh (release builds), make_icon.py
installer/                   GroceryPOS.iss (Inno Setup installer)
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
- Checkout requires an open shift (`SalesOptions.RequireOpenShift`, on by default): the POS asks the cashier to open one on entry and keeps Thanh toán disabled until then. Cash refunds for returns need one too (transfer refunds do not).
- **Trả hàng** screen: scan the invoice number, pick quantities per line (F4/Enter, +/−, Space = whole line, F7 = whole invoice), F6 marks damaged goods (refunded, not restocked), choose cash/transfer and a reason, F9 refunds and prints the refund slip (PHIẾU TRẢ HÀNG, `ReturnSlipFormatter`). State lives in the tested `Application.Sales.ReturnSheet`, whose refund and points preview equals what `ReturnService` charges.
- **Tồn kho** screen: KPI cards (stock value at cost and at sell price, active products, low/out of stock, negative stock), a stock list tab and a **Sắp hết hàng** tab (shortage to minimum, reorder suggestion and its cost). F6 **Điều chỉnh** (reason fixes the direction, type the quantity or the actual stock, live preview; state in `Application.Inventory.StockAdjustmentDraft`), F7 **Thẻ kho** (opening/in/out/closing for a period + every movement), F8 **Kiểm kê** (scan each item on the shelf or add a category / all products and type counts, Enter moves to the next uncounted line, variance value per line and in total, uncounted lines keep their stock; state in `StockCountSheet`). Needs `inventory.view`; adjusting and counting need `inventory.adjust`.
- **Nhập hàng** screen: KPI cards for the period (amount received, new supplier debt, completed receipts, open drafts), receipt list filtered by date range, status and supplier. F2 **Tạo phiếu**, F4 **Nhập hàng sắp hết** (pre-filled with the reorder suggestions), Enter opens a draft in the editor (completed/cancelled receipts open read-only), **Hủy phiếu tạm** with a reason. The editor scans barcodes (`24*mã`), F3 picks from the catalog, F4 adds low-stock items; each line shows landed cost, new moving-average cost, shelf price after and margin (below-cost lines in red, cost jumps ≥ 20% flagged); receipt discount, amount paid / **Trả đủ**, debt; Ctrl+S saves the draft, F9 posts it and shows the completion summary (cost before → after, below-cost products first). State in `Application.Purchasing.GoodsReceiptDraft`. Needs `purchases.manage`; new shelf prices need `prices.change`.
- **Công nợ NCC** screen: payables list (F6 **Thanh toán** opens the pay dialog – whole debt pre-filled, cash/transfer + reference), **Phiếu chi** tab with cancellation (reason required, the debt comes back), F7 **Sổ công nợ** (opening/increase/decrease/closing and every document with the running balance), F8 **Trả hàng NCC**: by goods receipt number (capped by what is left and by stock, price ≤ landed cost) or free to a chosen supplier; the value offsets the debt first ("Cấn trừ công nợ trước") or a typed amount comes back in money. State in `Application.Purchasing.SupplierReturnSheet`. Paying needs `suppliers.pay`, returns need `purchases.manage`.
- **Báo cáo** screen (`reports.view`): one period picker for four tabs – Doanh thu (KPIs with the trend vs. the previous period of the same length, daily chart or monthly for periods over 62 days, day table), Hàng bán chạy (rank by quantity/revenue/profit, category, top 10–100), Lợi nhuận theo danh mục and Giá trị tồn kho (current, not by period). **Xuất Excel (Ctrl+E)** saves the current tab, **Xuất tất cả** one workbook with a sheet per report (`IReportExportService` → `ReportSheets` → ClosedXML `IReportWorkbookWriter`: typed cells, VND/percent formats, filter row, totals, print-ready A4 landscape), audited as `report.exported`. **In / PDF (Ctrl+P)** opens an A4 print preview of the current tab (Ctrl+Shift+P: all tabs) – portrait/landscape, page navigation, zoom, **In**, **Chọn máy in…** (page range) and **Xuất PDF (Ctrl+S)** through Windows' built-in *Microsoft Print to PDF* printer (no extra library); pagination (scale to fit down to 72 %, wide reports split into column bands that repeat the date/name columns, repeated headers, totals placement, Trang x/y) is the tested `ReportPrintPlan`, printed or saved reports are audited as `report.printed`.
- **Nhật ký hệ thống** screen (`auditlog.view`, Admin by default): every audited action (sign-ins, failed logins and lockouts, sales, voids, returns, reprints, price changes, stock adjustments/counts, purchasing, shifts, user/role changes, exports) with period, group, severity and user filters, text search over user / document code / details, server-side paging, KPI cards (click to filter) and a detail card with "Cùng người dùng" / "Cùng đối tượng". Action names, groups and severities come from `Application.Auditing.AuditCatalog`; **Xuất Excel (Ctrl+E)** exports up to 20.000 matching rows and is itself audited (`auditlog.exported`).
- Reports work in store days (UTC+7, `Common.StoreCalendar`): a sale counts on the day it was completed, voided invoices are left out, and a customer return lowers revenue, VAT and profit on the day it was taken (restocked goods give their cost back, written-off goods do not), so a closed day's figures never change.
- Input forms are pre-validated with FluentValidation (`Application.Validation`, e.g. `CheckoutRequestValidator`, `CloseShiftRequestValidator`, `ChangePasswordInputValidator`); `ToFieldErrors()` gives per-field Vietnamese messages for dialogs and `ToResult()` plugs into the Result pattern. Services still enforce every rule themselves.
- UI colors, fonts and spacing come only from the current theme (`GroceryPOS.UI.Controls.Theming.ThemeManager.Current`); forms call `ThemeManager.Track(this)` and modern controls repaint themselves when the theme changes. Theme core files (`Theming/Core`) must stay free of WinForms types so they can be tested on Linux; the same goes for `Inputs/Core` (number/date parsing and formatting, date presets, `TextMatch`) `Grids/Core` (`GridQuery`, `GridPage<T>`, `GridDataView<T>`, `PageLinks`), `Display/Core` (`BadgePalette`, `Avatar`, `Trend`, `SurfacePalette`) `Navigation/Core` (`NavMenu`, `NavCursor`, `SidebarLayout`, `TabStripLayout`) and `Feedback/Core` (`ToastQueue`, `ToastLayout`, `DialogSpec`, `BusyTracker`, `FeedbackPalette`) and `Forms/Core` (`LoadSequencer`, `DirtyTracker`, `FieldGrid`, `ListCommand<T>`).
- Screens follow MVP (being migrated screen by screen): the WinForms control/dialog is a passive view implementing an `I…View` interface from `GroceryPOS.Presentation` (implement `IView` members explicitly, forwarding to the base-class helpers), resolves its presenter from DI (`services.AddPresentation()`, transient) and calls `presenter.Attach(this)`. Presenters hold the logic – permissions, use-case calls in a DI scope per action (`UseAsync<TService, TResult>`), messages and toasts – and never reference WinForms, so they are tested with fake views (`tests/GroceryPOS.Presentation.Tests`). Reference implementation: `CategoryListPresenter` / `CategoryListScreen` and `CategoryEditPresenter` / `CategoryEditForm`; `Partners.SupplierEditPresenter` shows the edit-dialog pattern with several fields (`FormInput.Optional` for optional text, `FormInput.FirstError` + `FocusField(propertyName)` to focus the first invalid field). Confirmations go through `IView.Confirm` (`danger: true` for a red button), information boxes through `IView.ShowInfo`, so presenters decide when to ask. Views expose extra hooks only where WinForms is needed (file pickers, the loading overlay, child dialogs) – see `IProductListView`.
- New windows derive from `BaseForm` (theme tracking, Esc to close, overlay/dialog/toast helpers); new screens from `BaseUserControl` (load once, F5 reload) or, for lists, `BaseListView<T>` (grid + permission/selection-aware toolbar commands, latest-wins reload that keeps the selected row); new add/edit dialogs from `BaseEditDialog` (declare fields with `AddField`, return an error message, an empty string (stay open silently) or null from `SaveAsync`; layout, Hủy/Lưu, Ctrl+S and the unsaved-changes prompt come for free). All master-data screens (categories, suppliers, products, customers, users) and every add/edit dialog – including role, reset-password, shift open / pay-in-out / close, stock adjustment and supplier payment – use them; categories and suppliers are the shortest reference implementations. Use `AddNote(text, NoteTone…)` for context lines and live previews (a `FormNote` whose height follows its text; the dialog re-lays out when it grows), `ModernSegmentedControl` instead of radio buttons for 2–4 exclusive choices, and override `CanSave` when the save button follows a draft's blocker.
- Inputs: amounts use `ModernCurrencyBox` (whole đồng, `₫` suffix, `25k` = 25.000, ↑/↓ ±1.000), other numbers `ModernNumericBox` (`Format = NumericFormat.Quantity/Percent/…`), dates `ModernDatePicker` (dd/MM/yyyy; also accepts `19/10`, `191026`, `hôm nay`, `-7`; ↑/↓ ±1 day, F4 calendar), lists `ModernComboBox` (`DataSource` + `DisplayMember`/`ValueMember`; `DropDownStyle = DropDown` filters as you type without accents). Report filters use `ModernDateRangePicker` (◀ preset ▾ from – to ▶; `Initialize(DatePreset.ThisMonth)`, `RangeChanged`, `Describe()` for subtitles; Alt+←/→ step whole weeks/months/years; never past today) built on the tested `DateRangeSelection` and `DatePresets` (Hôm nay, Tuần này, Tháng trước…; weeks start on Monday).
- Lists: use `ModernDataGrid`. Add columns with `AddTextColumn("Name", "Tên hàng")` (width 0 = fill), `AddMoneyColumn`, `AddNumberColumn`, `AddDateColumn`, `AddCheckColumn` (dotted paths like `Category.Name` work), put screen buttons in `ToolbarItems`, then either `SetItems(rows)` for in-memory search/sort/paging or set `DataProvider = (query, ct) => …` to return a `GridPage<object>` per `GridQuery` for large tables. Handle `RowActivated` to open the edit dialog.
- Shell: `SidebarNav` takes a `NavMenu` of `NavSection`s/`NavItem`s (key, Vietnamese text, glyph, required permissions – any one is enough, optional shortcut hint); set `PermissionFilter = currentUser.HasPermission` after sign-in so items the user may not open are hidden, handle `ItemSelected` to open the screen, `SetBadge("inventory", lowStockCount, BadgeKind.Warning)` for counts. `TopBar.SetPage(title, breadcrumb)` and `SetUser(fullName, roleName, username)`; give it a `UserMenu` (Đổi mật khẩu, Đăng xuất) and put screen buttons in `Actions`. Its theme toggle calls `ThemeManager.ToggleMode()` and raises `ThemeToggled` so the shell can save the choice.
- Surfaces: `ModernCard` (title/subtitle/icon header, children docked below it) for grouped content, `StatCard` for dashboard KPIs (`Value = Money.Format(x)`, `SetTrend(dto.RevenueChangePercent, "hôm qua")`, `HigherIsBetter = false` for refunds), `ModernBadge.Set("Hết hàng", BadgeKind.Danger)` for statuses, `ModernTabControl` with `ModernTabPage`s (`BadgeCount`, cancelable `Selecting` for unsaved edits), `BarChart` for column charts (`Points = [new ChartPoint("T2 26/10", revenue, tooltip)]`, `HighlightIndex`, `ShowValues`, compact VND axis via `ChartScale.CompactVnd`: 850k / 1,2tr / 2,5 tỷ).
- Feedback: never call `MessageBox.Show` in the app – use `ModernDialog` (same overloads as `MessageBox.Show`, plus `Info/Success/Warning/Error(owner, message, title, details)` and `Confirm(owner, message, title, "Xóa vai trò", "Giữ lại", danger: true)`; Esc gives the safe answer, a danger confirm defaults to it, letter keys press buttons, Ctrl+C copies, the owner window is dimmed). Results that need no answer go to a toast: `Toaster.Success(this, "Đã lưu sản phẩm SUA01.")`, or `Toaster.Show(this, new ToastRequest(FeedbackKind.Success, text, title, ActionText: "Mở file"), onAction)`; identical toasts merge into one with ×n, hovering pauses them. Wrap slow work in `await LoadingOverlay.RunAsync(this, () => service.ExportAsync(…), "Đang tạo file Excel…")` (or `using (LoadingOverlay.Begin(panel, …))`) – it appears only after 250 ms and blocks input to the covered controls. Generated Excel files are saved through `ExcelFiles.SaveAsync` (save dialog, locked-file warning, "Mở file" toast).
- Master data codes: customers get `KH000001`, suppliers `NCC0001` (or a typed code) via `Common.SequentialCode`; codes never change after creation. A customer's phone is unique (it is the member lookup at the counter). A supplier with open debt cannot be set to "ngừng giao dịch".
- POS counter (**Bán hàng**): all state lives in the UI-free `Application.Sales.PosCart` (lines, discounts, member + points, tender, quick-cash suggestions, `BuildRequest()`); the screen only renders it. Keys: F2 quét mã · F3 tìm sản phẩm · F4 số lượng · F5 khách hàng (SĐT) · F6 giảm giá dòng · F7 giảm giá hóa đơn · F8 khách đưa · F9 thanh toán · +/− số lượng · Del xóa dòng · Ctrl+P in lại hóa đơn · Esc hủy đơn. Typing `3*<mã>` sells 3 units (`1,5*GAO01` for weighed goods).
- POS extras: **F10 Lưu tạm** parks the cart under a name (member name or "Đơn tạm n" by default) so the next customer can be served; **F11 Đơn chờ** lists parked orders (oldest first, forgotten ones > 12 h in amber), resumes one with today's prices and the member's current points (price changes, discontinued products and lost discounts are reported) and parks a cart in progress first; Del discards. **F12 Kiểm giá** shows price, VAT and stock of a scanned item without selling it (Ctrl+Enter adds it). **Màn hình khách** opens a customer-facing window – full screen on a second monitor – with the last item, newest lines, savings, member points and the amount due, then "Cảm ơn quý khách" with the change after payment (`CustomerDisplayState`).
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

First start creates `grocerypos.db` (SQLite) in the data folder (below) and the default account **`admin` / `admin123`**, which must be changed at the first sign-in.

## Data folder

The program folder is treated as read-only (it may be `C:\Program Files\GroceryPOS`, and an upgrade replaces it). Everything the app writes goes to one **data folder** resolved by `Infrastructure.Hosting.AppDataLocation`:

| Priority | When | Data folder |
|---|---|---|
| 1 | `portable.txt` next to `GroceryPOS.exe` | `data\` beside the exe (USB stick, no install) |
| 2 | environment variable `GROCERYPOS_DATA` | that folder |
| 3 | `Paths:DataDirectory` in appsettings.json | that folder (relative = from the program folder) |
| 4 | default | `%LocalAppData%\GroceryPOS` |

It holds `grocerypos.db` (a relative SQLite `Data Source` is resolved here), `logs\`, `Backups\`, `parked-orders.json`, `ui-preferences.json` and the optional **`appsettings.local.json`**, which is layered over the shipped appsettings.json – put the shop's own changes there (SQL Server connection string, backup folder, log level…) so a new version never resets them. Explicit absolute paths in appsettings still win. A database left next to the exe by earlier versions is copied into the data folder once at start-up (the old file is kept).

Only one copy of the app runs per data folder; starting it again brings the open window to the front. **Thông tin phần mềm** (user menu, top right) shows the version and every path, opens the data / log folders and copies the details for support (SQL Server passwords masked).

## Publishing & installer

```powershell
./build/publish.ps1                      # tests → self-contained publish → portable zip → installer
./build/publish.ps1 -FrameworkDependent  # smaller build for PCs with the .NET Desktop Runtime 8.0 (x64)
```

```bash
./build/publish.sh                       # Linux/macOS CI: tests, win-x64 publish, portable zip (no installer)
```

- Publish profiles in `src/GroceryPOS.App/Properties/PublishProfiles`: **`win-x64`** – one self-contained `GroceryPOS.exe` (~75 MB, runtime and native SQLite inside, nothing to install) plus `appsettings.json`; **`win-x64-framework`** – the same without the runtime (~27 MB). Release builds embed debug symbols so logged stack traces keep line numbers.
- `installer/GroceryPOS.iss` (Inno Setup 6.3+, `winget install JRSoftware.InnoSetup`) → `artifacts/installer/GroceryPOS-Setup-<version>.exe`: installs to Program Files (or per user without admin rights), Start menu + optional desktop shortcut, asks to close a running GroceryPOS before upgrading (`AppMutex`), checks for the .NET Desktop Runtime in the framework-dependent variant, and never removes the data folder on uninstall. Uses Vietnamese wizard texts when `Vietnamese.isl` is installed in Inno's Languages folder.
- `artifacts/GroceryPOS-<version>-portable.zip` – the self-contained build with `portable.txt`, for running from a USB stick.
- **CI** (`.github/workflows/ci.yml`, GitHub Actions): every push/PR to `main` builds and tests on Linux, then runs `build/publish.ps1` on `windows-latest` (tests, self-contained publish, portable zip, Inno Setup installed via Chocolatey) and uploads **GroceryPOS-Setup** and **GroceryPOS-portable** as workflow artifacts; test results (`.trx`) are uploaded too.
- The version comes from `<Version>` in `Directory.Build.props`; the icon is `src/GroceryPOS.App/app.ico` (`build/make_icon.py`).

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

Files go to `logs` in the data folder by default (a relative `Directory` is taken from the data folder): `grocerypos-yyyyMMdd.log`
(daily, rolls over by size, 30 days) and `errors-yyyyMMdd.log` (Error/Fatal only, 90 days). Every line carries the signed-in
user, e.g. `[INF] (admin) …AuthService: User admin signed in as Admin`. Business actions are additionally kept in the
database audit log (`IAuditService`).

## Store & receipt settings

Parked carts are counter-local working state, kept in a JSON file per machine (`ParkedOrders:FilePath`, default `parked-orders.json` in the data folder; writes are atomic and an unreadable file is set aside as `.bad`).

The `Store` section of appsettings (`StoreProfile`) holds the **defaults** for what is printed on receipts: `Name`, `Address`, `Phone`, `TaxCode` (MST, optional), `ReceiptFooter` (`\n` for several lines), `ReceiptPaperWidthMm` (80 or 58) and `PreviewReceiptAfterCheckout`.

The **Cấu hình** screen (`settings.manage`) edits the store info, receipt footer/paper/preview, loyalty policy (VND per point, value of a point; 0 turns either off) and sales rules (return window, negative stock, mandatory shift, cash variance that needs a note). Values are stored in the `AppSettings` table (key/value, shared by every counter on the same database) on top of the appsettings defaults; `Application.Settings.StoreSettingsState` holds the values in force and the container resolves `StoreProfile` / `SalesOptions` from it per scope, so screens opened after a save use the new values. Saves are validated (`StoreSettingsValidator`), only run with changes, and audited as `settings.changed` with every old → new value. The light/dark theme is a per-machine preference in `ui-preferences.json` in the data folder.

## Backup & restore

The **Sao lưu dữ liệu** screen (`system.backup`, Admin only by default) lists the backups in the backup folder with a freshness badge, checks the selected file (integrity, schema version, number of products / customers / invoices, last invoice) and offers **Sao lưu ngay** (Ctrl+S), **Phục hồi** (from the list or any file, e.g. a USB stick), **Sao chép ra…** (copy to USB / another drive), **Xóa** and **Mở thư mục**.

- Configured in the `Backup` section of appsettings (`BackupOptions`): `Folder` (empty = `Backups` in the data folder), `AutomaticIntervalHours` (default 24: a backup is made at start-up when the newest one is older; 0 = off), `KeepAutomatic` (14) and `KeepBeforeRestore` (5). Manual backups are never pruned.
- Files are named `grocerypos-yyyyMMdd-HHmmss-{manual|auto|prerestore}.db`. SQLite uses the online backup API (a consistent copy while the app runs, written to `.tmp` first); SQL Server uses `BACKUP DATABASE … WITH COPY_ONLY` to a `.bak` path on the server and is restored with SQL Server Management Studio.
- A restore refuses unreadable files and backups made by a newer version, saves the current data as a "Trước khi phục hồi" copy, replaces the database, applies pending migrations (older backups are upgraded), writes `backup.restored` to the audit log of the restored data and restarts the app. Creating and deleting backups are audited as `backup.created` / `backup.deleted`.

## Default account

On a new database the initializer creates `admin` / `admin123` (role Admin) and forces a password change on first login.
Override it in the `Seed` section of appsettings (`AdminUsername`, `AdminPassword`, `AdminFullName`, `DefaultCategoryName`).

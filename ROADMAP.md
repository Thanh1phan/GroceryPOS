# GroceryPOS – Roadmap

Checklist of milestones. Each daily run picks the next unchecked items.

## M1 – Foundation
- [x] Solution layout, `Directory.Build.props`, `.editorconfig`, `.gitignore`
- [x] Domain: base entities (`Entity`, `AuditableEntity`), `DomainException`, guards
- [x] Domain: identity (User with lockout, Role, permission catalog, system roles)
- [x] Domain: catalog (Category, Product with barcode/VAT/price/stock rules)
- [x] Domain: partners (Supplier, Customer + loyalty points, VN phone normalization)
- [x] Domain: audit log entity + action keys
- [x] Application: Result pattern, repository/UoW/current-user/password-hasher/audit abstractions
- [x] Application: VND formatting/parsing (vi-VN)
- [x] Test projects for Domain and Application (xUnit-compatible, dependency-free harness in `tests/GroceryPOS.Testing`)
- [ ] Switch test harness to real xUnit packages – *blocked: nuget.org unreachable from the build sandbox (still blocked 2026-10-13)*
- [ ] Infrastructure: EF Core 8 DbContext (SQLite default, SQL Server switchable), configurations, migrations
- [ ] Infrastructure: repositories, UnitOfWork, audit service, seed data (roles + default admin)
- [ ] Infrastructure: BCrypt password hasher, Serilog (file, rolling)
- [ ] FluentValidation validators for application DTOs

## M2 – Authentication & authorization
- [x] AuthService: login, lockout after failed attempts, change password, logout (application layer)
- [ ] Login form, change-password dialog, session in app shell
- [ ] Permission-aware menus/buttons
- [ ] User management screen (create, edit, reset password, unlock, activate/deactivate)
- [ ] Role management screen (permission matrix)
- [ ] Global exception handling + Serilog wiring in WinForms host

## M3 – UI component library (GroceryPOS.UI.Controls)
- [ ] ThemeManager with color/font/spacing tokens, light/dark themes
- [ ] Rounded buttons (primary/secondary/danger), icon text box with placeholder & validation state
- [ ] Combo box, numeric/currency input, date picker
- [ ] Modern grid wrapper with paging/search/sorting
- [ ] Card/panel, sidebar navigation, top bar, tab control, badge
- [ ] Toast notifications, modal dialog, loading overlay
- [ ] BaseForm / BaseUserControl / BaseListView / BaseEditDialog

## M4 – Master data
- [ ] Categories CRUD
- [ ] Products CRUD (barcode, unit, prices, VAT, min stock) with price-change audit
- [ ] Suppliers CRUD
- [ ] Customers CRUD + loyalty points history

## M5 – Point of sale
- [x] Sale/SaleLine domain (discounts per line and per invoice, VAT split, payments)
- [x] Checkout use case (invoice numbering, stock deduction, loyalty earn/redeem, audit) + `ISaleRepository`
- [x] Void sale use case (restock, loyalty reversal, audit) and POS lookup (barcode/code scan, quick search, member by phone)
- [ ] POS screen: barcode scan, quick search, cart, discounts, cash/transfer, change calculation
- [x] Loyalty earn/redeem at checkout (application layer; POS UI pending)
- [ ] Receipt print preview (80mm)
- [x] Customer returns (phiếu trả `TH{yyMMdd}-{0001}`): partial/multiple returns per invoice, discount-aware refunds, restock or write-off, proportional loyalty reversal, return window (7 days; older needs `sales.void`), audit (application layer; UI pending)
- [ ] Returns screen: scan invoice, pick quantities, restock/write-off toggle, refund slip
- [x] Shifts (ca `CA{yyMMdd}-{0001}`): open with float, pay-in/pay-out with reason, live X-report, close with count by denomination, variance (note required ≥ 5.000 ₫), cash left for next shift, supervisor view/force-close (`shifts.supervise`), audit (application layer; UI pending)
- [ ] Shift screens: open-shift dialog (pre-filled float), pay-in/out dialog, close-shift count sheet + printable shift report; POS requires an open shift

## M6 – Inventory & purchasing
- [x] Goods receipt (phiếu nhập `PN{yyMMdd}-{0001}`): draft → complete/cancel, line + receipt discounts, moving weighted-average cost, optional new sell price, supplier debt, ledger + audit (application layer; UI pending)
- [x] Reorder suggestions from low-stock products (refill to 2× minimum)
- [ ] Supplier payments (trả nợ NCC) and supplier returns (trả hàng NCC)
- [ ] Goods receipt screens: receipt list, receipt editor with barcode scan, completion summary (below-cost warnings)
- [x] Stock adjustments with reason + audit, and stock counts (kiểm kê) with variance value (application layer; UI pending)
- [x] Stock movement ledger (`StockMovement`, written by checkout/void/adjust/count) + stock card query with opening/closing balance
- [x] Low-stock alerts query (out-of-stock first, shortage to minimum; UI pending)
- [ ] Inventory screens: adjustment dialog, stock count sheet, stock card, low-stock list

## M7 – Reports & dashboard
- [ ] Daily revenue, best sellers, inventory value, profit
- [ ] Dashboard (today's sales, low-stock, top products)
- [ ] Audit log viewer

## M8 – Polish
- [ ] Settings screen (store info, receipt footer, loyalty policy, theme)
- [ ] Database backup/restore
- [ ] Installer/publish profile

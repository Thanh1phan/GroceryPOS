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
- [ ] Switch test harness to real xUnit packages – *blocked: nuget.org unreachable from the build sandbox (still blocked 2026-10-09)*
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
- [ ] Returns
- [ ] Shifts: open/close, cash drawer count & variance

## M6 – Inventory & purchasing
- [ ] Purchase orders & goods receipt (updates cost price & stock)
- [ ] Stock adjustments with reason + audit
- [ ] Stock movement ledger
- [ ] Low-stock alerts

## M7 – Reports & dashboard
- [ ] Daily revenue, best sellers, inventory value, profit
- [ ] Dashboard (today's sales, low-stock, top products)
- [ ] Audit log viewer

## M8 – Polish
- [ ] Settings screen (store info, receipt footer, loyalty policy, theme)
- [ ] Database backup/restore
- [ ] Installer/publish profile

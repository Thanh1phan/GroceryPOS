# GroceryPOS

Phần mềm bán hàng & quản lý cửa hàng tạp hóa (.NET 8 WinForms).
Point-of-sale and store management app for a Vietnamese grocery store.

> Status: early development – see [ROADMAP.md](ROADMAP.md) and [CHANGELOG.md](CHANGELOG.md).

## Architecture (Clean Architecture)

```
src/
  GroceryPOS.Domain          Entities, value objects, domain rules (no dependencies)
  GroceryPOS.Application     Use cases, DTOs, interfaces, Result pattern
  GroceryPOS.Infrastructure  EF Core 8 (SQLite / SQL Server), repositories, BCrypt, Serilog   (planned)
  GroceryPOS.UI.Controls     Reusable themed WinForms component library                       (planned)
  GroceryPOS.WinForms        App shell, MVP views & presenters, DI host                        (planned)
tests/                       xUnit tests for Domain and Application                            (planned)
```

Conventions:
- UI text in Vietnamese; code, identifiers and comments in English.
- Currency is VND, formatted with `vi-VN` (`125.000 ₫`) via `GroceryPOS.Application.Formatting.Vnd`.
- Use cases return `Result` / `Result<T>` for expected failures; `DomainException` signals broken invariants.
- Authorization is permission-based (`Domain.Identity.Permissions`); built-in roles: Admin, Quản lý, Thu ngân, Thủ kho.

## Build & run

Requirements: .NET 8 SDK (the WinForms app will require Windows to run).

```bash
dotnet build GroceryPOS.sln
```

## Default account

Seeding will create `admin` / `admin123` (must be changed on first login) once the Infrastructure layer lands.

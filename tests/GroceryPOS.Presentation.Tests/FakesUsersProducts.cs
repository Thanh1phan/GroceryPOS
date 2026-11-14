using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakeUserManagementService : IUserManagementService
{
    public List<UserDto> Items { get; } = new();
    public List<RoleOptionDto> Roles { get; } = new();
    public Error? FailWith { get; set; }
    public List<string> Calls { get; } = new();
    public CreateUserRequest? LastCreate { get; private set; }
    public UpdateUserRequest? LastUpdate { get; private set; }

    public Task<Result<IReadOnlyList<UserDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("list");
        return Task.FromResult(FailWith is { } e ? Result.Failure<IReadOnlyList<UserDto>>(e) : Result.Success<IReadOnlyList<UserDto>>(Items.ToList()));
    }

    public Task<Result<IReadOnlyList<RoleOptionDto>>> ListRoleOptionsAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("roles");
        return Task.FromResult(FailWith is { } e ? Result.Failure<IReadOnlyList<RoleOptionDto>>(e) : Result.Success<IReadOnlyList<RoleOptionDto>>(Roles.ToList()));
    }

    public Task<Result<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"create:{request.Username}");
        LastCreate = request;
        return Task.FromResult(FailWith is { } e
            ? Result.Failure<UserDto>(e)
            : Result.Success(new UserDto(99, request.Username!, request.FullName!, request.RoleId, "Thu ngân", true, false, null, true, null, false)));
    }

    public Task<Result<UserDto>> UpdateAsync(int userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"update:{userId}:{request.FullName}:{request.RoleId}");
        LastUpdate = request;
        return Task.FromResult(FailWith is { } e
            ? Result.Failure<UserDto>(e)
            : Result.Success(Items.Single(u => u.Id == userId) with { FullName = request.FullName!, RoleId = request.RoleId }));
    }

    public Task<Result> ResetPasswordAsync(int userId, string newPassword, CancellationToken cancellationToken = default) =>
        Run($"reset:{userId}:{newPassword}");

    public Task<Result> UnlockAsync(int userId, CancellationToken cancellationToken = default) => Run($"unlock:{userId}");

    public Task<Result> DeactivateAsync(int userId, CancellationToken cancellationToken = default) => Run($"deactivate:{userId}");

    public Task<Result> ActivateAsync(int userId, CancellationToken cancellationToken = default) => Run($"activate:{userId}");

    private Task<Result> Run(string call)
    {
        Calls.Add(call);
        return Task.FromResult(FailWith is { } e ? Result.Failure(e) : Result.Success());
    }
}

/// <summary>Products and their Excel import/export in one fake (both work on the same catalog).</summary>
internal sealed class FakeProductService : IProductService, IProductImportExportAppService
{
    public List<ProductListItem> Items { get; } = new();
    public Dictionary<int, ProductDetail> Details { get; } = new();
    public Error? FailWith { get; set; }

    /// <summary>Only <see cref="GetAsync"/> fails (the product was removed elsewhere).</summary>
    public Error? GetFailsWith { get; set; }

    /// <summary>Only <see cref="SetActiveAsync"/> fails.</summary>
    public Error? SetActiveFailsWith { get; set; }

    public List<string> Calls { get; } = new();
    public SaveProductRequest? LastRequest { get; private set; }
    public byte[] Export { get; set; } = { 1, 2, 3 };
    public ImportSummary Import { get; set; } = new(2, 1, 0, Array.Empty<ImportRowResult>());
    public string? ImportedText { get; private set; }

    public Task<Result<IReadOnlyList<ProductListItem>>> ListAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("list");
        return Task.FromResult(FailWith is { } e ? Result.Failure<IReadOnlyList<ProductListItem>>(e) : Result.Success<IReadOnlyList<ProductListItem>>(Items.ToList()));
    }

    public Task<Result<ProductDetail>> GetAsync(int productId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"get:{productId}");
        return Task.FromResult(GetFailsWith is { } e ? Result.Failure<ProductDetail>(e) : Result.Success(Details[productId]));
    }

    public Task<Result<ProductDetail>> CreateAsync(SaveProductRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"create:{request.Code}");
        LastRequest = request;
        return Task.FromResult(FailWith is { } e ? Result.Failure<ProductDetail>(e) : Result.Success(ToDetail(99, request, isActive: true)));
    }

    public Task<Result<ProductDetail>> UpdateAsync(int productId, SaveProductRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"update:{productId}");
        LastRequest = request;
        var active = Details.TryGetValue(productId, out var d) && d.IsActive;
        return Task.FromResult(FailWith is { } e ? Result.Failure<ProductDetail>(e) : Result.Success(ToDetail(productId, request, active)));
    }

    public Task<Result> SetActiveAsync(int productId, bool isActive, CancellationToken cancellationToken = default)
    {
        Calls.Add($"active:{productId}:{isActive}");
        var error = SetActiveFailsWith ?? FailWith;
        return Task.FromResult(error is { } e ? Result.Failure(e) : Result.Success());
    }

    public Task<Result<byte[]>> ExportAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("export");
        return Task.FromResult(FailWith is { } e ? Result.Failure<byte[]>(e) : Result.Success(Export));
    }

    public async Task<Result<ImportSummary>> ImportAsync(Stream xlsxFile, CancellationToken cancellationToken = default)
    {
        Calls.Add("import");
        using var reader = new StreamReader(xlsxFile);
        ImportedText = await reader.ReadToEndAsync(cancellationToken);
        return FailWith is { } e ? Result.Failure<ImportSummary>(e) : Result.Success(Import);
    }

    private static ProductDetail ToDetail(int id, SaveProductRequest r, bool isActive) =>
        new(id, r.Code, r.Barcode, r.Name, r.CategoryId, "Sữa", r.Unit, r.CostPrice, r.SellPrice, r.VatRate, 0, r.MinStockLevel, isActive);
}

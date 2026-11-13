using FluentValidation;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Validation;
using GroceryPOS.Presentation;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakeCurrentUser(params string[] permissions) : ICurrentUser
{
    public UserSession? Session => null;

    public bool HasPermission(string permission) => permissions.Contains(permission);
}

/// <summary>Records what a presenter asked its view to show.</summary>
internal abstract class RecordingView : IView
{
    public List<string> Warnings { get; } = new();
    public List<string> Successes { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Questions { get; } = new();
    public bool IsAlive { get; set; } = true;

    /// <summary>What the user answers to <see cref="Confirm"/>.</summary>
    public bool ConfirmAnswer { get; set; } = true;

    public void ShowWarning(string message, string title) => Warnings.Add(message);

    public bool Confirm(string message, string title, string confirmText)
    {
        Questions.Add(message);
        return ConfirmAnswer;
    }

    public void ToastSuccess(string message, string? title = null) => Successes.Add(message);

    public void ToastError(string message, string? title = null) => Errors.Add(message);
}

internal sealed class FakeCategoryService : ICategoryService
{
    public List<CategoryDto> Items { get; } = new();
    public Error? FailWith { get; set; }
    public List<string> Calls { get; } = new();
    public int ScopesSeen { get; set; }

    public Task<Result<IReadOnlyList<CategoryDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("list");
        return Task.FromResult(FailWith is { } e ? Result.Failure<IReadOnlyList<CategoryDto>>(e) : Result.Success<IReadOnlyList<CategoryDto>>(Items.ToList()));
    }

    public Task<Result<CategoryDto>> CreateAsync(SaveCategoryRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"create:{request.Name}|{request.Description ?? "<null>"}");
        return Task.FromResult(FailWith is { } e ? Result.Failure<CategoryDto>(e) : Result.Success(new CategoryDto(99, request.Name, request.Description, true)));
    }

    public Task<Result<CategoryDto>> UpdateAsync(int categoryId, SaveCategoryRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"update:{categoryId}:{request.Name}|{request.Description ?? "<null>"}");
        return Task.FromResult(FailWith is { } e ? Result.Failure<CategoryDto>(e) : Result.Success(new CategoryDto(categoryId, request.Name, request.Description, true)));
    }

    public Task<Result> DeactivateAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"deactivate:{categoryId}");
        return Task.FromResult(FailWith is { } e ? Result.Failure(e) : Result.Success());
    }

    public Task<Result> ActivateAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"activate:{categoryId}");
        return Task.FromResult(FailWith is { } e ? Result.Failure(e) : Result.Success());
    }
}

internal sealed class FakeSupplierService : ISupplierService
{
    public List<SupplierDto> Items { get; } = new();
    public Error? FailWith { get; set; }
    public string NextCode { get; set; } = "NCC0007";
    public List<string> Calls { get; } = new();
    public SaveSupplierRequest? LastRequest { get; private set; }

    public Task<Result<IReadOnlyList<SupplierDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("list");
        return Task.FromResult(FailWith is { } e ? Result.Failure<IReadOnlyList<SupplierDto>>(e) : Result.Success<IReadOnlyList<SupplierDto>>(Items.ToList()));
    }

    public Task<Result<SupplierDto>> GetAsync(int supplierId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(Items.Single(s => s.Id == supplierId)));

    public Task<Result<string>> SuggestCodeAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("suggest");
        return Task.FromResult(FailWith is { } e ? Result.Failure<string>(e) : Result.Success(NextCode));
    }

    public Task<Result<SupplierDto>> CreateAsync(SaveSupplierRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"create:{request.Name}");
        LastRequest = request;
        return Task.FromResult(FailWith is { } e ? Result.Failure<SupplierDto>(e) : Result.Success(ToDto(99, request)));
    }

    public Task<Result<SupplierDto>> UpdateAsync(int supplierId, SaveSupplierRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"update:{supplierId}:{request.Name}");
        LastRequest = request;
        return Task.FromResult(FailWith is { } e ? Result.Failure<SupplierDto>(e) : Result.Success(ToDto(supplierId, request)));
    }

    public Task<Result> DeactivateAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"deactivate:{supplierId}");
        return Task.FromResult(FailWith is { } e ? Result.Failure(e) : Result.Success());
    }

    public Task<Result> ActivateAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"activate:{supplierId}");
        return Task.FromResult(FailWith is { } e ? Result.Failure(e) : Result.Success());
    }

    private static SupplierDto ToDto(int id, SaveSupplierRequest r) =>
        new(id, r.Code ?? "NCC0099", r.Name, r.ContactPerson, r.Phone, r.Email, r.Address, r.TaxCode, 0, true);
}

internal sealed class FakeCustomerService : ICustomerService
{
    public List<CustomerDto> Items { get; } = new();
    public Error? FailWith { get; set; }
    public List<string> Calls { get; } = new();
    public SaveCustomerRequest? LastRequest { get; private set; }

    public Task<Result<IReadOnlyList<CustomerDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("list");
        return Task.FromResult(FailWith is { } e ? Result.Failure<IReadOnlyList<CustomerDto>>(e) : Result.Success<IReadOnlyList<CustomerDto>>(Items.ToList()));
    }

    public Task<Result<CustomerDto>> GetAsync(int customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(Items.Single(c => c.Id == customerId)));

    public Task<Result<CustomerDto>> CreateAsync(SaveCustomerRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"create:{request.Name}");
        LastRequest = request;
        return Task.FromResult(FailWith is { } e ? Result.Failure<CustomerDto>(e) : Result.Success(ToDto(99, request)));
    }

    public Task<Result<CustomerDto>> UpdateAsync(int customerId, SaveCustomerRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"update:{customerId}:{request.Name}");
        LastRequest = request;
        return Task.FromResult(FailWith is { } e ? Result.Failure<CustomerDto>(e) : Result.Success(ToDto(customerId, request)));
    }

    public Task<Result> DeactivateAsync(int customerId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"deactivate:{customerId}");
        return Task.FromResult(FailWith is { } e ? Result.Failure(e) : Result.Success());
    }

    public Task<Result> ActivateAsync(int customerId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"activate:{customerId}");
        return Task.FromResult(FailWith is { } e ? Result.Failure(e) : Result.Success());
    }

    public Task<Result<CustomerLoyaltyHistoryDto>> GetLoyaltyHistoryAsync(int customerId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The loyalty history dialog loads its own data.");

    private static CustomerDto ToDto(int id, SaveCustomerRequest r) =>
        new(id, "KH000099", r.Name, r.Phone, r.Email, r.Address, 0, 0, true);
}

/// <summary>A real DI container with the presenters and the app's validators, plus fake services.</summary>
internal sealed class TestHost : IDisposable
{
    public TestHost(params string[] permissions)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser(permissions));
        services.AddSingleton(Categories);
        services.AddScoped<ICategoryService>(sp => sp.GetRequiredService<FakeCategoryService>());
        services.AddTransient<IValidator<SaveCategoryRequest>, SaveCategoryRequestValidator>();
        services.AddSingleton(Suppliers);
        services.AddScoped<ISupplierService>(sp => sp.GetRequiredService<FakeSupplierService>());
        services.AddTransient<IValidator<SaveSupplierRequest>, SaveSupplierRequestValidator>();
        services.AddSingleton(Customers);
        services.AddScoped<ICustomerService>(sp => sp.GetRequiredService<FakeCustomerService>());
        services.AddTransient<IValidator<SaveCustomerRequest>, SaveCustomerRequestValidator>();
        services.AddPresentation();
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public FakeCategoryService Categories { get; } = new();

    public FakeSupplierService Suppliers { get; } = new();

    public FakeCustomerService Customers { get; } = new();

    public ServiceProvider Provider { get; }

    public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

    public void Dispose() => Provider.Dispose();
}

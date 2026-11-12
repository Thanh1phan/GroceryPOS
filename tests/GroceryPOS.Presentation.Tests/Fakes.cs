using FluentValidation;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
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
    public bool IsAlive { get; set; } = true;

    public void ShowWarning(string message, string title) => Warnings.Add(message);

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
        services.AddPresentation();
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public FakeCategoryService Categories { get; } = new();

    public ServiceProvider Provider { get; }

    public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

    public void Dispose() => Provider.Dispose();
}

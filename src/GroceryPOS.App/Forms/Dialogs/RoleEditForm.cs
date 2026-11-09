using FluentValidation;
using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Users;
using GroceryPOS.Application.Validation;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Inputs;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Add/edit dialog for a role's name and description. A new role can start from another role's permissions
/// ("Sao chép quyền từ"); permissions themselves are edited in the matrix. System role names are read-only.
/// </summary>
internal sealed class RoleEditForm : BaseEditDialog
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RoleDto? _role;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _description;
    private readonly ModernComboBox? _copyFrom;

    public RoleEditForm(IServiceScopeFactory scopeFactory, RoleDto? role, IReadOnlyList<RoleDto> roles)
        : base(role is null ? "Thêm vai trò" : "Sửa vai trò", contentWidth: 396)
    {
        _scopeFactory = scopeFactory;
        _role = role;

        _name = AddField("Tên vai trò", new ModernTextBox
        {
            Text = role?.Name ?? string.Empty,
            ReadOnly = role is { IsSystem: true },
            PlaceholderText = "vd: Kế toán",
            HelperText = role is { IsSystem: true } ? "Vai trò hệ thống – không đổi được tên." : null,
        });
        _description = AddField("Mô tả", new ModernTextBox { Text = role?.Description ?? string.Empty, PlaceholderText = "Không bắt buộc" });

        if (role is null)
        {
            var sources = new List<RoleSource> { new(0, "(Không – bắt đầu trống)", Array.Empty<string>()) };
            sources.AddRange(roles.Select(r => new RoleSource(r.Id, r.Name, r.Permissions)));
            _copyFrom = AddField("Sao chép quyền từ", new ModernComboBox
            {
                DataSource = sources,
                DisplayMember = nameof(RoleSource.Name),
                ValueMember = nameof(RoleSource.Id),
                HelperText = "Quyền chi tiết chỉnh trong bảng phân quyền sau khi tạo.",
            });
        }
    }

    protected override async Task<string?> SaveAsync()
    {
        var description = string.IsNullOrWhiteSpace(_description.Text) ? null : _description.Text.Trim();
        var permissions = _role?.Permissions.ToList()
                          ?? (_copyFrom?.SelectedItem as RoleSource)?.Permissions.ToList()
                          ?? new List<string>();
        var request = new SaveRoleRequest(_name.Text.Trim(), description, permissions);

        using var scope = _scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IValidator<SaveRoleRequest>>().Check(request) is { IsFailure: true } invalid)
        {
            return invalid.ErrorMessageOrNull();
        }

        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var result = _role is null ? await roles.CreateAsync(request) : await roles.UpdateAsync(_role.Id, request);
        return result.ErrorMessageOrNull();
    }

    private sealed record RoleSource(int Id, string Name, IReadOnlyCollection<string> Permissions);
}

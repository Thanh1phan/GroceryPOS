using FluentValidation;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.Application.Validation;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Add/edit dialog for a role's name and description. A new role can start from another role's permissions
/// ("Sao chép quyền từ"); permissions themselves are edited in the matrix. System role names are read-only.
/// </summary>
internal sealed class RoleEditForm : ModernDialogForm
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RoleDto? _role;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _description;
    private readonly ModernComboBox? _copyFrom;
    private readonly ModernButton _save;

    public RoleEditForm(IServiceScopeFactory scopeFactory, RoleDto? role, IReadOnlyList<RoleDto> roles)
    {
        _scopeFactory = scopeFactory;
        _role = role;

        Text = role is null ? "Thêm vai trò" : "Sửa vai trò";
        ClientSize = new Size(460, role is null ? 340 : 270);

        const int left = 32;
        const int width = 396;

        Label Caption(string text, int y) => new() { Text = text, AutoSize = true, Location = new Point(left, y) };

        _name = new ModernTextBox
        {
            Location = new Point(left, 40),
            Width = width,
            Text = role?.Name ?? string.Empty,
            ReadOnly = role is { IsSystem: true },
            PlaceholderText = "vd: Kế toán",
            HelperText = role is { IsSystem: true } ? "Vai trò hệ thống – không đổi được tên." : null,
        };
        _description = new ModernTextBox { Location = new Point(left, 120), Width = width, Text = role?.Description ?? string.Empty, PlaceholderText = "Không bắt buộc" };
        Controls.AddRange([Caption("Tên vai trò", 16), _name, Caption("Mô tả", 96), _description]);

        var bottom = 190;
        if (role is null)
        {
            var sources = new List<RoleSource> { new(0, "(Không – bắt đầu trống)", Array.Empty<string>()) };
            sources.AddRange(roles.Select(r => new RoleSource(r.Id, r.Name, r.Permissions)));
            _copyFrom = new ModernComboBox
            {
                Location = new Point(left, 200),
                Width = width,
                DataSource = sources,
                DisplayMember = nameof(RoleSource.Name),
                ValueMember = nameof(RoleSource.Id),
            };
            Controls.AddRange([Caption("Sao chép quyền từ", 176), _copyFrom]);
            bottom = 260;
        }

        ErrorLabel.Location = new Point(left, bottom);
        ErrorLabel.Size = new Size(220, 64);

        _save = new ModernButton
        {
            Text = "Lưu",
            Variant = ButtonVariant.Primary,
            Location = new Point(left + width - 160, bottom + 8),
            Size = new Size(160, 40),
        };
        _save.Click += async (_, _) => await SaveAsync();
        AcceptButton = _save;
        Controls.Add(_save);
    }

    private async Task SaveAsync() => await RunSaveAsync(_save, async () =>
    {
        var description = string.IsNullOrWhiteSpace(_description.Text) ? null : _description.Text.Trim();
        var permissions = _role?.Permissions.ToList()
                          ?? (_copyFrom?.SelectedItem as RoleSource)?.Permissions.ToList()
                          ?? new List<string>();
        var request = new SaveRoleRequest(_name.Text.Trim(), description, permissions);

        using var scope = _scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IValidator<SaveRoleRequest>>().Check(request) is { IsFailure: true } invalid)
        {
            return invalid;
        }

        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var result = _role is null ? await roles.CreateAsync(request) : await roles.UpdateAsync(_role.Id, request);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    });

    private sealed record RoleSource(int Id, string Name, IReadOnlyCollection<string> Permissions);
}

using GroceryPOS.Application.Users;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids.Core;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>
/// Roles & permissions (vai trò & phân quyền) as a matrix: one row per permission grouped by area, one checkbox column
/// per role. Clicking a group row toggles the whole group. Changes are highlighted until "Lưu thay đổi" saves them all
/// at once; they apply at each user's next sign-in. The Admin column is read-only. Requires <c>roles.manage</c>.
/// </summary>
internal sealed class RolePermissionScreen : UserControl
{
    private const string Caption = "Vai trò & phân quyền";
    private const int FirstRoleColumn = 1;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly ModernButton _editRole;
    private readonly ModernButton _deleteRole;
    private readonly ModernButton _save;
    private readonly ModernButton _undo;
    private readonly Label _status;
    private PermissionMatrix? _matrix;

    public RolePermissionScreen(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Dock = DockStyle.Fill;
        Padding = new Padding(16);

        var addRole = new ModernButton { Text = "Thêm vai trò", Variant = ButtonVariant.Primary, Width = 130 };
        addRole.Click += (_, _) => OpenRole(null);
        _editRole = new ModernButton { Text = "Sửa vai trò", Variant = ButtonVariant.Secondary, Width = 130, Enabled = false };
        _editRole.Click += (_, _) => OpenRole(SelectedRole);
        _deleteRole = new ModernButton { Text = "Xóa vai trò", Variant = ButtonVariant.Danger, Width = 130, Enabled = false };
        _deleteRole.Click += async (_, _) => await DeleteRoleAsync();
        _save = new ModernButton { Text = "Lưu thay đổi", Variant = ButtonVariant.Primary, Width = 130, Enabled = false };
        _save.Click += async (_, _) => await SaveAsync();
        _undo = new ModernButton { Text = "Hoàn tác", Variant = ButtonVariant.Ghost, Width = 130, Enabled = false };
        _undo.Click += (_, _) =>
        {
            _matrix?.Reset();
            RefreshCells();
        };

        _status = new Label
        {
            AutoSize = true,
            Margin = new Padding(16, 12, 0, 0),
            Text = "Chọn một cột để sửa vai trò. Bấm vào dòng nhóm để bật/tắt cả nhóm.",
        };

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 12),
        };
        toolbar.Controls.AddRange([addRole, _editRole, _deleteRole, _save, _undo, _status]);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            ReadOnly = true,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 52,
            EnableHeadersVisualStyles = false,
        };
        _grid.RowTemplate.Height = 32;
        _grid.CellContentClick += (_, e) => ToggleCell(e.RowIndex, e.ColumnIndex);
        _grid.ColumnHeaderMouseClick += (_, e) => SelectColumn(e.ColumnIndex);
        _grid.CurrentCellChanged += (_, _) => UpdateToolbarState();
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Space && _grid.CurrentCell is { } cell)
            {
                ToggleCell(cell.RowIndex, cell.ColumnIndex);
                e.Handled = true;
            }
        };

        Controls.Add(_grid);
        Controls.Add(toolbar);

        ThemeManager.ThemeChanged += OnThemeChanged;
        Disposed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
        Load += async (_, _) =>
        {
            ApplyGridTheme(ThemeManager.Current);
            await ReloadAsync();
        };
    }

    private RoleDto? SelectedRole =>
        _grid.CurrentCell is { ColumnIndex: >= FirstRoleColumn } cell ? _grid.Columns[cell.ColumnIndex].Tag as RoleDto : null;

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        ApplyGridTheme(e.Current);
        RefreshCells();
    }

    private void ApplyGridTheme(Theme theme)
    {
        var colors = GridPalette.Resolve(theme);
        _grid.BackgroundColor = colors.Background;
        _grid.GridColor = colors.GridLine;
        _grid.DefaultCellStyle.BackColor = colors.Row;
        _grid.DefaultCellStyle.ForeColor = colors.Text;
        _grid.DefaultCellStyle.SelectionBackColor = colors.SelectionBackground;
        _grid.DefaultCellStyle.SelectionForeColor = colors.SelectionText;
        _grid.DefaultCellStyle.Font = ThemeFonts.Get(FontRole.Body);
        _grid.ColumnHeadersDefaultCellStyle.BackColor = colors.HeaderBackground;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = colors.HeaderText;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = colors.HeaderBackground;
        _grid.ColumnHeadersDefaultCellStyle.Font = ThemeFonts.Get(FontRole.Body, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
    }

    private async Task ReloadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var result = await roles.ListAsync();
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var selectedRoleId = SelectedRole?.Id;
        _matrix = new PermissionMatrix(roles.GetPermissionCatalog(), result.Value);
        BuildGrid();
        if (selectedRoleId is { } id && _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => (c.Tag as RoleDto)?.Id == id) is { } column)
        {
            SelectColumn(column.Index);
        }

        UpdateToolbarState();
    }

    private void BuildGrid()
    {
        var matrix = _matrix!;
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        _grid.Columns.Clear();

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Quyền",
            Width = 280,
            Frozen = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });

        foreach (var role in matrix.Roles)
        {
            _grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = $"{role.Name}\n{role.UserCount} người dùng",
                ToolTipText = role.PermissionsLocked
                    ? "Admin luôn có toàn bộ quyền."
                    : role.Description ?? role.Name,
                Tag = role,
                Width = 128,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
        }

        foreach (var group in matrix.Groups)
        {
            var header = _grid.Rows[_grid.Rows.Add()];
            header.Tag = group;
            header.Cells[0].Value = group.Title.ToUpperInvariant();
            for (var c = FirstRoleColumn; c < _grid.Columns.Count; c++)
            {
                ((DataGridViewCheckBoxCell)header.Cells[c]).ThreeState = true;
            }

            foreach (var item in group.Items)
            {
                var row = _grid.Rows[_grid.Rows.Add()];
                row.Tag = item;
                row.Cells[0].Value = "    " + item.DisplayName;
                row.Cells[0].ToolTipText = item.Key;
            }
        }

        _grid.ResumeLayout();
        RefreshCells();
    }

    private void RefreshCells()
    {
        if (_matrix is not { } matrix)
        {
            return;
        }

        var theme = ThemeManager.Current;
        var colors = GridPalette.Resolve(theme);
        var groupBack = theme.Colors.SurfaceAlt;
        var changedBack = ColorMath.Blend(colors.Row, theme.Colors.Warning, theme.IsDark ? 0.35 : 0.25);
        var lockedBack = ColorMath.Blend(colors.Row, theme.Colors.DisabledBackground, 0.6);

        foreach (DataGridViewRow row in _grid.Rows)
        {
            var isGroup = row.Tag is PermissionGroupDto;
            row.Cells[0].Style.BackColor = isGroup ? groupBack : colors.Row;
            row.Cells[0].Style.ForeColor = isGroup ? theme.Colors.TextSecondary : colors.Text;
            row.Cells[0].Style.Font = isGroup ? ThemeFonts.Get(FontRole.Caption, FontStyle.Bold) : null;

            for (var c = FirstRoleColumn; c < _grid.Columns.Count; c++)
            {
                var role = (RoleDto)_grid.Columns[c].Tag!;
                var cell = row.Cells[c];
                switch (row.Tag)
                {
                    case PermissionGroupDto group:
                        cell.Value = matrix.StateOf(role.Id, group) switch
                        {
                            GroupCheck.All => CheckState.Checked,
                            GroupCheck.Some => CheckState.Indeterminate,
                            _ => CheckState.Unchecked,
                        };
                        cell.Style.BackColor = groupBack;
                        break;
                    case PermissionItem item:
                        cell.Value = matrix.Has(role.Id, item.Key);
                        cell.Style.BackColor = matrix.IsChanged(role.Id, item.Key) ? changedBack
                            : role.PermissionsLocked ? lockedBack
                            : colors.Row;
                        break;
                }
            }
        }

        var changed = matrix.ChangedRoleCount;
        _status.Text = changed == 0
            ? "Chọn một cột để sửa vai trò. Bấm vào dòng nhóm để bật/tắt cả nhóm."
            : $"Có thay đổi chưa lưu ở {changed} vai trò. Quyền mới áp dụng từ lần đăng nhập tiếp theo.";
        _status.ForeColor = changed == 0 ? theme.Colors.TextSecondary : theme.Colors.TextPrimary;
        UpdateToolbarState();
    }

    private void ToggleCell(int rowIndex, int columnIndex)
    {
        if (_matrix is not { } matrix || rowIndex < 0 || columnIndex < FirstRoleColumn)
        {
            return;
        }

        var role = (RoleDto)_grid.Columns[columnIndex].Tag!;
        var changed = _grid.Rows[rowIndex].Tag switch
        {
            PermissionGroupDto group => matrix.ToggleGroup(role.Id, group),
            PermissionItem item => matrix.Toggle(role.Id, item.Key),
            _ => false,
        };

        if (changed)
        {
            RefreshCells();
        }
        else if (!matrix.CanEdit(role.Id))
        {
            _status.Text = "Vai trò Admin luôn có toàn bộ quyền, không thể thay đổi.";
        }
    }

    private void SelectColumn(int columnIndex)
    {
        if (columnIndex >= FirstRoleColumn && _grid.Rows.Count > 0)
        {
            _grid.CurrentCell = _grid.Rows[0].Cells[columnIndex];
        }
    }

    private void UpdateToolbarState()
    {
        var dirty = _matrix?.IsDirty ?? false;
        var role = SelectedRole;
        _save.Enabled = dirty;
        _undo.Enabled = dirty;
        _editRole.Enabled = role is not null;
        _deleteRole.Enabled = role is { IsSystem: false, UserCount: 0 };
    }

    private bool EnsureSaved()
    {
        if (_matrix is not { IsDirty: true })
        {
            return true;
        }

        MessageBox.Show(this, "Hãy lưu hoặc hoàn tác thay đổi phân quyền trước.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void OpenRole(RoleDto? role)
    {
        if (_matrix is null || !EnsureSaved())
        {
            return;
        }

        using var dialog = new RoleEditForm(_scopeFactory, role, _matrix.Roles);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ = ReloadAsync();
        }
    }

    private async Task DeleteRoleAsync()
    {
        if (SelectedRole is not { } role || !EnsureSaved())
        {
            return;
        }

        if (MessageBox.Show(this, $"Xóa vai trò \"{role.Name}\"?", Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IRoleService>().DeleteAsync(role.Id);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ReloadAsync();
    }

    private async Task SaveAsync()
    {
        if (_matrix is not { IsDirty: true } matrix)
        {
            return;
        }

        _save.Enabled = false;
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IRoleService>().SavePermissionMatrixAsync(matrix.Changes);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateToolbarState();
            return;
        }

        await ReloadAsync();
        _status.Text = "Đã lưu phân quyền. Người dùng nhận quyền mới khi đăng nhập lại.";
    }
}

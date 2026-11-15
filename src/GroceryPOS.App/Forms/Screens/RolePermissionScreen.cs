using GroceryPOS.Application.Users;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Users;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Grids.Core;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>
/// Roles & permissions (vai trò & phân quyền) as a matrix: one row per permission grouped by area, one checkbox column
/// per role. Clicking a group row toggles the whole group. Changes are highlighted until "Lưu thay đổi" saves them all
/// at once; they apply at each user's next sign-in. Passive MVP view – the logic lives in <see cref="RolePermissionPresenter"/>.
/// </summary>
internal sealed class RolePermissionScreen : BaseUserControl, IRolePermissionView
{
    private const int FirstRoleColumn = 1;

    private readonly IServiceProvider _services;
    private readonly RolePermissionPresenter _presenter;
    private readonly DataGridView _grid;
    private readonly ModernButton _editRole;
    private readonly ModernButton _deleteRole;
    private readonly ModernButton _save;
    private readonly ModernButton _undo;
    private readonly Label _status;
    private PermissionMatrix? _matrix;
    private bool _statusEmphasis;

    public RolePermissionScreen(IServiceProvider services)
    {
        _services = services;
        _presenter = services.GetRequiredService<RolePermissionPresenter>();
        _presenter.Attach(this);
        Padding = new Padding(16);

        var addRole = new ModernButton { Text = "Thêm vai trò", Variant = ButtonVariant.Primary, Width = 130, Enabled = _presenter.CanManage };
        addRole.Click += async (_, _) => await _presenter.AddRoleAsync();
        _editRole = new ModernButton { Text = "Sửa vai trò", Variant = ButtonVariant.Secondary, Width = 130, Enabled = false };
        _editRole.Click += async (_, _) =>
        {
            if (SelectedRole is { } role)
            {
                await _presenter.EditRoleAsync(role);
            }
        };
        _deleteRole = new ModernButton { Text = "Xóa vai trò", Variant = ButtonVariant.Danger, Width = 130, Enabled = false };
        _deleteRole.Click += async (_, _) =>
        {
            if (SelectedRole is { } role)
            {
                await _presenter.DeleteRoleAsync(role);
            }
        };
        _save = new ModernButton { Text = "Lưu thay đổi", Variant = ButtonVariant.Primary, Width = 130, Enabled = false };
        _save.Click += async (_, _) =>
        {
            _save.Enabled = false;
            await _presenter.SaveAsync();
            UpdateToolbarState();
        };
        _undo = new ModernButton { Text = "Hoàn tác", Variant = ButtonVariant.Ghost, Width = 130, Enabled = false };
        _undo.Click += (_, _) => _presenter.Undo();

        _status = new Label
        {
            AutoSize = true,
            Margin = new Padding(16, 12, 0, 0),
            Text = RolePermissionPresenter.IdleHint,
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
        ApplyGridTheme(ThemeManager.Current);
    }

    int? IRolePermissionView.SelectedRoleId => SelectedRole?.Id;

    bool IView.IsAlive => IsAlive;

    /// <summary>F5 reloads, but asks first when there are unsaved changes.</summary>
    public override async Task RefreshDataAsync()
    {
        if (_presenter.ConfirmLeave())
        {
            await _presenter.LoadAsync();
        }
    }

    public override bool CanLeave() => _presenter.ConfirmLeave();

    void IRolePermissionView.ShowMatrix(PermissionMatrix matrix, int? selectRoleId)
    {
        _matrix = matrix;
        BuildGrid();
        if (selectRoleId is { } id && _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => (c.Tag as RoleDto)?.Id == id) is { } column)
        {
            SelectColumn(column.Index);
        }

        UpdateToolbarState();
    }

    void IRolePermissionView.RefreshMatrix() => RefreshCells();

    void IRolePermissionView.ShowStatus(string text, bool emphasis)
    {
        _status.Text = text;
        _statusEmphasis = emphasis;
        ApplyStatusColor(ThemeManager.Current);
    }

    bool IRolePermissionView.OpenRoleEditor(RoleDto? role, IReadOnlyList<RoleDto> roles)
    {
        using var dialog = new RoleEditForm(_services.GetRequiredService<RoleEditPresenter>().For(role, roles));
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);

    private RoleDto? SelectedRole =>
        _grid.CurrentCell is { ColumnIndex: >= FirstRoleColumn } cell ? _grid.Columns[cell.ColumnIndex].Tag as RoleDto : null;

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        ApplyGridTheme(e.Current);
        RefreshCells();
        ApplyStatusColor(e.Current);
    }

    private void ApplyStatusColor(Theme theme) =>
        _status.ForeColor = _statusEmphasis ? theme.Colors.TextPrimary : theme.Colors.TextSecondary;

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
                HeaderText = RolePermissionPresenter.HeaderText(role),
                ToolTipText = RolePermissionPresenter.HeaderToolTip(role),
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

        UpdateToolbarState();
    }

    private void ToggleCell(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < FirstRoleColumn || _grid.Columns[columnIndex].Tag is not RoleDto role)
        {
            return;
        }

        switch (_grid.Rows[rowIndex].Tag)
        {
            case PermissionGroupDto group:
                _presenter.ToggleGroup(role.Id, group);
                break;
            case PermissionItem item:
                _presenter.Toggle(role.Id, item.Key);
                break;
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
        var role = SelectedRole;
        var canManage = _presenter.CanManage;
        _save.Enabled = _presenter.CanSave;
        _undo.Enabled = canManage && _presenter.HasChanges;
        _editRole.Enabled = canManage && role is not null;
        _deleteRole.Enabled = canManage && RolePermissionPresenter.CanDelete(role);
    }
}

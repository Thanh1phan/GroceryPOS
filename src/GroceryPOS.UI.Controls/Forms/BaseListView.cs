using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Forms.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Forms;

/// <summary>
/// Base class for list screens (danh mục, nhà cung cấp, …): a <see cref="ModernDataGrid"/> filling the screen,
/// toolbar commands whose enabled state and text follow the permission and the selected row
/// (<see cref="ListCommand{TItem}"/>), "latest wins" reloads that keep the selected row, and a default command
/// run on double-click / Enter. Derived screens add columns to <see cref="Grid"/>, register commands and
/// implement <see cref="LoadItemsAsync"/>.
/// </summary>
/// <typeparam name="TItem">Row type (a DTO).</typeparam>
public abstract class BaseListView<TItem> : BaseUserControl
    where TItem : class
{
    private readonly LoadSequencer _loads = new();
    private readonly List<CommandBinding> _commands = new();
    private CommandBinding? _defaultCommand;

    protected BaseListView()
    {
        Grid = new ModernDataGrid { Dock = DockStyle.Fill };
        Grid.SelectionChanged += (_, _) => UpdateCommands();
        Grid.PageLoaded += (_, _) => UpdateCommands();
        Grid.RowActivated += (_, e) =>
        {
            if (e.Item is TItem item)
            {
                OnItemActivated(item);
            }
        };
        Controls.Add(Grid);
        Disposed += (_, _) => _loads.Dispose();
    }

    protected ModernDataGrid Grid { get; }

    /// <summary>The selected row, or null.</summary>
    protected TItem? SelectedItem => Grid.GetSelected<TItem>();

    /// <summary>True while a reload is running.</summary>
    protected bool IsLoading => _loads.IsLoading;

    /// <summary>
    /// Loads the rows. Return null when loading failed (show the error yourself); the grid then keeps its rows.
    /// The token is cancelled when a newer reload starts or the screen closes.
    /// </summary>
    protected abstract Task<IReadOnlyList<TItem>?> LoadItemsAsync(CancellationToken cancellationToken);

    /// <summary>Identity of a row, used to select the same row again after a reload. Default: none (no re-selection).</summary>
    protected virtual object? KeyOf(TItem item) => null;

    /// <summary>Double-click / Enter on a row. Default: run the command registered with <c>isDefault: true</c>, if enabled.</summary>
    protected virtual void OnItemActivated(TItem item)
    {
        if (_defaultCommand is { } command && command.Button.Enabled)
        {
            command.Button.PerformClick();
        }
    }

    /// <summary>Adds a toolbar command that does not need a row (Thêm …).</summary>
    protected ModernButton AddCommand(ListCommand<TItem> rule, Func<Task> execute, ButtonVariant variant = ButtonVariant.Secondary, string? iconGlyph = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return Register(rule, _ => execute(), variant, iconGlyph, isDefault: false);
    }

    /// <summary>Adds a toolbar command that acts on the selected row (Sửa, Ngừng dùng …); it is disabled without a selection.</summary>
    protected ModernButton AddCommand(
        ListCommand<TItem> rule,
        Func<TItem, Task> execute,
        ButtonVariant variant = ButtonVariant.Secondary,
        string? iconGlyph = null,
        bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(execute);
        if (!rule.RequiresSelection)
        {
            rule = new ListCommand<TItem>(rule.Text)
            {
                Allowed = rule.Allowed,
                WhenUnauthorized = rule.WhenUnauthorized,
                RequiresSelection = true,
                EnabledWhen = rule.EnabledWhen,
                TextFor = rule.TextFor,
                Available = rule.Available,
                LongestText = rule.LongestText,
            };
        }

        return Register(rule, item => item is null ? Task.CompletedTask : execute(item), variant, iconGlyph, isDefault);
    }

    /// <summary>Synchronous overload of <see cref="AddCommand(ListCommand{TItem}, Func{Task}, ButtonVariant, string?)"/>.</summary>
    protected ModernButton AddCommand(ListCommand<TItem> rule, Action execute, ButtonVariant variant = ButtonVariant.Secondary, string? iconGlyph = null) =>
        AddCommand(rule, () => { execute(); return Task.CompletedTask; }, variant, iconGlyph);

    /// <summary>Synchronous overload for row commands.</summary>
    protected ModernButton AddCommand(
        ListCommand<TItem> rule,
        Action<TItem> execute,
        ButtonVariant variant = ButtonVariant.Secondary,
        string? iconGlyph = null,
        bool isDefault = false) =>
        AddCommand(rule, item => { execute(item); return Task.CompletedTask; }, variant, iconGlyph, isDefault);

    /// <summary>Re-evaluates every command for the current selection (call after state the rules depend on changed).</summary>
    protected void UpdateCommands()
    {
        if (!IsAlive)
        {
            return;
        }

        var selected = SelectedItem;
        foreach (var binding in _commands)
        {
            var state = binding.Rule.Evaluate(selected);
            binding.Button.Visible = state.Visible;
            binding.Button.Enabled = state.Enabled && !binding.Running;
            if (!string.Equals(binding.Button.Text, state.Text, StringComparison.Ordinal))
            {
                binding.Button.Text = state.Text;
            }
        }
    }

    public override async Task RefreshDataAsync()
    {
        if (!IsAlive)
        {
            return;
        }

        var ticket = _loads.Begin();
        var keep = SelectedItem is { } selected ? KeyOf(selected) : null;

        IReadOnlyList<TItem>? items;
        try
        {
            items = await LoadItemsAsync(ticket.Token);
        }
        catch (OperationCanceledException) when (ticket.Token.IsCancellationRequested)
        {
            return; // superseded by a newer reload, or the screen closed
        }

        if (!IsAlive || !_loads.Complete(ticket))
        {
            return;
        }

        if (items is not null)
        {
            Grid.SetItems(items);
            if (keep is not null && items.FirstOrDefault(i => Equals(KeyOf(i), keep)) is { } again)
            {
                Grid.SelectItem(again);
            }
        }

        UpdateCommands();
    }

    private ModernButton Register(ListCommand<TItem> rule, Func<TItem?, Task> execute, ButtonVariant variant, string? iconGlyph, bool isDefault)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var button = new ModernButton
        {
            Text = rule.Text,
            Variant = variant,
            IconGlyph = iconGlyph,
            AutoSize = false,
        };
        button.Width = Math.Max(button.Width, TextRenderer.MeasureText(rule.SizingText, button.Font).Width + (iconGlyph is null ? 32 : 56));

        var binding = new CommandBinding(rule, button);
        button.Click += async (_, _) =>
        {
            var state = rule.Evaluate(SelectedItem);
            if (binding.Running || !state.Enabled)
            {
                return;
            }

            binding.Running = true;
            UpdateCommands();
            try
            {
                await execute(SelectedItem);
            }
            finally
            {
                binding.Running = false;
                UpdateCommands();
            }
        };

        _commands.Add(binding);
        if (isDefault)
        {
            _defaultCommand = binding;
        }

        Grid.ToolbarItems.Controls.Add(button);
        var initial = rule.Evaluate(null);
        button.Visible = initial.Visible;
        button.Enabled = initial.Enabled;
        return button;
    }

    private sealed class CommandBinding(ListCommand<TItem> rule, ModernButton button)
    {
        public ListCommand<TItem> Rule { get; } = rule;

        public ModernButton Button { get; } = button;

        public bool Running { get; set; }
    }
}

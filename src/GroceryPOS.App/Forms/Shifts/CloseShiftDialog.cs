using System.Globalization;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Shifts;

/// <summary>
/// Kết ca: count sheet by denomination (or one typed total), live variance against the system's expected cash,
/// cash left for the next shift and the hand-over amount. The state lives in the tested <see cref="CashCountSheet"/>;
/// "Kết ca" stays disabled while the sheet has a blocker. Enter moves down the count sheet (Ctrl+S closes the shift),
/// and closing the window after counting asks first. On success <see cref="ClosedShift"/> holds the frozen report.
/// </summary>
internal sealed class CloseShiftDialog : BaseEditDialog
{
    private const int BodyWidth = 752;
    private const int SheetWidth = 380;
    private const int SummaryX = 408;
    private const int SummaryWidth = BodyWidth - SummaryX;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CashCountSheet _sheet;
    private readonly Dictionary<decimal, ModernNumericBox> _countBoxes = new();
    private readonly Dictionary<decimal, Label> _amountLabels = new();
    private readonly ModernSegmentedControl _mode;
    private readonly Panel _denominationPanel;
    private readonly ModernCurrencyBox _typedTotal;
    private readonly Label _counted;
    private readonly Label _noteCount;
    private readonly ModernBadge _variance;
    private readonly Label _varianceAmount;
    private readonly ModernCurrencyBox _left;
    private readonly FormNote _handedOver;
    private readonly ModernTextBox _note;
    private readonly FormNote _blocker;
    private bool _syncing;

    public CloseShiftDialog(IServiceScopeFactory scopeFactory, ShiftSummaryDto shift, SalesOptions options, bool closingForSomeoneElse)
        : base(closingForSomeoneElse ? $"Kết ca hộ – {shift.Code} ({shift.UserName})" : $"Kết ca {shift.Code}", contentWidth: BodyWidth)
    {
        _scopeFactory = scopeFactory;
        _sheet = new CashCountSheet(shift, options);
        SaveText = closingForSomeoneElse ? "Kết ca hộ" : "Kết ca";
        AcceptButton = null; // Enter walks down the count sheet instead of closing the shift

        AddNote(
            $"Thu ngân: {shift.UserName}  ·  Mở ca: {shift.OpenedAt.ToOffset(options.StoreUtcOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}  ·  " +
            $"{shift.Totals.SalesCount} HĐ  ·  Doanh thu thuần {Vnd.Format(shift.Totals.NetRevenue)}",
            NoteTone.Muted);
        var body = AddField(null, new Panel { Height = 440 }, fullWidth: true, track: false);

        // ---- Count sheet (left) -------------------------------------------------------------------
        _mode = new ModernSegmentedControl { Location = new Point(0, 0), Width = SheetWidth };
        _mode.SetItems(["Đếm theo mệnh giá", "Nhập tổng tiền"]);
        _mode.SelectedIndexChanged += (_, _) =>
            _sheet.SetMode(_mode.SelectedIndex == 0 ? CashCountMode.ByDenomination : CashCountMode.TypedTotal);

        _denominationPanel = new Panel { Location = new Point(0, 48), Size = new Size(SheetWidth, 392) };
        var y = 0;
        _denominationPanel.Controls.Add(new Label { Text = "Mệnh giá", Location = new Point(0, y), Size = new Size(110, 20) });
        _denominationPanel.Controls.Add(new Label { Text = "Số tờ", Location = new Point(120, y), Size = new Size(110, 20) });
        _denominationPanel.Controls.Add(new Label { Text = "Thành tiền", Location = new Point(240, y), Size = new Size(130, 20), TextAlign = ContentAlignment.TopRight });
        y += 24;
        foreach (var denomination in _sheet.Denominations)
        {
            var caption = new Label
            {
                Text = Vnd.Format(denomination),
                Location = new Point(0, y + 8),
                Size = new Size(110, 22),
                Font = new Font(Font, FontStyle.Bold),
            };
            var box = new ModernNumericBox
            {
                Location = new Point(120, y),
                Width = 110,
                DecimalPlaces = 0,
                Minimum = 0,
                Maximum = 100_000,
                AllowThousandsShortcut = false,
                ReserveMessageSpace = false,
                SuffixText = "tờ",
            };
            var amount = new Label { Location = new Point(240, y + 8), Size = new Size(130, 22), TextAlign = ContentAlignment.TopRight };
            var current = denomination;
            box.ValueChanged += (_, _) =>
            {
                if (!_syncing)
                {
                    _sheet.SetCount(current, (int)box.ValueOrZero);
                }
            };
            box.KeyDown += (_, e) => MoveFocusOnEnter(e, current);
            _countBoxes[denomination] = box;
            _amountLabels[denomination] = amount;
            _denominationPanel.Controls.AddRange([caption, box, amount]);
            y += 40;
        }

        _typedTotal = new ModernCurrencyBox { Location = new Point(0, 56), Width = SheetWidth, Visible = false, PlaceholderText = "Tổng tiền mặt thực đếm" };
        _typedTotal.ValueChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _sheet.SetTypedTotal(_typedTotal.ValueOrZero);
            }
        };

        // ---- Summary (right) ----------------------------------------------------------------------
        const int rx = SummaryX;
        const int rw = SummaryWidth;
        var card = new ModernCard { Title = "Đối chiếu tiền mặt", Location = new Point(rx, 0), Size = new Size(rw, 220) };
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        Label Value(bool bold = false) => new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = bold ? new Font(Font, FontStyle.Bold) : Font,
        };
        void Row(string caption, Control value)
        {
            rows.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
            rows.Controls.Add(value);
        }

        var expected = Value(bold: true);
        expected.Text = Vnd.Format(_sheet.Expected);
        _counted = Value(bold: true);
        _noteCount = Value();
        _variance = new ModernBadge { Anchor = AnchorStyles.Right };
        _varianceAmount = Value(bold: true);
        Row("Tiền mặt theo hệ thống", expected);
        Row("Tiền mặt thực đếm", _counted);
        Row("Số tờ đã đếm", _noteCount);
        Row("Chênh lệch", _varianceAmount);
        Row(string.Empty, _variance);
        card.Controls.Add(rows);

        var leftCaption = new Label { Text = "Để lại két cho ca sau", AutoSize = true, Location = new Point(rx, 232) };
        _left = new ModernCurrencyBox { Location = new Point(rx, 254), Width = 220, ReserveMessageSpace = false };
        _left.ValueChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _sheet.SetCashLeftInDrawer(_left.ValueOrZero);
            }
        };
        var leaveAll = new ModernButton { Text = "Để lại tất cả", Variant = ButtonVariant.Secondary, Location = new Point(rx + 228, 254), Size = new Size(116, 36), TabStop = false };
        leaveAll.Click += (_, _) => _sheet.LeaveAll();

        var handCaption = new Label { Text = "Bàn giao (thực đếm − để lại két)", AutoSize = true, Location = new Point(rx, 302) };
        _handedOver = new FormNote { Location = new Point(rx, 324), Width = rw, FontRole = FontRole.Title, TextStyle = FontStyle.Bold };

        var noteCaption = new Label { Text = "Ghi chú", AutoSize = true, Location = new Point(rx, 360) };
        _note = new ModernTextBox { Location = new Point(rx, 382), Width = rw, MaxLength = 500, ReserveMessageSpace = false, PlaceholderText = "Bắt buộc khi chênh lệch lớn" };
        _note.InnerTextBox.TextChanged += (_, _) => _sheet.SetNote(_note.Text);
        _note.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None)
            {
                e.SuppressKeyPress = true;
                SaveButton.Focus();
            }
        };

        _blocker = new FormNote { Location = new Point(rx, 418), Width = rw, Tone = NoteTone.Warning, MinLines = 0 };

        body.Controls.AddRange([
            _mode, _denominationPanel, _typedTotal, card,
            leftCaption, _left, leaveAll, handCaption, _handedOver, noteCaption, _note, _blocker,
        ]);

        // Unsaved work = anything counted or typed since the dialog opened.
        Changes.Track("count", () => string.Create(CultureInfo.InvariantCulture, $"{_sheet.Mode}|{_sheet.Counted}|{_sheet.CashLeftInDrawer}|{_note.Text.Trim()}"));

        _sheet.Changed += (_, _) => Render();
        Render();
    }

    public ShiftSummaryDto? ClosedShift { get; private set; }

    protected override bool CanSave => _sheet.CanClose;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var first = _countBoxes[_sheet.Denominations[0]];
        first.Focus();
        first.SelectAll();
    }

    private void MoveFocusOnEnter(KeyEventArgs e, decimal denomination)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        var list = _sheet.Denominations;
        var index = list.ToList().IndexOf(denomination);
        if (index + 1 < list.Count)
        {
            _countBoxes[list[index + 1]].Focus();
            _countBoxes[list[index + 1]].SelectAll();
        }
        else
        {
            _left.Focus();
        }
    }

    private void Render()
    {
        _syncing = true;
        try
        {
            var byDenomination = _sheet.Mode == CashCountMode.ByDenomination;
            _denominationPanel.Visible = byDenomination;
            _typedTotal.Visible = !byDenomination;
            if (!byDenomination && _typedTotal.ValueOrZero != _sheet.Counted)
            {
                _typedTotal.Value = _sheet.Counted;
            }

            foreach (var denomination in _sheet.Denominations)
            {
                var amount = _sheet.AmountOf(denomination);
                _amountLabels[denomination].Text = amount == 0 ? "–" : Vnd.FormatNumber(amount);
            }

            if (_left.ValueOrZero != _sheet.CashLeftInDrawer)
            {
                _left.Value = _sheet.CashLeftInDrawer;
            }
        }
        finally
        {
            _syncing = false;
        }

        _counted.Text = Vnd.Format(_sheet.Counted);
        _noteCount.Text = _sheet.Mode == CashCountMode.ByDenomination ? _sheet.NoteCount.ToString("#,0", Vnd.Culture) : "–";
        var variance = _sheet.Variance;
        _varianceAmount.Text = variance > 0 ? "+" + Vnd.Format(variance) : Vnd.Format(variance);
        _variance.Set(_sheet.VarianceLabel, variance switch
        {
            0 => BadgeKind.Success,
            _ when _sheet.NoteRequired => BadgeKind.Danger,
            _ => BadgeKind.Warning,
        });
        _handedOver.Text = Vnd.Format(_sheet.CashHandedOver);
        _blocker.Text = _sheet.Blocker ?? string.Empty;
        if (!IsSaving)
        {
            SaveButton.Enabled = _sheet.CanClose;
        }
    }

    protected override async Task<string?> SaveAsync()
    {
        foreach (var box in _countBoxes.Values)
        {
            box.Commit();
        }

        _typedTotal.Commit();
        _left.Commit();
        var request = _sheet.BuildRequest();
        if (request.IsFailure)
        {
            Render();
            return request.Error.Message;
        }

        var confirmed = Confirm(
            $"Kết ca với tiền mặt thực đếm {Vnd.Format(_sheet.Counted)} ({_sheet.VarianceLabel.ToLower(Vnd.Culture)}" +
            (_sheet.Variance == 0 ? ")" : $" {Vnd.Format(Math.Abs(_sheet.Variance))})") +
            $"?\nBàn giao {Vnd.Format(_sheet.CashHandedOver)}, để lại két {Vnd.Format(_sheet.CashLeftInDrawer)}.\n\nSau khi kết ca không thể sửa lại.",
            "Kết ca",
            SaveText,
            "Đếm lại");
        if (!confirmed)
        {
            return string.Empty;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IShiftService>().CloseAsync(request.Value);
        if (result.IsFailure)
        {
            return result.Error.Message;
        }

        ClosedShift = result.Value;
        return null;
    }
}

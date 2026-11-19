using System.ComponentModel;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Drawing;

namespace GroceryPOS.UI.Controls.Display;

/// <summary>
/// Row of KPI cards (<see cref="StatCard"/> or any control) that always shows every visible card: cards share the
/// width equally and wrap onto a second row (balanced) when they would get narrower than <see cref="MinCardWidth"/>.
/// The row sets its own height, so dock it Top – it never cuts cards off at the bottom or the right edge.
/// </summary>
[ToolboxItem(true)]
public class StatCardRow : Panel
{
    private int _minCardWidth = 200;
    private int _cardHeight = 128;
    private int _gap = 16;
    private bool _arranging;

    public StatCardRow()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        Dock = DockStyle.Top;
        Height = _cardHeight;
    }

    /// <summary>Narrowest a card may get (pixels at 96 DPI) before the cards wrap onto another row.</summary>
    [Category("Layout")]
    [DefaultValue(200)]
    public int MinCardWidth
    {
        get => _minCardWidth;
        set
        {
            _minCardWidth = Math.Max(80, value);
            PerformLayout();
        }
    }

    /// <summary>Card height (pixels at 96 DPI).</summary>
    [Category("Layout")]
    [DefaultValue(128)]
    public int CardHeight
    {
        get => _cardHeight;
        set
        {
            _cardHeight = Math.Max(40, value);
            PerformLayout();
        }
    }

    /// <summary>Space between cards, horizontally and vertically (pixels at 96 DPI).</summary>
    [Category("Layout")]
    [DefaultValue(16)]
    public int Gap
    {
        get => _gap;
        set
        {
            _gap = Math.Max(0, value);
            PerformLayout();
        }
    }

    /// <summary>Adds cards in order.</summary>
    public void AddCards(params Control[] cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        SuspendLayout();
        foreach (var card in cards)
        {
            card.Margin = Padding.Empty;
            Controls.Add(card);
        }

        ResumeLayout(true);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is not null)
        {
            e.Control.VisibleChanged += OnCardVisibleChanged;
        }
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        if (e.Control is not null)
        {
            e.Control.VisibleChanged -= OnCardVisibleChanged;
        }

        base.OnControlRemoved(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        // While hidden (e.g. on a tab page not selected) Control.Visible is false for every card, which would collapse
        // the row; lay out again once it is shown.
        if (_arranging || (IsHandleCreated && !Visible))
        {
            return;
        }

        _arranging = true;
        try
        {
            // Controls.Add order = left to right (z-order is the reverse, so do not rely on it).
            var cards = Controls.Cast<Control>().Where(c => c.Visible || !IsHandleCreated).ToList();
            var arrangement = CardGridLayout.Arrange(
                cards.Count,
                ClientSize.Width - Padding.Horizontal,
                this.ScaleDpi(_minCardWidth),
                this.ScaleDpi(_cardHeight),
                this.ScaleDpi(_gap));
            for (var i = 0; i < cards.Count; i++)
            {
                var b = arrangement.Bounds[i];
                cards[i].SetBounds(Padding.Left + b.X, Padding.Top + b.Y, b.Width, b.Height);
            }

            var height = arrangement.Height + Padding.Vertical;
            if (Dock is DockStyle.Top or DockStyle.Bottom or DockStyle.None && Height != height)
            {
                Height = height;
            }
        }
        finally
        {
            _arranging = false;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            PerformLayout();
        }
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
    }

    private void OnCardVisibleChanged(object? sender, EventArgs e) => PerformLayout();
}

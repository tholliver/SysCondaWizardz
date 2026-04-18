namespace SysCondaWizard;

/// <summary>
/// Thin helpers to build consistent UI across all wizard steps.
/// Keeps step code clean and layout consistent.
/// </summary>
public static class WizardUi
{
    private static readonly Color SectionColor = Color.FromArgb(50, 50, 120);
    private static readonly Color HintColor    = Color.FromArgb(110, 110, 130);
    private static readonly Color InfoBg       = Color.FromArgb(238, 242, 255);
    private static readonly Color InfoBorder   = Color.FromArgb(180, 190, 240);
    private const int DefaultContentWidth = 660;

    /// <summary>Creates the scrollable root panel every step uses as its container.</summary>
    public static Panel MakeScrollPanel()
    {
        var panel = new Panel
        {
            AutoScroll = true,
            AutoSize   = false,
            Padding    = new Padding(4, 0, 4, 8),
        };

        panel.Resize += (_, _) => RefreshLayout(panel);
        return panel;
    }

    /// <summary>Bold section heading.</summary>
    public static void SectionLabel(Control parent, string text)
    {
        var lbl = new Label
        {
            Text      = text,
            AutoSize  = true,
            Font      = new Font("Segoe UI Semibold", 10f),
            ForeColor = SectionColor,
            Margin    = new Padding(0, 10, 0, 2),
        };
        AddRow(parent, lbl);
    }

    /// <summary>Muted hint text.</summary>
    public static void Hint(Control parent, string text)
    {
        var lbl = new Label
        {
            Text      = text,
            AutoSize  = false,
            Height    = (text.Split('\n').Length) * 18,
            Width     = 640,
            ForeColor = HintColor,
            Font      = new Font("Segoe UI", 8.5f),
            Margin    = new Padding(0, 0, 0, 4),
        };
        AddRow(parent, lbl);
    }

    /// <summary>Horizontal rule separator.</summary>
    public static void Separator(Control parent)
    {
        var sep = new Panel
        {
            Height    = 1,
            Width     = 640,
            BackColor = Color.FromArgb(220, 220, 235),
            Margin    = new Padding(0, 10, 0, 4),
        };
        AddRow(parent, sep);
    }

    /// <summary>Indigo-tinted info box.</summary>
    public static void InfoBox(Control parent, string text)
    {
        var lines = text.Split('\n').Length;
        var box   = new Panel
        {
            Width     = 640,
            Height    = lines * 18 + 16,
            BackColor = InfoBg,
            Margin    = new Padding(0, 12, 0, 0),
            Padding   = new Padding(10, 6, 10, 6),
        };
        box.Paint += (_, e) =>
        {
            using var pen = new Pen(InfoBorder, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, box.Width - 1, box.Height - 1);
        };
        var lbl = new Label
        {
            Text      = text,
            AutoSize  = false,
            Dock      = DockStyle.Fill,
            ForeColor = Color.FromArgb(60, 60, 160),
            Font      = new Font("Segoe UI", 8.5f),
        };
        box.Controls.Add(lbl);
        AddRow(parent, box);
    }

    /// <summary>Standard text box with optional placeholder text.</summary>
    public static TextBox TextBox(string value = "", string placeholder = "")
    {
        var tb = new TextBox
        {
            Text        = value,
            BorderStyle = BorderStyle.FixedSingle,
            Height      = 24,
        };
        if (!string.IsNullOrEmpty(placeholder) && string.IsNullOrEmpty(value))
            tb.ForeColor = Color.FromArgb(160, 160, 175);

        return tb;
    }

    /// <summary>Small secondary button (browse, etc.).</summary>
    public static Button SmallButton(string text, int width = 90)
    {
        return new Button
        {
            Text      = text,
            Width     = width,
            Height    = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(240, 240, 250),
            ForeColor = Color.FromArgb(60, 60, 100),
            Cursor    = Cursors.Hand,
            Font      = new Font("Segoe UI", 8.5f),
        };
    }

    /// <summary>
    /// Appends a control as the next row inside a Panel, stacking vertically.
    /// Respects the child's existing Margin.Top as extra spacing above it.
    /// </summary>
    public static void AddRow(Control parent, Control child)
    {
        if (parent is FlowLayoutPanel flp)
        {
            flp.Controls.Add(child);
            return;
        }

        // Calculate the Y position right below the last existing child,
        // plus any top-margin the incoming child declares.
        int nextY = parent.Padding.Top;
        foreach (Control c in parent.Controls)
        {
            int bottom = c.Location.Y + c.Height + c.Margin.Bottom;
            if (bottom > nextY) nextY = bottom;
        }

        child.Dock     = DockStyle.None;
        child.Location = new Point(parent.Padding.Left, nextY + child.Margin.Top);
        ApplyWidth(parent, child);

        parent.Controls.Add(child);
    }

    private static void RefreshLayout(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            ApplyWidth(parent, child);
        }
    }

    private static void ApplyWidth(Control parent, Control child)
    {
        if (child is Label { AutoSize: true } || child is CheckBox { AutoSize: true })
        {
            return;
        }

        var availableWidth = Math.Max(DefaultContentWidth, parent.ClientSize.Width - parent.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);

        if (child is Panel or RichTextBox)
        {
            child.Width = availableWidth;
            child.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            return;
        }

        if (child.Width <= 0 || child.Width > availableWidth)
        {
            child.Width = availableWidth;
        }
    }
}

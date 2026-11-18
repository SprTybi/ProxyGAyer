namespace ProxyGAyer;

internal sealed class ProxyBypassEditor : UserControl
{
    private readonly LinkLabel toggle = new();
    private readonly TextBox addresses = new();
    private readonly TableLayoutPanel details = new();
    public event EventHandler? ExpansionChanged;
    public bool Expanded { get; private set; }
    public int RequiredHeight => Expanded ? 132 : 32;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string BypassText { get => addresses.Text; set => addresses.Text = value; }

    public ProxyBypassEditor()
    {
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toggle.Dock = DockStyle.Fill;
        toggle.Margin = Padding.Empty;
        toggle.Text = "تنظیمات پیشرفته +";
        toggle.RightToLeft = RightToLeft.Yes;
        toggle.TextAlign = ContentAlignment.MiddleLeft;
        toggle.LinkColor = Color.FromArgb(0, 116, 109);
        toggle.LinkBehavior = LinkBehavior.HoverUnderline;
        toggle.Font = new Font("Segoe UI", 9);
        toggle.LinkClicked += (_, _) => SetExpanded(!Expanded);
        layout.Controls.Add(toggle, 0, 0);

        details.Dock = DockStyle.Fill;
        details.Margin = Padding.Empty;
        details.Visible = false;
        details.ColumnCount = 1;
        details.RowCount = 4;
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 25, 29, 22, 24 }) details.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        details.Controls.Add(Caption("آدرس‌هایی که مستقیم و بدون پروکسی باز شوند", 9), 0, 0);
        addresses.Dock = DockStyle.Fill;
        addresses.RightToLeft = RightToLeft.No;
        addresses.AccessibleName = "آدرس‌های بدون پروکسی";
        addresses.PlaceholderText = "localhost,127.0.0.1";
        details.Controls.Add(addresses, 0, 1);
        details.Controls.Add(Caption("مثلاً localhost برای سرویس‌های همین کامپیوتر است. موارد را با کاما جدا کنید.", 8), 0, 2);
        details.Controls.Add(Caption("فقط برای اجراهای داخل برنامه و بخش User environment؛ معمولاً نیازی به تغییر نیست.", 8), 0, 3);
        layout.Controls.Add(details, 0, 1);
        Controls.Add(layout);
    }

    public void SetExpanded(bool expanded)
    {
        if (Expanded == expanded) return;
        Expanded = expanded;
        details.Visible = expanded;
        toggle.Text = expanded ? "بستن تنظیمات پیشرفته −" : "تنظیمات پیشرفته +";
        ExpansionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Label Caption(string text, float fontSize) => new()
    {
        Text = text, Dock = DockStyle.Fill, RightToLeft = RightToLeft.Yes,
        TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", fontSize),
        ForeColor = Color.FromArgb(101, 114, 134), Margin = Padding.Empty
    };
}

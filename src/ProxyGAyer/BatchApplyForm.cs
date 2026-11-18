using ProxyGAyer.Core;

namespace ProxyGAyer;

internal sealed class BatchApplyForm : Form
{
    private static readonly Color Accent = Color.FromArgb(0, 116, 109);
    private static readonly Color Muted = Color.FromArgb(101, 114, 134);
    private readonly BatchProxyApplier applier;
    private readonly TextBox httpHost = new();
    private readonly TextBox httpPort = new();
    private readonly TextBox httpsHost = new();
    private readonly TextBox httpsPort = new();
    private readonly ProxyBypassEditor bypass = new();
    private readonly CheckBox same = new();
    private readonly CheckBox all = new();
    private readonly Label count = new();
    private readonly TextBox results = new();
    private readonly Button apply = new();
    private readonly FlowLayoutPanel targets = new();
    private readonly TableLayoutPanel layout = new();
    private readonly List<(ServiceDefinition Service, CheckBox Check)> choices = [];
    private bool updating;

    internal IReadOnlyList<BatchApplyResult> LastResults { get; private set; } = [];
    internal IReadOnlyList<string> SelectedIds => choices.Where(c => c.Check.Checked).Select(c => c.Service.Id).ToArray();

    public BatchApplyForm(BatchProxyApplier applier, IReadOnlyList<ServiceDefinition> services, ProxyProfile initial, IEnumerable<string> selectedIds)
    {
        this.applier = applier;
        Text = "ProxyGAyer — اعمال گروهی";
        Icon = AppBranding.Icon;
        Font = new Font("Segoe UI", 10);
        ForeColor = Color.FromArgb(27, 37, 57);
        BackColor = Color.FromArgb(245, 247, 250);
        ClientSize = new Size(930, 838);
        MinimumSize = new Size(880, 810);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowInTaskbar = false;
        MaximizeBox = false;

        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 164, Padding = new Padding(20, 8, 20, 14), ColumnCount = 1, RowCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        results.Dock = DockStyle.Fill;
        results.Multiline = true;
        results.ReadOnly = true;
        results.ScrollBars = ScrollBars.Vertical;
        results.RightToLeft = RightToLeft.Yes;
        results.BackColor = Color.White;
        results.BorderStyle = BorderStyle.FixedSingle;
        results.Text = "فقط سرویس‌های تیک‌خورده تغییر می‌کنند. نتیجه هر سرویس اینجا نمایش داده می‌شود.";
        footer.Controls.Add(results, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        MainForm.ConfigureButton(apply, "Set selected", true);
        apply.Click += (_, _) => ApplySelected();
        var close = new Button { DialogResult = DialogResult.Cancel };
        MainForm.ConfigureButton(close, "Close", false);
        actions.Controls.AddRange([apply, close]);
        footer.Controls.Add(actions, 0, 1);
        AcceptButton = apply;
        CancelButton = close;

        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 14, 20, 0) };
        layout.Dock = DockStyle.Top;
        layout.AutoSize = true;
        layout.ColumnCount = 1;
        layout.RowCount = 9;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 42, 44, 102, 30, 32, 32, 304, 44, 8 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(new Label { Text = "اعمال گروهی پروکسی", Dock = DockStyle.Fill, RightToLeft = RightToLeft.Yes,
            TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Accent }, 0, 0);
        layout.Controls.Add(new Label { Text = "آدرس و پورت را یک‌بار وارد کنید و سرویس‌های دلخواه را تیک بزنید. هر سرویس با دامنه معمول خودش تنظیم می‌شود و Reset مستقل خود را حفظ می‌کند.",
            Dock = DockStyle.Fill, RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleRight, ForeColor = Muted }, 0, 1);
        var endpoints = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        endpoints.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        endpoints.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        endpoints.Controls.Add(MainForm.EndpointCard("HTTP", httpHost, httpPort), 0, 0);
        endpoints.Controls.Add(MainForm.EndpointCard("HTTPS", httpsHost, httpsPort), 1, 0);
        layout.Controls.Add(endpoints, 0, 2);
        same.Text = "استفاده از همین پروکسی برای HTTPS";
        same.RightToLeft = RightToLeft.Yes;
        same.Dock = DockStyle.Fill;
        same.CheckedChanged += (_, _) =>
        {
            httpsHost.Enabled = httpsPort.Enabled = !same.Checked;
            if (same.Checked) MirrorHttp();
        };
        httpHost.TextChanged += (_, _) => { if (same.Checked) MirrorHttp(); };
        httpPort.TextChanged += (_, _) => { if (same.Checked) MirrorHttp(); };
        layout.Controls.Add(same, 0, 3);
        bypass.ExpansionChanged += (_, _) => UpdateBypassVisibility();
        layout.Controls.Add(bypass, 0, 4);

        var selection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        all.AutoCheck = false;
        all.ThreeState = true;
        all.Text = "انتخاب همه";
        all.Dock = DockStyle.Fill;
        all.RightToLeft = RightToLeft.Yes;
        all.Click += (_, _) => SelectAll(all.CheckState != CheckState.Checked);
        count.Dock = DockStyle.Fill;
        count.RightToLeft = RightToLeft.Yes;
        count.TextAlign = ContentAlignment.MiddleRight;
        count.ForeColor = Muted;
        selection.Controls.Add(count, 0, 0);
        selection.Controls.Add(all, 1, 0);
        layout.Controls.Add(selection, 0, 5);

        targets.Dock = DockStyle.Fill;
        targets.AutoScroll = true;
        targets.BackColor = Color.White;
        targets.Padding = new Padding(4, 2, 4, 2);
        targets.Margin = Padding.Empty;
        var selected = selectedIds.ToHashSet();
        foreach (var service in services)
        {
            var card = new Panel { Height = 54, Margin = new Padding(3), BackColor = Color.White };
            var check = new CheckBox { Text = service.Name, Checked = selected.Contains(service.Id),
                Dock = DockStyle.Top, Height = 26, AutoEllipsis = true, Padding = new Padding(4, 0, 0, 0) };
            check.CheckedChanged += (_, _) => UpdateSelection();
            card.Controls.Add(new Label { Text = service.ScopeLabel, Dock = DockStyle.Bottom, Height = 24,
                Font = new Font("Segoe UI", 8), ForeColor = Muted, RightToLeft = RightToLeft.Yes,
                TextAlign = ContentAlignment.TopRight, AutoEllipsis = true, Padding = new Padding(4, 0, 6, 0) });
            card.Controls.Add(check);
            targets.Controls.Add(card);
            choices.Add((service, check));
        }
        targets.SizeChanged += (_, _) =>
        {
            var width = Math.Max(240, (targets.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 30) / 3);
            foreach (Control card in targets.Controls) card.Width = width;
        };
        layout.Controls.Add(targets, 0, 6);
        layout.Controls.Add(new Label { Text = "Git، pip، Cargo و Windows PowerShell پروکسی مشترک HTTP/HTTPS می‌خواهند. بخش‌های Windows و User environment دامنه مشترک دارند؛ WinHTTP نیازمند Administrator است.",
            Dock = DockStyle.Fill, ForeColor = Muted, RightToLeft = RightToLeft.Yes,
            TextAlign = ContentAlignment.MiddleRight }, 0, 7);
        scroller.Controls.Add(layout);
        Controls.Add(scroller);
        Controls.Add(footer);
        httpHost.Text = initial.Http.Host;
        httpPort.Text = initial.Http.Port.ToString();
        httpsHost.Text = initial.Https.Host;
        httpsPort.Text = initial.Https.Port.ToString();
        bypass.BypassText = initial.Bypass;
        same.Checked = initial.SameForHttps;
        UpdateSelection();
    }

    private void MirrorHttp() { httpsHost.Text = httpHost.Text; httpsPort.Text = httpPort.Text; }

    private void SelectAll(bool selected)
    {
        updating = true;
        try { foreach (var choice in choices) choice.Check.Checked = selected; }
        finally { updating = false; }
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (updating) return;
        var selected = choices.Count(c => c.Check.Checked);
        all.CheckState = selected == 0 ? CheckState.Unchecked : selected == choices.Count ? CheckState.Checked : CheckState.Indeterminate;
        count.Text = $"{selected} از {choices.Count} سرویس انتخاب شده";
        apply.Enabled = selected > 0;
        UpdateBypassVisibility();
    }

    private void UpdateBypassVisibility()
    {
        var available = choices.Any(c => c.Check.Checked &&
            (c.Service.IsSession || c.Service.Kind == AdapterKind.UserEnvironment));
        bypass.Visible = bypass.Enabled = available;
        layout.RowStyles[4].Height = available ? bypass.RequiredHeight : 0;
    }

    private void ApplySelected()
    {
        UseWaitCursor = true;
        apply.Enabled = false;
        try
        {
            var http = ProxyEndpoint.Parse(httpHost.Text, httpPort.Text);
            var https = same.Checked ? http : ProxyEndpoint.Parse(httpsHost.Text, httpsPort.Text);
            var profile = new ProxyProfile(http, https, same.Checked, bypass.BypassText.Trim());
            LastResults = applier.Apply(choices.Where(c => c.Check.Checked).Select(c => c.Service), profile);
            var successful = LastResults.Count(r => r.Success);
            results.ForeColor = successful == LastResults.Count ? Accent : Color.DarkRed;
            results.Text = $"نتیجه: {successful} موفق، {LastResults.Count - successful} ناموفق.\r\n" +
                string.Join("\r\n", LastResults.Select(r => r.Success
                    ? $"✓ {r.Service.Name} — " + (r.Service.IsSession ? "پروفایل ذخیره شد؛ اجرا از داخل برنامه" : "تنظیمات اعمال شد")
                    : $"✗ {r.Service.Name} — {r.Error?.Replace("\r", "").Replace("\n", " ")}"));
        }
        catch (Exception exception)
        {
            LastResults = [];
            results.ForeColor = Color.DarkRed;
            results.Text = exception.Message;
        }
        finally { UseWaitCursor = false; UpdateSelection(); }
    }

    internal void VerifyPreview(ChangeManager manager, string directory)
    {
        SelectAll(false);
        if (apply.Enabled) throw new InvalidOperationException("Empty batch selection must be disabled.");
        var chosen = choices.First(c => c.Service.Id == "cmd");
        chosen.Check.Checked = true;
        choices.First(c => c.Service.Id == "git").Check.Checked = true;
        choices.First(c => c.Service.Id == "npm").Check.Checked = true;
        if (all.CheckState != CheckState.Indeterminate) throw new InvalidOperationException("Mixed selection state failed.");
        same.Checked = true;
        httpPort.Text = "۸۱۲۵";
        apply.PerformClick();
        if (LastResults.Count != 3 || LastResults.Any(r => !r.Success) || manager.State.Changes.Count != 3 ||
            manager.State.Profiles["git"].Http.Port != 8125)
            throw new InvalidOperationException("Batch UI apply failed.");
        Application.DoEvents();
        using (var bitmap = new Bitmap(Width, Height))
        {
            DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
            bitmap.Save(Path.Combine(directory, "batch.png"));
        }
        manager.Reset("cmd");
        if (!manager.State.Changes.ContainsKey("git") || !manager.State.Changes.ContainsKey("npm"))
            throw new InvalidOperationException("Batch UI independent Reset failed.");
        manager.Reset("git");
        manager.Reset("npm");
        same.Checked = false;
        httpsPort.Text = "9000";
        apply.PerformClick();
        if (LastResults.Count != 0 || manager.State.Changes.Count != 0)
            throw new InvalidOperationException("Incompatible batch must not write any settings.");
        SelectAll(true);
        if (SelectedIds.Count != choices.Count || all.CheckState != CheckState.Checked)
            throw new InvalidOperationException("Batch select all failed.");
        SelectAll(false);
    }
}

using System.Diagnostics;
using ProxyGAyer.Core;

namespace ProxyGAyer;

public sealed class MainForm : Form
{
    // Change this address to update the footer's email link.
    private const string ContactEmail = "example@gmail.com";
    private static readonly Color Ink = Color.FromArgb(27, 37, 57);
    private static readonly Color Muted = Color.FromArgb(101, 114, 134);
    private static readonly Color Accent = Color.FromArgb(0, 116, 109);
    private static readonly Color Canvas = Color.FromArgb(245, 247, 250);
    private readonly ChangeManager manager;
    private readonly ConfigPlanner planner;
    private readonly StateStore store;
    private readonly ListBox services = new();
    private readonly TextBox search = new();
    private readonly Label title = new();
    private readonly Label description = new();
    private readonly Label status = new();
    private readonly Label mode = new();
    private readonly TextBox httpHost = new();
    private readonly TextBox httpPort = new();
    private readonly TextBox httpsHost = new();
    private readonly TextBox httpsPort = new();
    private readonly ProxyBypassEditor bypass = new();
    private readonly CheckBox same = new();
    private readonly TextBox testUrl = new();
    private readonly Button set = new();
    private readonly Button reset = new();
    private readonly Button launch = new();
    private readonly Button test = new();
    private readonly TableLayoutPanel body = new();
    private readonly CancellationTokenSource lifetime = new();
    private ServiceDefinition? current;
    private HashSet<string>? batchSelection;
    private bool loading;

    public MainForm(ChangeManager manager, ConfigPlanner planner, StateStore store)
    {
        this.manager = manager;
        this.planner = planner;
        this.store = store;
        Text = "ProxyGAyer — Proxy Manager";
        Icon = AppBranding.Icon;
        Font = new Font("Segoe UI", 10F);
        ForeColor = Ink;
        BackColor = Canvas;
        ClientSize = new Size(1160, 838);
        MinimumSize = new Size(1040, 810);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        var sidebar = BuildSidebar();
        sidebar.Dock = DockStyle.Right;
        sidebar.Width = 274;
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(28, 20, 28, 20) };
        body.Dock = DockStyle.Top;
        body.AutoSize = true;
        body.ColumnCount = 1;
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowCount = 11;
        foreach (var height in new[] { 62, 82, 106, 36, 106, 32, 82, 60, 94, 28, 12 })
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        scroller.Controls.Add(body);
        Controls.Add(scroller);
        Controls.Add(sidebar);
        var credit = new LinkLabel
        {
            Text = "Copyleft by Crazy", Dock = DockStyle.Bottom, Height = 26,
            Font = new Font("Segoe UI", 8), TextAlign = ContentAlignment.MiddleCenter,
            LinkColor = Muted, ActiveLinkColor = Accent, VisitedLinkColor = Muted,
            LinkBehavior = LinkBehavior.HoverUnderline,
            AccessibleDescription = "Open your email app to contact " + ContactEmail
        };
        credit.LinkClicked += (_, _) => Execute(() =>
        {
            using var process = Process.Start(new ProcessStartInfo("mailto:" + ContactEmail) { UseShellExecute = true });
        });
        Controls.Add(credit);
        BuildEditor();
        PopulateServices();
        FormClosing += (_, _) => lifetime.Cancel();
        AcceptButton = set;
    }

    private Panel BuildSidebar()
    {
        var panel = new Panel { BackColor = Color.White, Padding = new Padding(18, 18, 18, 16) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        foreach (var height in new[] { 43, 34, 44, 46, -1, 46, 52 })
            layout.RowStyles.Add(height == -1 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "ProxyGAyer", Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Accent,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(new Label { Text = "مدیریت پروکسی ابزارهای شما", ForeColor = Muted, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight, RightToLeft = RightToLeft.Yes }, 0, 1);
        search.Dock = DockStyle.Fill;
        search.PlaceholderText = "جست‌وجوی ابزار…";
        search.RightToLeft = RightToLeft.Yes;
        search.Margin = new Padding(0, 5, 0, 10);
        search.TextChanged += (_, _) => PopulateServices();
        layout.Controls.Add(search, 0, 2);
        var batch = MakeButton("Batch set · اعمال گروهی", true);
        batch.Dock = DockStyle.Fill;
        batch.Margin = new Padding(0, 0, 0, 8);
        batch.Click += (_, _) => OpenBatch();
        layout.Controls.Add(batch, 0, 3);

        services.Dock = DockStyle.Fill;
        services.BorderStyle = BorderStyle.None;
        services.DrawMode = DrawMode.OwnerDrawFixed;
        services.ItemHeight = 48;
        services.IntegralHeight = false;
        services.Margin = Padding.Empty;
        services.DrawItem += DrawService;
        services.SelectedIndexChanged += (_, _) =>
        {
            if (services.SelectedItem is ServiceDefinition service) SelectService(service);
        };
        layout.Controls.Add(services, 0, 4);
        var add = MakeButton("+ Add application", false);
        add.Dock = DockStyle.Fill;
        add.Margin = new Padding(0, 7, 0, 4);
        add.Click += (_, _) => AddApplication();
        layout.Controls.Add(add, 0, 5);
        var all = MakeButton("Reset All", false);
        all.ForeColor = Color.FromArgb(177, 53, 68);
        all.Dock = DockStyle.Fill;
        all.Margin = new Padding(0, 8, 0, 0);
        all.Click += (_, _) => ResetAll();
        layout.Controls.Add(all, 0, 6);
        panel.Controls.Add(layout);
        return panel;
    }

    private void BuildEditor()
    {
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        title.Dock = DockStyle.Fill;
        title.Font = new Font("Segoe UI", 26, FontStyle.Bold);
        title.TextAlign = ContentAlignment.MiddleLeft;
        mode.Dock = DockStyle.Fill;
        mode.TextAlign = ContentAlignment.MiddleRight;
        mode.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        mode.ForeColor = Accent;
        heading.Controls.Add(title, 0, 0);
        heading.Controls.Add(mode, 1, 0);
        body.Controls.Add(heading, 0, 0);
        description.Dock = DockStyle.Fill;
        description.RightToLeft = RightToLeft.Yes;
        description.ForeColor = Muted;
        description.Padding = new Padding(0, 8, 0, 4);
        description.TextAlign = ContentAlignment.TopRight;
        body.Controls.Add(description, 0, 1);
        body.Controls.Add(EndpointCard("HTTP", httpHost, httpPort), 0, 2);

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
        body.Controls.Add(same, 0, 3);
        body.Controls.Add(EndpointCard("HTTPS", httpsHost, httpsPort), 0, 4);

        bypass.ExpansionChanged += (_, _) => UpdateBypassVisibility();
        body.Controls.Add(bypass, 0, 5);

        var testing = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 10, 0, 2) };
        testing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        testing.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134));
        testing.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        testing.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        testing.Controls.Add(new Label { Text = "TEST URL  /  آزمون مستقیم پروکسی با مقادیر فرم", ForeColor = Muted, Dock = DockStyle.Fill }, 0, 0);
        testing.SetColumnSpan(testing.GetControlFromPosition(0, 0)!, 2);
        testUrl.Text = "https://google.com/";
        testUrl.Dock = DockStyle.Fill;
        testing.Controls.Add(testUrl, 0, 1);
        ConfigureButton(test, "Test connection", false);
        test.Dock = DockStyle.Fill;
        test.Margin = new Padding(8, 0, 0, 4);
        test.Click += async (_, _) => await TestConnection();
        testing.Controls.Add(test, 1, 1);
        body.Controls.Add(testing, 0, 6);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(0, 10, 0, 0), Margin = Padding.Empty };
        ConfigureButton(set, "Set proxy", true);
        ConfigureButton(reset, "Reset", false);
        ConfigureButton(launch, "Open terminal", false);
        set.Click += (_, _) => Apply();
        reset.Click += (_, _) => ResetCurrent();
        launch.Click += (_, _) => Launch();
        actions.Controls.AddRange([set, reset, launch]);
        body.Controls.Add(actions, 0, 7);

        status.Dock = DockStyle.Fill;
        status.BackColor = Color.White;
        status.ForeColor = Muted;
        status.RightToLeft = RightToLeft.Yes;
        status.Padding = new Padding(16, 12, 16, 8);
        status.TextAlign = ContentAlignment.TopRight;
        status.Margin = new Padding(0, 8, 0, 0);
        body.Controls.Add(status, 0, 8);
        body.Controls.Add(new Label { Text = "آدرس و پورت یک پروکسی HTTP فعال را از تنظیمات ابزار اتصال خود بردارید.", RightToLeft = RightToLeft.Yes,
            Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8), ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleRight }, 0, 9);
    }

    internal static Panel EndpointCard(string protocol, TextBox host, TextBox port)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 4, 0, 4), Padding = new Padding(16, 10, 16, 8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var protocolLabel = new Label { Text = protocol, Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Accent, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(protocolLabel, 0, 0);
        layout.SetRowSpan(protocolLabel, 2);
        layout.Controls.Add(new Label { Text = "HOST / IP", ForeColor = Muted, Dock = DockStyle.Fill }, 1, 0);
        layout.Controls.Add(new Label { Text = "PORT", ForeColor = Muted, Dock = DockStyle.Fill }, 2, 0);
        host.Dock = port.Dock = DockStyle.Fill;
        host.Font = port.Font = new Font("Consolas", 12);
        host.Margin = new Padding(0, 0, 12, 0);
        port.Margin = Padding.Empty;
        host.PlaceholderText = "127.0.0.1";
        port.PlaceholderText = "7890";
        port.MaxLength = 5;
        layout.Controls.Add(host, 1, 1);
        layout.Controls.Add(port, 2, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private static Button MakeButton(string text, bool primary)
    {
        var button = new Button();
        ConfigureButton(button, text, primary);
        return button;
    }
    internal static void ConfigureButton(Button button, string text, bool primary)
    {
        button.Text = text;
        button.Size = new Size(text.Length > 12 ? 164 : 124, 38);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(213, 220, 228);
        button.BackColor = primary ? Accent : Color.White;
        button.ForeColor = primary ? Color.White : Ink;
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 10, 0);
    }

    private void DrawService(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var service = (ServiceDefinition)services.Items[e.Index];
        var selected = e.State.HasFlag(DrawItemState.Selected);
        using var brush = new SolidBrush(selected ? Color.FromArgb(230, 244, 242) : Color.White);
        e.Graphics.FillRectangle(brush, e.Bounds);
        if (selected)
        {
            using var stripe = new SolidBrush(Accent);
            e.Graphics.FillRectangle(stripe, e.Bounds.Right - 4, e.Bounds.Top + 5, 3, e.Bounds.Height - 10);
        }
        var active = manager.State.Changes.ContainsKey(service.Id);
        TextRenderer.DrawText(e.Graphics, service.Name, Font, new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 4, e.Bounds.Width - 25, 24),
            selected ? Accent : Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var small = new Font("Segoe UI", 7.5F);
        TextRenderer.DrawText(e.Graphics, service.Group + (active ? "  •  SET" : ""), small,
            new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 28, e.Bounds.Width - 22, 16), Muted, TextFormatFlags.Left);
    }

    private void PopulateServices()
    {
        var selectedId = current?.Id;
        services.BeginUpdate();
        services.Items.Clear();
        foreach (var service in ServiceCatalog.All.Concat(manager.State.CustomServices)
            .Where(s => s.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase))) services.Items.Add(service);
        services.EndUpdate();
        var index = services.Items.Cast<ServiceDefinition>().ToList().FindIndex(s => s.Id == selectedId);
        if (services.Items.Count > 0) services.SelectedIndex = index >= 0 ? index : 0;
    }

    private void SelectService(ServiceDefinition service)
    {
        if (loading) return;
        loading = true;
        try
        {
            current = service;
            title.Text = service.Name;
            // Long custom executable names remain legible without changing the layout.
            title.Font = new Font("Segoe UI", service.Name.Length > 21 ? 18 : 26, FontStyle.Bold);
            description.Text = service.Description;
            mode.Text = service.ScopeTag;
            var profile = manager.State.Profiles.GetValueOrDefault(service.Id, ProxyProfile.Default);
            same.Checked = false;
            httpHost.Text = profile.Http.Host;
            httpPort.Text = profile.Http.Port.ToString();
            httpsHost.Text = profile.Https.Host;
            httpsPort.Text = profile.Https.Port.ToString();
            bypass.BypassText = profile.Bypass;
            same.Checked = service.SingleProxy || profile.SameForHttps;
            same.Enabled = !service.SingleProxy;
            same.Text = service.SingleProxy ? "این ابزار یک پروکسی مشترک برای HTTP و HTTPS می‌پذیرد" : "استفاده از همین پروکسی برای HTTPS";
            bypass.SetExpanded(false);
            UpdateBypassVisibility();
            launch.Visible = service.IsSession;
            launch.Text = service.Executable is null || service.Group == "CUSTOM APPS" ? "Launch application" : "Open terminal";
            RefreshStatus();
        }
        finally { loading = false; }
    }

    private void MirrorHttp() { httpsHost.Text = httpHost.Text; httpsPort.Text = httpPort.Text; }

    private void UpdateBypassVisibility()
    {
        var available = current?.IsSession == true || current?.Kind == AdapterKind.UserEnvironment;
        bypass.Visible = bypass.Enabled = available;
        body.RowStyles[5].Height = available ? bypass.RequiredHeight : 0;
    }

    private ProxyProfile ReadProfile()
    {
        var http = ProxyEndpoint.Parse(httpHost.Text, httpPort.Text);
        var https = same.Checked ? http : ProxyEndpoint.Parse(httpsHost.Text, httpsPort.Text);
        var profile = new ProxyProfile(http, https, same.Checked, bypass.BypassText.Trim());
        profile.Validate();
        return profile;
    }

    private void Apply()
    {
        if (current is null) return;
        Execute(() =>
        {
            var profile = ReadProfile();
            manager.Apply(current.Id, profile, planner.Plan(current, profile));
            RefreshStatus();
        });
    }

    private void OpenBatch()
    {
        var previous = current is null ? null : manager.State.Profiles.GetValueOrDefault(current.Id);
        var initial = previous ?? ProxyProfile.Default;
        try { initial = ReadProfile(); }
        catch (ArgumentException) { /* Invalid drafts can be corrected in the batch form. */ }
        using var dialog = new BatchApplyForm(new BatchProxyApplier(manager, planner),
            ServiceCatalog.All.Concat(manager.State.CustomServices).ToArray(), initial,
            batchSelection ?? (current is null ? [] : new HashSet<string> { current.Id }));
        dialog.ShowDialog(this);
        batchSelection = dialog.SelectedIds.ToHashSet();
        // Refresh applied values while keeping untouched drafts when cancelled.
        if (current is { } service && manager.State.Profiles.GetValueOrDefault(service.Id) != previous)
            SelectService(service);
        else RefreshStatus();
    }

    private void ResetCurrent()
    {
        if (current is null) return;
        Execute(() =>
        {
            manager.Reset(current.Id);
            RefreshStatus();
            status.Text = current.IsSession
                ? "پروفایل غیرفعال شد. ترمینال‌ها و برنامه‌هایی که قبلاً باز شده‌اند باید بسته شوند."
                : "تنظیمات پیش از اولین Set بازگردانده شد. ممکن است لازم باشد ابزار را دوباره اجرا کنید.";
        });
    }

    private void ResetAll()
    {
        if (manager.State.Changes.Count == 0) { status.Text = "تنظیمی برای بازگرداندن وجود ندارد."; return; }
        var failures = new List<string>();
        foreach (var id in manager.State.Changes.Keys.ToArray())
        {
            try { manager.Reset(id); }
            catch (Exception exception) { failures.Add(id + ": " + exception.Message); }
        }
        RefreshStatus();
        status.Text = failures.Count == 0
            ? "همه تغییرات این برنامه بازگردانده شد. ترمینال‌ها و برنامه‌های قبلی را ببندید و دوباره باز کنید."
            : $"{failures.Count} بخش بازگردانده نشد؛ پشتیبان آن‌ها حفظ شده است.";
        if (failures.Count > 0) ShowError(string.Join("\n\n", failures));
    }

    private void RefreshStatus()
    {
        if (current is null) return;
        var active = manager.State.Changes.TryGetValue(current.Id, out var change);
        reset.Enabled = active;
        launch.Enabled = active && change?.Pending == false;
        status.ForeColor = change?.Pending == true ? Color.DarkRed : Muted;
        status.Text = change?.Pending == true
            ? "عملیات قبلی کامل نشده است. برای بازیابی تنظیمات، Reset را اجرا کنید."
            : active
                ? current.IsSession ? "پروفایل ذخیره شد. برای استفاده، دکمه باز کردن ترمینال یا اجرای برنامه را بزنید. نتیجه اتصال هنوز بررسی نشده است."
                : "تنظیمات توسط این برنامه اعمال شده است. این وضعیت به معنی تأیید اتصال نیست؛ برای بررسی از Test استفاده کنید."
                : "این بخش هنوز توسط برنامه تنظیم نشده است. آدرس و پورت پروکسی فعال خود را وارد کنید و Set را بزنید.";
        services.Invalidate();
    }

    private void Launch()
    {
        if (current is null) return;
        Execute(() =>
        {
            string? executable = null;
            if (current.Executable is null)
            {
                using var picker = new OpenFileDialog { Filter = "Windows application (*.exe)|*.exe", Title = "انتخاب برنامه" };
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                executable = picker.FileName;
            }
            using var process = Process.Start(SessionLauncher.CreateStartInfo(current, manager.State.Profiles[current.Id], executable));
            status.Text = "برنامه با پروفایل ذخیره‌شده اجرا شد. تغییرات ذخیره‌نشده فرم در این اجرا استفاده نمی‌شوند.";
        });
    }

    private async Task TestConnection()
    {
        var testedServiceId = current?.Id;
        try
        {
            var profile = ReadProfile();
            test.Enabled = false;
            status.ForeColor = Muted;
            status.Text = "در حال ارسال درخواست از مسیر پروکسی…";
            var result = await ProxyTester.TestAsync(profile, testUrl.Text.Trim(), lifetime.Token);
            if (!IsDisposed && current?.Id == testedServiceId) status.Text = result;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsDisposed && current?.Id == testedServiceId)
            { status.ForeColor = Color.DarkRed; status.Text = "تست ناموفق: " + exception.Message; }
        }
        finally { if (!IsDisposed) test.Enabled = true; }
    }

    private void AddApplication()
    {
        using var picker = new OpenFileDialog { Filter = "Windows application (*.exe)|*.exe", Title = "افزودن برنامه با پروفایل مستقل" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        Execute(() =>
        {
            var service = new ServiceDefinition("custom-" + Guid.NewGuid().ToString("N"),
                Path.GetFileNameWithoutExtension(picker.FileName), "CUSTOM APPS", AdapterKind.Session,
                "پروفایل مستقل برای این برنامه. برنامه با متغیرهای HTTP_PROXY و HTTPS_PROXY اجرا می‌شود؛ باید این متغیرها را پشتیبانی کند.", picker.FileName);
            manager.State.CustomServices.Add(service);
            store.Save(manager.State);
            search.Clear();
            PopulateServices();
            services.SelectedItem = service;
        });
    }

    private void Execute(Action action)
    {
        UseWaitCursor = true;
        try { action(); }
        catch (Exception exception) { ShowError(exception.Message); }
        finally { UseWaitCursor = false; }
    }

    private void ShowError(string message) => MessageBox.Show(this, message, "ProxyGAyer", MessageBoxButtons.OK,
        MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

    internal void ExportPreviews(string directory)
    {
        foreach (var service in ServiceCatalog.All)
        {
            services.SelectedItem = service;
            Application.DoEvents();
            if (service.Id is "cmd" or "git" or "windows")
            {
                using var bitmap = new Bitmap(Width, Height);
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(Path.Combine(directory, service.Id == "cmd" ? "preview.png" : service.Id + ".png"));
                if (service.Id == "cmd")
                {
                    bypass.SetExpanded(true);
                    Application.DoEvents();
                    using var advanced = new Bitmap(Width, Height);
                    DrawToBitmap(advanced, new Rectangle(0, 0, Width, Height));
                    advanced.Save(Path.Combine(directory, "advanced.png"));
                    bypass.SetExpanded(false);
                }
            }
            // All resources are in memory in preview mode; exercise the actual UI
            // wiring without touching any user's tool configuration or launching apps.
            httpPort.Text = "8123";
            if (!service.SingleProxy) { same.Checked = false; httpsPort.Text = "8124"; }
            set.PerformClick();
            if (!manager.State.Changes.ContainsKey(service.Id) || manager.State.Profiles[service.Id].Http.Port != 8123 ||
                manager.State.Profiles[service.Id].Https.Port != (service.SingleProxy ? 8123 : 8124))
                throw new InvalidOperationException("Preview Set flow failed for " + service.Id);
            if (launch.Visible && !launch.Enabled) throw new InvalidOperationException("Launch not enabled: " + service.Id);
            reset.PerformClick();
            if (manager.State.Changes.ContainsKey(service.Id)) throw new InvalidOperationException("Preview Reset flow failed: " + service.Id);
        }
        foreach (var service in ServiceCatalog.All.Take(4))
        {
            services.SelectedItem = service;
            set.PerformClick();
        }
        if (manager.State.Changes.Count != 4) throw new InvalidOperationException("Reset All setup failed.");
        ResetAll();
        if (manager.State.Changes.Count != 0) throw new InvalidOperationException("Reset All left active sections.");
        using (var batch = new BatchApplyForm(new BatchProxyApplier(manager, planner),
            ServiceCatalog.All.Concat(manager.State.CustomServices).ToArray(), ProxyProfile.Default, []))
        {
            batch.Opacity = 0;
            batch.Show(this);
            Application.DoEvents();
            batch.VerifyPreview(manager, directory);
            batch.Close();
        }
        File.WriteAllText(Path.Combine(directory, "ui-checks.txt"),
            $"PASS: Set / Reset / endpoint bindings for {ServiceCatalog.All.Count} sections.\nPASS: Reset All restores multiple active sections.\n" +
            "PASS: Batch selection / select all / apply / independent Reset / incompatible endpoint validation.\n");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) lifetime.Dispose();
        base.Dispose(disposing);
    }
}

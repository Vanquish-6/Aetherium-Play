using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace AcLegacyLauncher;

internal sealed record AethTankAddonPresentation(
    bool Available, bool Installed, bool Enabled, bool GameRunning,
    string? CurrentVersion, string? PreviousVersion, string Message, bool ManagedStateExists = false);

/// <summary>Player controls for the independently released, optional native add-on.</summary>
internal sealed class AethTankAddonForm : Form
{
    private readonly AethTankAddonManager? manager;
    private readonly bool preview;
    private const string ActivityPlaceholder = "Update checks, signature verification, and install activity will appear here.";
    private readonly Label versionLabel = new();
    private readonly Label statusLabel = new();
    private readonly Label statePill = new();
    private readonly AddonToggle enabledCheck = new();
    private readonly AddonButton checkButton = new();
    private readonly AddonButton installButton = new() { Kind = AddonButtonKind.Primary };
    private readonly AddonButton rollbackButton = new();
    private readonly AddonButton uninstallButton = new() { Kind = AddonButtonKind.Danger };
    private readonly AddonButton closeButton = new();
    private readonly TextBox activity = new();
    private AethTankAddonPresentation state;
    private AethTankRelease? candidate;
    private CancellationTokenSource? operation;
    private bool refreshing, busy, closeRequested;

    internal AethTankAddonForm(AethTankAddonManager? manager = null, AethTankRelease? initialRelease = null)
        : this(manager ?? AethTankAddonManager.Default, initialRelease, null)
    {
    }

    private AethTankAddonForm(AethTankAddonManager? manager, AethTankRelease? initialRelease,
        AethTankAddonPresentation? previewState)
    {
        this.manager = manager;
        candidate = initialRelease;
        preview = previewState is not null;
        state = previewState ?? ReadPresentation(manager!);
        Text = "Aetherium Play — Aeth-Tank";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5F);
        BackColor = AddonPalette.Background;
        ForeColor = AddonPalette.Text;
        ClientSize = new Size(820, 580);
        MinimumSize = new Size(720, 540);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AddonPalette.Background
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 244));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(shell);

        var identity = new AddonCard
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 18, 0),
            Padding = new Padding(22),
            Accent = true
        };
        shell.Controls.Add(identity, 0, 0);
        var identityLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 7
        };
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        identity.Controls.Add(identityLayout);
        identityLayout.Controls.Add(new AddonMark { Dock = DockStyle.Left }, 0, 0);
        identityLayout.Controls.Add(new Label
        {
            Text = "Aeth-Tank", Font = new Font("Segoe UI", 20F, FontStyle.Bold),
            ForeColor = AddonPalette.Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        identityLayout.Controls.Add(new Label
        {
            Text = "A focused combat companion for the classic Dark Majesty client. Powerful when you want it, paused when you do not.",
            ForeColor = AddonPalette.Muted, Dock = DockStyle.Fill
        }, 0, 2);
        identityLayout.Controls.Add(new Label
        {
            Text = "INCLUDED", Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = AddonPalette.Gold, Dock = DockStyle.Fill
        }, 0, 3);
        identityLayout.Controls.Add(new Label
        {
            Text = "✓  Melee, missile, and spell combat\r\n✓  Healing and buff profiles\r\n✓  Looting and salvaging\r\n✓  Per-character settings\r\n✓  Signed, verified updates",
            ForeColor = AddonPalette.Text, Dock = DockStyle.Fill
        }, 0, 4);
        identityLayout.Controls.Add(new Label
        {
            Text = "SAFE BY DEFAULT\r\nThe add-on starts paused. Installing it never changes your character settings.",
            Font = new Font("Segoe UI", 8.5F), ForeColor = AddonPalette.Muted,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft
        }, 0, 6);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Transparent,
            ColumnCount = 1, RowCount = 6
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        shell.Controls.Add(content, 1, 0);

        var statusCard = new AddonCard
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(18, 14, 18, 12)
        };
        content.Controls.Add(statusCard, 0, 0);
        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 3
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        statusCard.Controls.Add(statusLayout);
        statusLayout.Controls.Add(new Label
        {
            Text = "INSTALLATION", Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = AddonPalette.Muted, Dock = DockStyle.Fill
        }, 0, 0);
        statePill.Dock = DockStyle.Fill;
        statePill.TextAlign = ContentAlignment.MiddleCenter;
        statePill.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        statePill.Margin = new Padding(8, 0, 0, 2);
        statusLayout.Controls.Add(statePill, 1, 0);
        versionLabel.Dock = DockStyle.Fill;
        versionLabel.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        versionLabel.ForeColor = AddonPalette.Text;
        statusLayout.Controls.Add(versionLabel, 0, 1);
        statusLayout.SetColumnSpan(versionLabel, 2);
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = AddonPalette.Muted;
        statusLabel.AutoEllipsis = true;
        statusLayout.Controls.Add(statusLabel, 0, 2);
        statusLayout.SetColumnSpan(statusLabel, 2);

        var enableCard = new AddonCard
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(10, 7, 10, 7)
        };
        content.Controls.Add(enableCard, 0, 1);
        enabledCheck.Text = "Load Aeth-Tank with the classic client";
        enabledCheck.Subtitle = "Changes apply the next time the game starts.";
        enabledCheck.Dock = DockStyle.Fill;
        enabledCheck.AutoSize = false;
        enabledCheck.CheckedChanged += EnableChoiceChanged;
        enableCard.Controls.Add(enabledCheck);

        var actionsCard = new AddonCard
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(8)
        };
        content.Controls.Add(actionsCard, 0, 2);
        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 2
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        actionsCard.Controls.Add(actions);
        ConfigureButton(checkButton, "Check for updates");
        ConfigureButton(installButton, "Install");
        ConfigureButton(rollbackButton, "Roll back");
        ConfigureButton(uninstallButton, "Uninstall");
        actions.Controls.Add(checkButton, 0, 0);
        actions.Controls.Add(installButton, 1, 0);
        actions.Controls.Add(rollbackButton, 0, 1);
        actions.Controls.Add(uninstallButton, 1, 1);

        content.Controls.Add(new Label
        {
            Text = "ACTIVITY", Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = AddonPalette.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 3);
        activity.Dock = DockStyle.Fill;
        activity.Multiline = true;
        activity.ReadOnly = true;
        activity.ScrollBars = ScrollBars.Vertical;
        activity.BackColor = AddonPalette.Inset;
        activity.ForeColor = AddonPalette.Muted;
        activity.BorderStyle = BorderStyle.None;
        activity.Padding = new Padding(8);
        activity.Text = ActivityPlaceholder;
        activity.TabStop = false;
        content.Controls.Add(activity, 0, 4);
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            Margin = Padding.Empty, BackColor = Color.Transparent
        };
        ConfigureButton(closeButton, "Close");
        closeButton.Size = new Size(108, 36);
        closeButton.Dock = DockStyle.None;
        closeButton.Margin = new Padding(0, 10, 0, 0);
        closeButton.Click += (_, _) => { if (busy) operation?.Cancel(); else Close(); };
        footer.Controls.Add(closeButton);
        content.Controls.Add(footer, 0, 5);
        CancelButton = closeButton;
        checkButton.Click += async (_, _) => await CheckAsync();
        installButton.Click += async (_, _) => await InstallAsync();
        rollbackButton.Click += async (_, _) => await MutateAsync(() => manager!.Rollback(),
            "Previous version restored. It will be used on the next launch.");
        uninstallButton.Click += async (_, _) =>
        {
            if (preview || manager is null) return;
            if (MessageBox.Show(this,
                "Remove Aeth-Tank? Your character settings will be kept.",
                "Uninstall Aeth-Tank", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                await MutateAsync(() => manager.Uninstall(), "Aeth-Tank removed. Character settings were kept.");
        };
        Shown += async (_, _) =>
        {
            if (!preview && state.Available && candidate is null) await CheckAsync();
        };
        FormClosing += (_, e) =>
        {
            if (!busy) return;
            closeRequested = true;
            operation?.Cancel();
            e.Cancel = true;
        };
        ApplyState();
    }

    private static AethTankAddonPresentation ReadPresentation(AethTankAddonManager manager)
    {
        var status = manager.GetStatus();
        return new(status.ReleaseConfigured, status.Installed, status.Enabled, status.GameRunning,
            status.CurrentVersion, status.PreviousVersion, status.Message, status.ManagedStateExists);
    }

    private static void ConfigureButton(AddonButton button, string text)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(4);
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = AddonPalette.SurfaceRaised;
        button.ForeColor = AddonPalette.Text;
        button.UseVisualStyleBackColor = false;
    }

    private void ApplyState()
    {
        refreshing = true;
        enabledCheck.Checked = state.Installed && state.Enabled;
        enabledCheck.Enabled = state.Installed && !busy;
        refreshing = false;
        versionLabel.Text = state.Installed ? $"Version {state.CurrentVersion}" : "Not installed";
        statePill.Text = state.GameRunning ? "GAME OPEN" : state.Installed
            ? state.Enabled ? "ENABLED" : "DISABLED"
            : state.Available ? "AVAILABLE" : "COMING SOON";
        statePill.ForeColor = state.GameRunning ? AddonPalette.Warning : state.Installed && state.Enabled
            ? AddonPalette.Green : state.Available ? AddonPalette.Gold : AddonPalette.Muted;
        statePill.BackColor = AddonPalette.Inset;
        statusLabel.Text = !state.Available && !state.Installed && !state.ManagedStateExists
            ? "Aeth-Tank is being prepared for release. Downloads will appear here when available."
            : state.GameRunning
                ? "Close all classic game clients before installing, updating, rolling back, or uninstalling."
                : candidate is not null
                    ? $"Version {candidate.Version} is ready to install ({FormatSize(candidate.PackageSize)})."
                    : state.Message;
        checkButton.Enabled = state.Available && !busy;
        installButton.Text = candidate is null ? "Install" : state.Installed
            ? $"Update to {candidate.Version}" : $"Install {candidate.Version}";
        installButton.Enabled = candidate is not null && !busy && !state.GameRunning;
        rollbackButton.Enabled = state.PreviousVersion is not null && !busy && !state.GameRunning;
        // Uninstall is also the recovery action for malformed local state.
        // Such a state cannot truthfully report an installed version.
        uninstallButton.Enabled = (state.Installed || state.ManagedStateExists) && !busy && !state.GameRunning;
        closeButton.Text = busy ? "Cancel" : "Close";
    }

    private static string FormatSize(long bytes) => bytes >= 1048576
        ? (bytes / 1048576D).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
        : Math.Ceiling(bytes / 1024D).ToString("0", CultureInfo.InvariantCulture) + " KB";

    private void Log(string message)
    {
        if (IsDisposed || Disposing) return;
        if (activity.Text == ActivityPlaceholder) activity.Clear();
        if (activity.TextLength > 12000) activity.Text = activity.Text[^6000..];
        activity.AppendText(message + Environment.NewLine);
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy || preview || manager is null) return;
        busy = true;
        operation = new CancellationTokenSource();
        ApplyState();
        try { await action(operation.Token); }
        catch (OperationCanceledException) { Log("Cancelled. The installed version was kept."); }
        catch (Exception ex) { Log(ex.Message); }
        finally
        {
            operation.Dispose();
            operation = null;
            busy = false;
            if (!IsDisposed && !Disposing)
            {
                try { state = ReadPresentation(manager); }
                catch (Exception ex) { Log(ex.Message); }
                ApplyState();
                if (closeRequested) Close();
            }
        }
    }

    private Task CheckAsync() => RunAsync(async token =>
    {
        Log("Checking for an Aeth-Tank release...");
        candidate = await manager!.CheckForUpdateAsync(token);
        Log(candidate is null ? "No newer release is available." : $"Version {candidate.Version} is available.");
    });

    private Task InstallAsync() => RunAsync(async token =>
    {
        if (candidate is null) return;
        await manager!.InstallAsync(candidate, new Progress<string>(Log), token);
        candidate = null;
        Log("Aeth-Tank installed. Launch the classic client to use it; the macro starts paused.");
    });

    private Task MutateAsync(Action action, string success) => RunAsync(async token =>
    {
        token.ThrowIfCancellationRequested();
        await Task.Run(action, token);
        candidate = null;
        Log(success);
    });

    private void EnableChoiceChanged(object? sender, EventArgs e)
    {
        if (refreshing || busy || preview || manager is null) return;
        try
        {
            manager.SetEnabled(enabledCheck.Checked);
            state = ReadPresentation(manager);
            Log(state.Enabled ? "Aeth-Tank will load on the next classic launch." :
                "Aeth-Tank will be disabled on the next classic launch.");
        }
        catch (Exception ex) { Log(ex.Message); state = ReadPresentation(manager); }
        ApplyState();
    }

    internal static async Task OfferOnceAsync(Form owner, CancellationToken cancellationToken)
    {
        try
        {
            var manager = AethTankAddonManager.Default;
            var status = manager.GetStatus();
            if (!status.ReleaseConfigured || status.ManagedStateExists || status.PromptDismissed ||
                status.DontAskAgain || status.GameRunning || cancellationToken.IsCancellationRequested) return;
            var release = await manager.CheckForUpdateAsync(cancellationToken);
            if (release is null || owner.IsDisposed || owner.Disposing || cancellationToken.IsCancellationRequested) return;
            // Recheck after the asynchronous download: another launcher or a game
            // may have changed the decision while the offer was being prepared.
            status = manager.GetStatus();
            if (status.ManagedStateExists || status.PromptDismissed || status.DontAskAgain || status.GameRunning) return;
            using var offer = new AethTankInstallOffer(release.Version, FormatSize(release.PackageSize));
            var result = offer.ShowDialog(owner);
            manager.RememberPrompt(result == DialogResult.Abort);
            if (result != DialogResult.Yes) return;
            using var dialog = new AethTankAddonForm(manager, release);
            dialog.Shown += async (_, _) => await dialog.InstallAsync();
            dialog.ShowDialog(owner);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Optional add-on availability must never block the ordinary launcher.
            Debug.WriteLine($"Aeth-Tank offer unavailable: {ex.Message}");
        }
    }

    internal static AethTankAddonForm CreatePreview(AethTankAddonPresentation presentation) =>
        new(null, null, presentation);

    internal void SavePreview(string path)
    {
        CreateControl();
        Show();
        PerformLayout();
        Application.DoEvents();
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Hide();
    }

    internal (bool Install, bool Enable, bool Rollback, bool Uninstall, bool Check) ActionAvailability =>
        (installButton.Enabled, enabledCheck.Enabled, rollbackButton.Enabled, uninstallButton.Enabled, checkButton.Enabled);
}

internal static class AddonPalette
{
    internal static readonly Color Background = Color.FromArgb(12, 15, 20);
    internal static readonly Color Surface = Color.FromArgb(22, 27, 35);
    internal static readonly Color SurfaceRaised = Color.FromArgb(31, 38, 49);
    internal static readonly Color Inset = Color.FromArgb(15, 19, 25);
    internal static readonly Color Border = Color.FromArgb(48, 57, 71);
    internal static readonly Color Text = Color.FromArgb(238, 241, 246);
    internal static readonly Color Muted = Color.FromArgb(145, 156, 174);
    internal static readonly Color Gold = Color.FromArgb(238, 174, 68);
    internal static readonly Color GoldDark = Color.FromArgb(92, 65, 25);
    internal static readonly Color Green = Color.FromArgb(79, 199, 126);
    internal static readonly Color Red = Color.FromArgb(229, 91, 91);
    internal static readonly Color Warning = Color.FromArgb(226, 188, 74);
}

internal sealed class AddonCard : Panel
{
    internal bool Accent { get; init; }

    internal AddonCard()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = Rounded(bounds, 12);
        using var fill = new SolidBrush(AddonPalette.Surface);
        using var border = new Pen(Accent ? AddonPalette.Gold : AddonPalette.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        if (Accent)
        {
            using var accent = new SolidBrush(AddonPalette.Gold);
            e.Graphics.FillRectangle(accent, 0, 18, 3, Math.Max(0, Height - 36));
        }
        base.OnPaint(e);
    }

    internal static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal enum AddonButtonKind { Neutral, Primary, Danger }

internal sealed class AddonButton : Button
{
    private bool hovered;
    internal AddonButtonKind Kind { get; init; }

    internal AddonButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var fill = Kind switch
        {
            AddonButtonKind.Primary => hovered ? Color.FromArgb(112, 78, 27) : AddonPalette.GoldDark,
            AddonButtonKind.Danger => hovered ? Color.FromArgb(95, 42, 47) : Color.FromArgb(65, 34, 39),
            _ => hovered ? Color.FromArgb(41, 49, 62) : AddonPalette.SurfaceRaised
        };
        var edge = Kind switch
        {
            AddonButtonKind.Primary => AddonPalette.Gold,
            AddonButtonKind.Danger => AddonPalette.Red,
            _ => AddonPalette.Border
        };
        if (!Enabled)
        {
            fill = Color.FromArgb(20, 24, 31);
            edge = Color.FromArgb(38, 44, 55);
        }
        using var path = AddonCard.Rounded(bounds, 8);
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(edge);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds,
            Enabled ? (Kind == AddonButtonKind.Primary ? AddonPalette.Gold : AddonPalette.Text) : Color.FromArgb(84, 94, 109),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class AddonToggle : CheckBox
{
    internal string Subtitle { get; set; } = string.Empty;

    internal AddonToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Padding = new Padding(4);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(AddonPalette.Surface);
        var titleColor = Enabled ? AddonPalette.Text : Color.FromArgb(91, 101, 116);
        var subColor = Enabled ? AddonPalette.Muted : Color.FromArgb(70, 79, 92);
        var titleRect = new Rectangle(8, 7, Math.Max(1, Width - 74), 20);
        var subtitleRect = new Rectangle(8, 27, Math.Max(1, Width - 74), 18);
        TextRenderer.DrawText(e.Graphics, Text, Font, titleRect, titleColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var subtitleFont = new Font(Font.FontFamily, 8F);
        TextRenderer.DrawText(e.Graphics, Subtitle, subtitleFont, subtitleRect, subColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(Math.Max(4, Width - 58), Math.Max(4, Height / 2 - 11), 44, 22);
        using var path = AddonCard.Rounded(track, 11);
        using var trackBrush = new SolidBrush(Checked && Enabled ? Color.FromArgb(38, 122, 77) : AddonPalette.SurfaceRaised);
        using var trackPen = new Pen(Checked && Enabled ? AddonPalette.Green : AddonPalette.Border);
        e.Graphics.FillPath(trackBrush, path);
        e.Graphics.DrawPath(trackPen, path);
        var knobX = Checked ? track.Right - 18 : track.Left + 4;
        using var knob = new SolidBrush(Checked && Enabled ? AddonPalette.Text : AddonPalette.Muted);
        e.Graphics.FillEllipse(knob, knobX, track.Top + 4, 14, 14);
    }
}

internal sealed class AddonMark : Control
{
    internal AddonMark()
    {
        Size = new Size(44, 38);
        DoubleBuffered = true;
        BackColor = AddonPalette.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(AddonPalette.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, 38, 38);
        using var path = AddonCard.Rounded(bounds, 9);
        using var fill = new SolidBrush(AddonPalette.GoldDark);
        using var edge = new Pen(AddonPalette.Gold, 1.5F);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(edge, path);
        using var markFont = new Font("Segoe UI", 11F, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, "AT", markFont, bounds,
            AddonPalette.Gold, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class AethTankInstallOffer : Form
{
    internal AethTankInstallOffer(string version, string size)
    {
        Text = "Optional Aeth-Tank add-on";
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(510, 260);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        Controls.Add(layout);
        layout.Controls.Add(new Label
        {
            Text = "Install Aeth-Tank?", Font = new Font("Segoe UI", 15F, FontStyle.Bold), Dock = DockStyle.Fill
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = $"Version {version} · {size}\r\n\r\n" +
                "Optional automation for combat, healing, buffs, looting, salvaging, and session statistics " +
                "in the classic client. This proprietary add-on starts paused.\r\n\r\n" +
                "You can install it later from Tools → Aeth-Tank add-on.",
            Dock = DockStyle.Fill
        }, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var install = new Button { Text = "Install", DialogResult = DialogResult.Yes, Width = 90, Height = 30 };
        var later = new Button { Text = "Not now", DialogResult = DialogResult.No, Width = 90, Height = 30 };
        var never = new Button { Text = "Don't ask again", DialogResult = DialogResult.Abort, Width = 120, Height = 30 };
        buttons.Controls.AddRange([install, later, never]);
        layout.Controls.Add(buttons, 0, 2);
        CancelButton = later;
    }
}

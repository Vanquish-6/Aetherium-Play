using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AcLegacyLauncher;

public partial class Form1 : Form
{
    private const string LauncherName = "Aetherium Launcher";
    private const string DeveloperCredit = "Developed by Vanquish, aka Chosen One";
    private const string LegacyConfigFileName = "launcher.json";
    private const string InstallerSkinPreferenceFileName = "launcher.skin";
    private const string DefaultSkinName = "default";
    private const string PkSkinName = "pk";
    private const string BackdropRelativePath = @"Assets\zone-era-launcher-bg-final.png";
    private const string FeatureArtworkRelativePath = @"Assets\zone-era-ac-village-camp.png";
    private const string PkFeatureArtworkRelativePath = @"Assets\skins\pk.bmp";
    private const string PlayButtonRelativePath = @"Assets\zone-era-play-button-better.png";
    private const float FieldLabelColumnWidth = 124f;
    private const int ShellMargin = 32;
    private const int FieldLabelColumnMinWidth = 108;

    // The backdrop PNG (Assets\zone-era-launcher-bg-final.png, 1578×974) bakes a black margin
    // around the gold launcher frame. ZoneEraSurface crops that margin away at load time using
    // BackdropShellCropRect, so the shell fills the window edge-to-edge (no black box) and all
    // design coordinates below are authored in cropped-shell pixels (1514×956).
    private static readonly Rectangle BackdropShellCropRect = new(30, 18, 1514, 956);
    private static readonly Size BackdropDesignSize = new(1514, 956);
    private static readonly RectangleF ConfigOverlayRect = new(648, 236, 720, 428);
    private static readonly RectangleF PkConfigOverlayRect = new(575, 310, 625, 205);
    private static readonly SizeF OverlayLayoutReferenceSize = new(640f, 420f);
    // The launcher name sits centered vertically in the orange title-bar strip (y≈9-42).
    private static readonly RectangleF WindowTitleTextRect = new(24, 8, 420, 35);
    // Room / Zone / Tools / Help sit on the tan menu strip (y≈44-79), packed at the left in blue
    // italic-serif "link" style, exactly like the original Zone-era launcher chrome.
    private static readonly RectangleF RoomHotspotRect = new(16, 44, 82, 35);    // "Room"
    private static readonly RectangleF ZoneHotspotRect = new(98, 44, 80, 35);    // "Zone"
    private static readonly RectangleF ToolsHotspotRect = new(178, 44, 84, 35);  // "Tools"
    private static readonly RectangleF HelpHotspotRect = new(262, 44, 78, 35);   // "Help"
    private static readonly RectangleF RoomTextRect = new(24, 46, 96, 31);
    private static readonly RectangleF ZoneTextRect = new(106, 46, 96, 31);
    private static readonly RectangleF ToolsTextRect = new(186, 46, 100, 31);
    private static readonly RectangleF HelpTextRect = new(270, 46, 92, 31);
    private static readonly RectangleF PlayHotspotRect = new(945, 695, 495, 210);
    private static readonly RectangleF PlayArtworkRect = new(959, 706, 470, 185);
    // Village artwork fills the content area below the ad-banner strip (which ends at y≈204 of
    // the cropped shell), staying inside the gold side borders.
    private static readonly RectangleF FeatureArtworkDestRect = new(10, 210, 1494, 696);
    private static readonly RectangleF FeatureArtworkSrcRect = new(0, 0, 1976, 796);
    // The village art bakes the parchment in at art-space (655, 10, 880, 498), which lands at
    // design-space (505, 219, 665, 435). Re-blit it wider and further right so the login form can
    // sit over the dead black area; the dest fully covers the original footprint so no doubled
    // parchment edge shows, and the crop's fringe pixels land back over matching scenery.
    private static readonly RectangleF FeatureParchmentSrcRect = new(655, 10, 880, 498);
    private static readonly RectangleF FeatureParchmentDestRect = new(505, 216, 930, 462);
    private static readonly RectangleF TitleDragRect = new(0, 0, 1394, 44);
    // The min/max/close glyphs are baked into the backdrop chrome at these spots.
    private static readonly RectangleF MinimizeHotspotRect = new(1396, 9, 34, 33);
    private static readonly RectangleF MaximizeHotspotRect = new(1430, 9, 34, 33);
    private static readonly RectangleF CloseHotspotRect = new(1465, 9, 35, 33);

    private static readonly string LegacyConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AcLegacyLauncher");

    private static readonly Color InkBrown = Color.FromArgb(32, 16, 6);
    private static readonly Color EntryFill = Color.FromArgb(230, 206, 156);
    private static readonly Color EntryInsetDark = Color.FromArgb(92, 54, 22);
    private static readonly Color EntryInsetLight = Color.FromArgb(246, 226, 184);
    private static readonly Color ParchmentPatch = Color.FromArgb(198, 164, 108);
    private static readonly Color ChromeTitleColor = Color.FromArgb(252, 228, 156); // warm golden — readable on orange
    private static readonly Color ChromeMenuColor = Color.FromArgb(34, 52, 154);
    private static readonly Color VioletGlow = Color.FromArgb(187, 68, 170);
    private static readonly Color VioletDark = Color.FromArgb(27, 7, 24);

    private readonly TextBox installPathTextBox = new();
    private readonly TextBox usernameTextBox = new();
    private readonly TextBox hostTextBox = new();
    private readonly ServerChoiceBar serverChoice = new();
    private readonly TextBox passwordTextBox = new();
    private readonly TextBox zoneKeyTextBox = new();
    private readonly InkToggle useNoDisplayModeCheckBox = new();
    private readonly InkToggle seedSafeGraphicsCheckBox = new();
    private readonly List<TableLayoutPanel> fieldRows = new();
    private readonly List<Label> fieldLabels = new();
    private readonly List<LedgerField> ledgerFields = new();
    private readonly List<ArcaneButton> toolButtons = new();
    private readonly object runtimeGuardLock = new();
    private readonly Dictionary<string, RunningSlot> runningSlots = new();
    private readonly AccountSlot slotOne = new() { Id = "1" };
    private readonly AccountSlot slotTwo = new() { Id = "2" };
    private readonly string? startupInstallDirectory;

    private ZoneEraSurface? surface;
    private TransparentOverlayPanel? configOverlay;
    private FlowLayoutPanel? optionRow;
    private FlowLayoutPanel? toolRow;
    private Label? windowTitleChromeLabel;
    private Label? fileChromeLabel;
    private Label? zoneChromeLabel;
    private Label? toolsChromeLabel;
    private Label? helpChromeLabel;
    private Label? titleLabel;
    private Panel? titleRule;
    private FlowLayoutPanel? slotChoice;
    private readonly ToolTip slotToolTip = new();
    private ArcaneButton? slotOneButton;
    private ArcaneButton? slotTwoButton;
    private string selectedSlotId = "1";
    private bool suppressSlotEvents;
    private ContextMenuStrip? fileMenu;
    private ContextMenuStrip? toolsMenu;
    private ContextMenuStrip? helpMenu;
    private ToolStripMenuItem? defaultSkinMenuItem;
    private ToolStripMenuItem? pkSkinMenuItem;
    private float currentOverlayScale = 1f;
    private bool currentCompactOverlay;
    private string currentSkin = DefaultSkinName;

    public Form1()
        : this(null)
    {
    }

    internal Form1(string? startupInstallDirectory)
    {
        this.startupInstallDirectory = startupInstallDirectory;
        InitializeComponent();
        BuildLayout();
        if (startupInstallDirectory is not null)
        {
            installPathTextBox.ReadOnly = true;
        }
        LoadConfigIntoControls();
        Shown += Form1_Shown;
    }

    private async void Form1_Shown(object? sender, EventArgs e)
    {
        Shown -= Form1_Shown;
        await UpdateChecker.CheckForUpdatesAsync(
            this,
            interactive: false);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel)
        {
            return;
        }

        RunningSlot[] running;
        lock (runtimeGuardLock)
        {
            running = runningSlots.Values.ToArray();
        }

        if (running.Length != 0 &&
            e.CloseReason is CloseReason.UserClosing or CloseReason.ApplicationExitCall)
        {
            var clients = running.Length == 1 ? "the monitored game client" : "both monitored game clients";
            var choice = MessageBox.Show(
                this,
                $"Exiting Aetherium Launcher will also close {clients}. Exit now?",
                LauncherName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (choice != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        foreach (var slot in running)
        {
            slot.Guard.Dispose();
        }
    }

    private void BuildLayout()
    {
        SuspendLayout();
        Controls.Clear();

        AutoScaleMode = AutoScaleMode.None;
        Text = LauncherName;
        var shellSize = CalculateShellSize();
        ClientSize = shellSize;
        MinimumSize = shellSize;
        MaximumSize = shellSize;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        BackColor = Color.Black;
        Font = new Font("Trebuchet MS", 8.75f, FontStyle.Regular, GraphicsUnit.Point);
        KeyDown -= Form1_KeyDown;
        KeyDown += Form1_KeyDown;

        surface = new ZoneEraSurface(GetBackdropPath(), GetFeatureArtworkPath(currentSkin), GetPlayButtonPath())
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
        };
        surface.Resize += (_, _) => LayoutOverlayControls();
        surface.MouseMove += Surface_MouseMove;
        surface.MouseLeave += Surface_MouseLeave;
        surface.MouseDown += Surface_MouseDown;
        Controls.Add(surface);

        fileMenu = BuildRoomMenu();
        toolsMenu = BuildToolsMenu();
        helpMenu = BuildHelpMenu();
        BuildChromeLabels();
        configOverlay = BuildConfigOverlay();

        // Chrome text is now drawn directly in ZoneEraSurface.OnPaint (DrawChromeText),
        // so the label controls do NOT need to be in surface.Controls.
        // WinForms transparent labels on a double-buffered custom-painted panel render
        // against the panel's BackColor (black), not the painted content — adding them
        // here was what caused the black title strip above the orange title bar.
        surface.Controls.Add(configOverlay);

        LayoutOverlayControls();
        ResumeLayout(true);
    }

    private void BuildChromeLabels()
    {
        windowTitleChromeLabel = CreateChromeLabel(LauncherName, ChromeTitleColor, Cursors.SizeAll);
        windowTitleChromeLabel.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                BeginWindowDrag();
            }
        };

        // Match the original Zone-era launcher: Room  Zone  Tools  Help
        fileChromeLabel = CreateChromeLabel("Room", ChromeMenuColor, Cursors.Hand, (_, _) => ShowFileMenu(MapDesignRect(RoomHotspotRect)));
        zoneChromeLabel = CreateChromeLabel("Zone", ChromeMenuColor, Cursors.Hand, LaunchButton_Click);
        toolsChromeLabel = CreateChromeLabel("Tools", ChromeMenuColor, Cursors.Hand, (_, _) => ShowToolsMenu(MapDesignRect(ToolsHotspotRect)));
        helpChromeLabel = CreateChromeLabel("Help", ChromeMenuColor, Cursors.Hand, (_, _) => ShowHelpMenu());
    }

    private static Label CreateChromeLabel(string text, Color color, Cursor cursor, EventHandler? clickHandler = null)
    {
        var label = new Label
        {
            AutoSize = false,
            ForeColor = color,
            Text = text,
            Cursor = cursor,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            UseCompatibleTextRendering = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        try
        {
            label.BackColor = Color.Transparent;
        }
        catch (ArgumentException)
        {
            label.BackColor = EntryFill;
        }

        if (clickHandler is not null)
        {
            label.Click += clickHandler;
        }

        return label;
    }

    private TransparentOverlayPanel BuildConfigOverlay()
    {
        fieldRows.Clear();
        fieldLabels.Clear();
        ledgerFields.Clear();
        toolButtons.Clear();
        currentOverlayScale = 1f;

        StyleTextInput(installPathTextBox, @"C:\path\to\client");
        StyleTextInput(usernameTextBox, "Account name");
        StyleTextInput(passwordTextBox, "Password", true);
        StyleTextInput(hostTextBox, AetheriumInstallationConfiguration.DefaultHost);
        StyleTextInput(zoneKeyTextBox, "Optional -z");
        serverChoice.SelectionChanged += (_, _) =>
        {
            if (!suppressSlotEvents)
            {
                SaveControlsToConfig();
                RefreshSlotChips();
            }
        };
        usernameTextBox.TextChanged += (_, _) =>
        {
            if (suppressSlotEvents)
            {
                return;
            }

            WriteFieldsToSelectedSlot();
            RefreshSlotChips();
        };
        usernameTextBox.Leave += (_, _) =>
        {
            if (!suppressSlotEvents)
            {
                SaveControlsToConfig();
            }
        };
        passwordTextBox.Leave += (_, _) =>
        {
            if (!suppressSlotEvents)
            {
                SaveControlsToConfig();
            }
        };

        useNoDisplayModeCheckBox.Text = "No display mode (-nd)";
        useNoDisplayModeCheckBox.Checked = true;
        seedSafeGraphicsCheckBox.Text = "Safe graphics";
        seedSafeGraphicsCheckBox.Checked = true;

        var overlay = new TransparentOverlayPanel
        {
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(22, 16, 20, 12),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        overlay.Controls.Add(layout);

        titleLabel = new Label
        {
            AutoSize = true,
            Text = "Portal Login",
            Font = new Font("Georgia", 16f, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = InkBrown,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 4),
        };
        layout.Controls.Add(titleLabel);

        titleRule = new Panel
        {
            Height = 2,
            Width = 176,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.FromArgb(118, 72, 24),
        };
        layout.Controls.Add(titleRule);

        slotChoice = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
        };
        slotOneButton = CreateSlotChip("1");
        slotTwoButton = CreateSlotChip("2");
        slotChoice.Controls.Add(slotOneButton);
        slotChoice.Controls.Add(slotTwoButton);

        layout.Controls.Add(CreateFieldRow("Install", WrapLedger(installPathTextBox)));
        layout.Controls.Add(CreateFieldRow("Slot", slotChoice));
        slotChoice.Dock = DockStyle.Left;
        layout.Controls.Add(CreateFieldRow("Account", WrapLedger(usernameTextBox)));
        layout.Controls.Add(CreateFieldRow("Password", WrapLedger(passwordTextBox)));
        layout.Controls.Add(CreateFieldRow("Host", WrapLedger(hostTextBox)));
        layout.Controls.Add(CreateFieldRow("Server", serverChoice));
        serverChoice.Dock = DockStyle.Left;
        layout.Controls.Add(CreateFieldRow("Zone", WrapLedger(zoneKeyTextBox)));

        optionRow = new FlowLayoutPanel
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(124, 12, 0, 0),
            Padding = Padding.Empty,
        };
        optionRow.Controls.Add(useNoDisplayModeCheckBox);
        optionRow.Controls.Add(seedSafeGraphicsCheckBox);
        layout.Controls.Add(optionRow);

        toolRow = new FlowLayoutPanel
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(124, 8, 0, 0),
            Padding = Padding.Empty,
        };
        toolRow.Controls.Add(CreateToolButton("Folder", (_, _) => OpenInstallFolder()));
        toolRow.Controls.Add(CreateToolButton("ACD3D", (_, _) => LaunchCompanionTool("ACD3DSetup.exe")));
        toolRow.Controls.Add(CreateToolButton("ACSET", (_, _) => LaunchCompanionTool("ACSET.EXE", "Acset.exe")));
        layout.Controls.Add(toolRow);

        return overlay;
    }

    private LedgerField WrapLedger(TextBox textBox)
    {
        var field = new LedgerField(textBox);
        ledgerFields.Add(field);
        return field;
    }

    private TableLayoutPanel CreateFieldRow(string labelText, Control inputControl)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Margin = new Padding(0, 4, 0, 0),
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FieldLabelColumnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        row.Controls.Add(CreateFieldLabel(labelText), 0, 0);

        inputControl.Dock = DockStyle.Fill;
        inputControl.Margin = Padding.Empty;
        row.Controls.Add(inputControl, 1, 0);

        fieldRows.Add(row);
        return row;
    }

    private ArcaneButton CreateSlotChip(string slotId)
    {
        var button = new ArcaneButton
        {
            Text = slotId,
            Width = 148,
            Height = 28,
            Margin = new Padding(0, 0, slotId == "1" ? 8 : 0, 0),
            Palette = ArcaneButton.ButtonPalette.Bone,
            Font = new Font("Georgia", 9f, FontStyle.Bold, GraphicsUnit.Point),
        };
        button.Click += (_, _) => SelectSlot(slotId);
        return button;
    }

    private void SelectSlot(string slotId)
    {
        if (slotId == selectedSlotId || suppressSlotEvents)
        {
            return;
        }

        WriteFieldsToSelectedSlot();
        selectedSlotId = slotId;
        LoadSelectedSlotFields();
        SaveControlsToConfig();
        RefreshSlotChips();
    }

    private AccountSlot SelectedSlot()
    {
        return selectedSlotId == "2" ? slotTwo : slotOne;
    }

    private void WriteFieldsToSelectedSlot()
    {
        var slot = SelectedSlot();
        slot.Account = usernameTextBox.Text.Trim();
        slot.Password = passwordTextBox.Text.Trim();
        slot.Port = serverChoice.RedSelected
            ? AetheriumInstallationConfiguration.RedServerPort
            : AetheriumInstallationConfiguration.WhiteServerPort;
    }

    private void LoadSelectedSlotFields()
    {
        var slot = SelectedSlot();
        suppressSlotEvents = true;
        try
        {
            usernameTextBox.Text = slot.Account;
            passwordTextBox.Text = slot.Password;
            serverChoice.SetRedSelected(slot.Port == AetheriumInstallationConfiguration.RedServerPort);
        }
        finally
        {
            suppressSlotEvents = false;
        }

        RefreshSlotChips();
    }

    private void CopySlotsFrom(LaunchConfig config)
    {
        config.EnsureSlots();
        CopySlotOnto(slotOne, config.Slots.First(slot => slot.Id == "1"));
        CopySlotOnto(slotTwo, config.Slots.First(slot => slot.Id == "2"));
        selectedSlotId = config.SelectedSlotId;
    }

    private static void CopySlotOnto(AccountSlot target, AccountSlot source)
    {
        target.Account = source.Account;
        target.Password = source.Password;
        target.Port = source.Port;
    }

    private static AccountSlot CopySlot(AccountSlot source)
    {
        return new AccountSlot
        {
            Id = source.Id,
            Account = source.Account,
            Password = source.Password,
            Port = source.Port,
        };
    }

    private void RefreshSlotChips()
    {
        if (slotOneButton is not null)
        {
            slotOneButton.Text = ChipText(slotOne);
            slotOneButton.Chosen = selectedSlotId == "1";
            slotToolTip.SetToolTip(slotOneButton, SlotTip(slotOne));
        }

        if (slotTwoButton is not null)
        {
            slotTwoButton.Text = ChipText(slotTwo);
            slotTwoButton.Chosen = selectedSlotId == "2";
            slotToolTip.SetToolTip(slotTwoButton, SlotTip(slotTwo));
        }
    }

    private string ChipText(AccountSlot slot)
    {
        var name = string.IsNullOrWhiteSpace(slot.Account) ? "Empty" : slot.Account.Trim();
        if (name.Length > 16)
        {
            name = name[..15] + "…";
        }

        return IsSlotRunning(slot.Id) ? $"{slot.Id}  {name}  ·  on" : $"{slot.Id}  {name}";
    }

    private static string SlotTip(AccountSlot slot)
    {
        var name = string.IsNullOrWhiteSpace(slot.Account) ? "No account saved" : slot.Account.Trim();
        var server = slot.Port == AetheriumInstallationConfiguration.RedServerPort ? "Red" : "White";
        return $"Slot {slot.Id} · {name} · {server}";
    }

    private bool IsSlotRunning(string slotId)
    {
        lock (runtimeGuardLock)
        {
            return runningSlots.TryGetValue(slotId, out var running) && !running.Process.HasExited;
        }
    }

    private bool IsAnotherSlotRunning(string slotId)
    {
        lock (runtimeGuardLock)
        {
            return runningSlots.Any(pair =>
                pair.Key != slotId && !pair.Value.Process.HasExited);
        }
    }

    private ArcaneButton CreateToolButton(string text, EventHandler clickHandler)
    {
        var button = new ArcaneButton
        {
            Text = text,
            Width = 90,
            Height = 28,
            Margin = new Padding(0, 0, 8, 0),
        };
        button.Click += clickHandler;
        toolButtons.Add(button);
        return button;
    }

    private ContextMenuStrip BuildRoomMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
        };
        menu.Items.Add("Browse Install Folder", null, BrowseButton_Click);
        menu.Items.Add("Open Install Folder", null, (_, _) => OpenInstallFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());
        return menu;
    }

    private ContextMenuStrip BuildToolsMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
        };
        menu.Items.Add("Open Install Folder", null, (_, _) => OpenInstallFolder());
        menu.Items.Add(new ToolStripSeparator());
        var skinMenu = new ToolStripMenuItem("Launcher Skin");
        defaultSkinMenuItem = new ToolStripMenuItem("Default", null, (_, _) => ApplySkin(DefaultSkinName, save: true));
        pkSkinMenuItem = new ToolStripMenuItem("PK", null, (_, _) => ApplySkin(PkSkinName, save: true));
        skinMenu.DropDownItems.Add(defaultSkinMenuItem);
        skinMenu.DropDownItems.Add(pkSkinMenuItem);
        menu.Items.Add(skinMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Run ACD3DSetup", null, (_, _) => LaunchCompanionTool("ACD3DSetup.exe"));
        menu.Items.Add("Run ACSET", null, (_, _) => LaunchCompanionTool("ACSET.EXE", "Acset.exe"));
        UpdateSkinMenuChecks();
        return menu;
    }

    private void LayoutOverlayControls()
    {
        if (surface is null || configOverlay is null || windowTitleChromeLabel is null || fileChromeLabel is null ||
            zoneChromeLabel is null || toolsChromeLabel is null || helpChromeLabel is null)
        {
            return;
        }

        windowTitleChromeLabel.Bounds = surface.MapFromDesign(WindowTitleTextRect, BackdropDesignSize);
        fileChromeLabel.Bounds = surface.MapFromDesign(RoomHotspotRect, BackdropDesignSize);
        zoneChromeLabel.Bounds = surface.MapFromDesign(ZoneHotspotRect, BackdropDesignSize);
        toolsChromeLabel.Bounds = surface.MapFromDesign(ToolsHotspotRect, BackdropDesignSize);
        helpChromeLabel.Bounds = surface.MapFromDesign(HelpHotspotRect, BackdropDesignSize);
        configOverlay.Bounds = surface.MapFromDesign(
            currentSkin == PkSkinName ? PkConfigOverlayRect : ConfigOverlayRect,
            BackdropDesignSize);
        var surfaceScale = surface.SceneBounds.Height / (float)BackdropDesignSize.Height;
        // The authored 596×529 reference layout leaves generous slack inside the overlay, so boost
        // the fitted scale ~20% to render the form larger/easier to read while still fitting.
        var compact = currentSkin == PkSkinName;
        var overlayScale = compact
            ? Math.Clamp(configOverlay.Bounds.Height / 320f, 0.42f, 0.62f)
            : Math.Min(
                configOverlay.Bounds.Width / OverlayLayoutReferenceSize.Width,
                configOverlay.Bounds.Height / OverlayLayoutReferenceSize.Height);
        ApplyChromeScale(surfaceScale);
        ApplyOverlayScale(overlayScale, compact);
    }

    private Label CreateFieldLabel(string text)
    {
        var label = new Label
        {
            AutoSize = false,
            Height = 26,
            Dock = DockStyle.Fill,
            Text = text,
            Font = new Font("Georgia", 9.5f, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = InkBrown,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0, 0, 10, 0),
            BackColor = Color.Transparent,
        };

        fieldLabels.Add(label);
        return label;
    }

    private static void StyleTextInput(TextBox textBox, string placeholderText, bool usePasswordChar = false)
    {
        textBox.AutoSize = false;
        textBox.PlaceholderText = placeholderText;
        textBox.BorderStyle = BorderStyle.None;
        textBox.BackColor = EntryFill;
        textBox.ForeColor = InkBrown;
        textBox.Font = new Font("Trebuchet MS", 9f, FontStyle.Bold, GraphicsUnit.Point);
        textBox.UseSystemPasswordChar = usePasswordChar;
        textBox.Height = 20;
        textBox.Margin = Padding.Empty;
    }

    private static string GetBackdropPath()
    {
        return Path.Combine(AppContext.BaseDirectory, BackdropRelativePath);
    }

    private static string GetFeatureArtworkPath(string skinName)
    {
        var relativePath = skinName.Equals(PkSkinName, StringComparison.OrdinalIgnoreCase)
            ? PkFeatureArtworkRelativePath
            : FeatureArtworkRelativePath;
        return Path.Combine(AppContext.BaseDirectory, relativePath);
    }

    private static string GetInstallerSkinPreferencePath()
    {
        return Path.Combine(AppContext.BaseDirectory, InstallerSkinPreferenceFileName);
    }

    private static string NormalizeSkinName(string? skinName)
    {
        return string.Equals(skinName?.Trim(), PkSkinName, StringComparison.OrdinalIgnoreCase)
            ? PkSkinName
            : DefaultSkinName;
    }

    private static string? ReadInstallerSkinPreference()
    {
        var preferencePath = GetInstallerSkinPreferencePath();
        if (!File.Exists(preferencePath))
        {
            return null;
        }

        try
        {
            var preference = File.ReadAllText(preferencePath).Trim();
            if (preference.Equals(PkSkinName, StringComparison.OrdinalIgnoreCase))
            {
                return PkSkinName;
            }

            return preference.Equals(DefaultSkinName, StringComparison.OrdinalIgnoreCase)
                ? DefaultSkinName
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void ApplySkin(string? skinName, bool save)
    {
        var resolvedSkin = NormalizeSkinName(skinName);
        var artworkPath = GetFeatureArtworkPath(resolvedSkin);
        if (!File.Exists(artworkPath) && resolvedSkin == PkSkinName)
        {
            resolvedSkin = DefaultSkinName;
            artworkPath = GetFeatureArtworkPath(resolvedSkin);
            if (save)
            {
                MessageBox.Show(this, "The PK skin artwork is missing. The Default skin will be used.", LauncherName);
            }
        }

        currentSkin = resolvedSkin;
        surface?.SetFeatureArtwork(artworkPath, resolvedSkin == PkSkinName);
        UpdateSkinMenuChecks();
        LayoutOverlayControls();

        if (save)
        {
            SaveControlsToConfig();
        }
    }

    private void UpdateSkinMenuChecks()
    {
        if (defaultSkinMenuItem is not null)
        {
            defaultSkinMenuItem.Checked = currentSkin == DefaultSkinName;
        }

        if (pkSkinMenuItem is not null)
        {
            pkSkinMenuItem.Checked = currentSkin == PkSkinName;
        }
    }

    private static string GetPlayButtonPath()
    {
        return Path.Combine(AppContext.BaseDirectory, PlayButtonRelativePath);
    }

    private static Size CalculateShellSize()
    {
        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        var maxWidth = Math.Min(BackdropDesignSize.Width, Math.Max(1, workingArea.Width - ShellMargin));
        var maxHeight = Math.Min(BackdropDesignSize.Height, Math.Max(1, workingArea.Height - ShellMargin));
        var scale = Math.Min(
            maxWidth / (float)BackdropDesignSize.Width,
            maxHeight / (float)BackdropDesignSize.Height);

        return new Size(
            (int)Math.Round(BackdropDesignSize.Width * scale),
            (int)Math.Round(BackdropDesignSize.Height * scale));
    }

    // The full composite image now provides its own parchment area, so scale the live controls
    // against the authored overlay size rather than the overall window size.
    private void ApplyOverlayScale(float scale, bool compact)
    {
        if (configOverlay is null || titleLabel is null || titleRule is null || optionRow is null || toolRow is null)
        {
            return;
        }

        scale = compact
            ? Math.Clamp(scale, 0.42f, 0.62f)
            : Math.Clamp(scale, 0.78f, 1.08f);
        if (Math.Abs(scale - currentOverlayScale) < 0.01f && compact == currentCompactOverlay)
        {
            return;
        }

        currentOverlayScale = scale;
        currentCompactOverlay = compact;
        configOverlay.Padding = ScalePadding(
            compact ? new Padding(12, 10, 12, 8) : new Padding(22, 16, 20, 12),
            scale);

        titleLabel.Visible = !compact;
        titleRule.Visible = !compact;
        toolRow.Visible = !compact;
        titleLabel.Font = CreateScaledFont("Georgia", 16f, FontStyle.Bold, scale);
        titleLabel.Margin = ScalePadding(new Padding(0, 0, 0, 4), scale);
        titleRule.Width = ScaleInt(168, scale);
        titleRule.Height = Math.Max(1, ScaleInt(2, scale));
        titleRule.Margin = ScalePadding(new Padding(0, 0, 0, compact ? 4 : 8), scale);
        var labelColumn = compact
            ? Math.Max(64, ScaleInt((int)FieldLabelColumnWidth, scale))
            : Math.Max(FieldLabelColumnMinWidth, ScaleInt((int)FieldLabelColumnWidth, scale));
        var slotWidth = Math.Max(compact ? 72 : 120, ScaleInt(compact ? 96 : 148, scale));
        var slotHeight = Math.Max(compact ? 20 : 24, ScaleInt(28, scale));
        var slotFont = CreateScaledFont("Georgia", 9f, FontStyle.Bold, scale, compact ? 7f : 8f);
        if (slotOneButton is not null)
        {
            slotOneButton.Width = slotWidth;
            slotOneButton.Height = slotHeight;
            slotOneButton.Margin = ScalePadding(new Padding(0, 0, compact ? 4 : 8, 0), scale);
            slotOneButton.Font = slotFont;
        }

        if (slotTwoButton is not null)
        {
            slotTwoButton.Width = slotWidth;
            slotTwoButton.Height = slotHeight;
            slotTwoButton.Margin = Padding.Empty;
            slotTwoButton.Font = CreateScaledFont("Georgia", 9f, FontStyle.Bold, scale, compact ? 7f : 8f);
        }

        foreach (var row in fieldRows)
        {
            row.Margin = ScalePadding(new Padding(0, compact ? 2 : 3, 0, 0), scale);
            if (row.ColumnStyles.Count > 0)
            {
                row.ColumnStyles[0].Width = labelColumn;
            }
        }

        foreach (var label in fieldLabels)
        {
            label.Height = ScaleInt(26, scale);
            label.Font = CreateScaledFont("Georgia", 9.5f, FontStyle.Bold, scale, compact ? 7.5f : 8.75f);
            label.Margin = ScalePadding(new Padding(0, 0, 10, 0), scale);
        }

        foreach (var ledger in ledgerFields)
        {
            ledger.ApplyScale(scale);
        }

        serverChoice.ApplyScale(scale, compact);

        optionRow.FlowDirection = FlowDirection.TopDown;
        optionRow.WrapContents = false;
        optionRow.Margin = ScalePadding(new Padding(compact ? 0 : 124, 12, 0, 0), scale);
        toolRow.Margin = ScalePadding(new Padding(compact ? 0 : 124, 12, 0, 0), scale);

        useNoDisplayModeCheckBox.Font = CreateScaledFont("Georgia", 9.25f, FontStyle.Bold, scale, compact ? 6.5f : 8.25f);
        useNoDisplayModeCheckBox.Margin = ScalePadding(new Padding(0, 0, 0, 6), scale);
        seedSafeGraphicsCheckBox.Font = CreateScaledFont("Georgia", 9.25f, FontStyle.Bold, scale, compact ? 6.5f : 8.25f);
        seedSafeGraphicsCheckBox.Margin = Padding.Empty;

        foreach (var button in toolButtons)
        {
            button.Width = ScaleInt(90, scale);
            button.Height = ScaleInt(28, scale);
            button.Margin = ScalePadding(new Padding(0, 0, 8, 0), scale);
            button.Font = CreateScaledFont("Georgia", 9.5f, FontStyle.Bold, scale);
        }

        configOverlay.PerformLayout();
    }

    private void ApplyChromeScale(float scale)
    {
        if (windowTitleChromeLabel is null || fileChromeLabel is null || zoneChromeLabel is null ||
            toolsChromeLabel is null || helpChromeLabel is null)
        {
            return;
        }

        windowTitleChromeLabel.Font = CreateScaledFont("Georgia", 16.5f, FontStyle.Italic, scale, 10.5f);
        // Blue italic-serif "link" style, matching the rendered chrome text in DrawChromeText.
        var menuStyle = FontStyle.Bold | FontStyle.Italic | FontStyle.Underline;
        fileChromeLabel.Font = CreateScaledFont("Georgia", 11f, menuStyle, scale, 9f);
        zoneChromeLabel.Font = CreateScaledFont("Georgia", 11f, menuStyle, scale, 9f);
        toolsChromeLabel.Font = CreateScaledFont("Georgia", 11f, menuStyle, scale, 9f);
        helpChromeLabel.Font = CreateScaledFont("Georgia", 11f, menuStyle, scale, 9f);
    }

    private static Font CreateScaledFont(string family, float size, FontStyle style, float scale)
    {
        return CreateScaledFont(family, size, style, scale, 6.5f);
    }

    private static Font CreateScaledFont(string family, float size, FontStyle style, float scale, float minimumSize)
    {
        return new Font(family, Math.Max(minimumSize, size * scale), style, GraphicsUnit.Point);
    }

    private static int ScaleInt(int value, float scale)
    {
        return Math.Max(1, (int)Math.Round(value * scale));
    }

    private static Padding ScalePadding(Padding padding, float scale)
    {
        return new Padding(
            ScaleInt(padding.Left, scale),
            ScaleInt(padding.Top, scale),
            ScaleInt(padding.Right, scale),
            ScaleInt(padding.Bottom, scale));
    }

    private Rectangle MapDesignRect(RectangleF designRect)
    {
        return surface?.MapFromDesign(designRect, BackdropDesignSize) ?? Rectangle.Empty;
    }

    private SurfaceHitTarget HitTestSurface(Point location)
    {
        if (MapDesignRect(CloseHotspotRect).Contains(location))
        {
            return SurfaceHitTarget.Close;
        }

        if (MapDesignRect(MaximizeHotspotRect).Contains(location))
        {
            return SurfaceHitTarget.Maximize;
        }

        if (MapDesignRect(MinimizeHotspotRect).Contains(location))
        {
            return SurfaceHitTarget.Minimize;
        }

        if (MapDesignRect(RoomHotspotRect).Contains(location))
        {
            return SurfaceHitTarget.FileMenu;
        }

        if (MapDesignRect(ZoneHotspotRect).Contains(location))
        {
            return SurfaceHitTarget.ZoneLaunch;
        }

        if (MapDesignRect(ToolsHotspotRect).Contains(location))
        {
            return SurfaceHitTarget.ToolsMenu;
        }

        if (MapDesignRect(HelpHotspotRect).Contains(location))
        {
            return SurfaceHitTarget.Help;
        }

        if (MapDesignRect(PlayHotspotRect).Contains(location))
        {
            return SurfaceHitTarget.PlayLaunch;
        }

        if (MapDesignRect(TitleDragRect).Contains(location))
        {
            return SurfaceHitTarget.TitleDrag;
        }

        return SurfaceHitTarget.None;
    }

    private void Surface_MouseMove(object? sender, MouseEventArgs e)
    {
        if (surface is null)
        {
            return;
        }

        surface.Cursor = HitTestSurface(e.Location) switch
        {
            SurfaceHitTarget.None => Cursors.Default,
            SurfaceHitTarget.TitleDrag => Cursors.SizeAll,
            _ => Cursors.Hand,
        };
    }

    private void Surface_MouseLeave(object? sender, EventArgs e)
    {
        if (surface is not null)
        {
            surface.Cursor = Cursors.Default;
        }
    }

    private void Surface_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        switch (HitTestSurface(e.Location))
        {
            case SurfaceHitTarget.FileMenu:
                ShowFileMenu(MapDesignRect(RoomHotspotRect));
                break;
            case SurfaceHitTarget.ZoneLaunch:
            case SurfaceHitTarget.PlayLaunch:
                LaunchButton_Click(sender, EventArgs.Empty);
                break;
            case SurfaceHitTarget.ToolsMenu:
                ShowToolsMenu(MapDesignRect(ToolsHotspotRect));
                break;
            case SurfaceHitTarget.Help:
                ShowHelpMenu();
                break;
            case SurfaceHitTarget.Minimize:
                WindowState = FormWindowState.Minimized;
                break;
            case SurfaceHitTarget.Maximize:
                ToggleWindowState();
                break;
            case SurfaceHitTarget.Close:
                Close();
                break;
            case SurfaceHitTarget.TitleDrag:
                BeginWindowDrag();
                break;
        }
    }

    private void ShowFileMenu(Rectangle anchorBounds)
    {
        if (fileMenu is not null && surface is not null && !anchorBounds.IsEmpty)
        {
            fileMenu.Show(surface, new Point(anchorBounds.Left, anchorBounds.Bottom));
        }
    }

    private void ShowToolsMenu(Rectangle anchorBounds)
    {
        if (toolsMenu is not null && surface is not null && !anchorBounds.IsEmpty)
        {
            toolsMenu.Show(surface, new Point(anchorBounds.Left, anchorBounds.Bottom));
        }
    }

    private void BeginWindowDrag()
    {
        Win32Native.ReleaseCapture();
        Win32Native.SendMessage(Handle, Win32Native.WmNcLButtonDown, (nint)Win32Native.HtCaption, nint.Zero);
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    private ContextMenuStrip BuildHelpMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
        };
        menu.Items.Add("Check for Updates...", null, (_, _) =>
        {
            _ = CheckForUpdatesInteractiveAsync();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("About", null, (_, _) => ShowAboutDialog());
        return menu;
    }

    private void ShowHelpMenu()
    {
        if (helpMenu is null || surface is null)
        {
            return;
        }

        var anchorBounds = MapDesignRect(HelpHotspotRect);
        if (!anchorBounds.IsEmpty)
        {
            helpMenu.Show(surface, new Point(anchorBounds.Left, anchorBounds.Bottom));
        }
    }

    private async Task CheckForUpdatesInteractiveAsync()
    {
        await UpdateChecker.CheckForUpdatesAsync(
            this,
            interactive: true);
    }

    private void ShowAboutDialog()
    {
        MessageBox.Show(
            this,
            $"{LauncherName} v{UpdateChecker.CurrentVersion}\n\n" +
            "Room lets you browse the client folder or exit.\n" +
            "Zone and the purple PLAY button both launch client.exe with the parchment settings.\n" +
            "The client always launches from your install folder so DAT updates stay in one place.\n" +
            "Two account slots keep separate logins, Red/White, and game settings.\n" +
            "We do not rewrite existing Documents\\Asheron's Call\\UserPreferences.ini.\n" +
            "A09 checks active program identity locally and verifies its own client hooks while the game runs.\n" +
            "It resolves active executable paths only for version identity; paths stay local and are not logged.\n" +
            "It does not scan directories, upload a process list, or stop another program.\n" +
            "Forcibly ending the launcher also ends its monitored client.\n" +
            "Tools exposes Open Folder, ACD3DSetup, and ACSET.\n" +
            "Help → Check for Updates uses GitHub Releases.\n\n" +
            DeveloperCredit,
            LauncherName);
    }

    private void OpenInstallFolder()
    {
        var installFolder = GetInstallDirectory();
        if (installFolder is null)
        {
            MessageBox.Show(this, "Set a valid install folder first.", LauncherName);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = installFolder,
            UseShellExecute = true,
        });
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        if (startupInstallDirectory is not null)
        {
            MessageBox.Show(
                this,
                $"This shortcut is pinned to {startupInstallDirectory}",
                LauncherName);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the folder containing client.exe",
            ShowNewFolderButton = false,
        };

        if (Directory.Exists(installPathTextBox.Text))
        {
            dialog.InitialDirectory = installPathTextBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            installPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        LaunchButton_Click(null, EventArgs.Empty);
        e.SuppressKeyPress = true;
    }

    private void LaunchButton_Click(object? sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(usernameTextBox.Text))
            {
                MessageBox.Show(this, "Account name is required.", LauncherName);
                return;
            }

            if (IsSlotRunning(selectedSlotId))
            {
                MessageBox.Show(
                    this,
                    $"Slot {selectedSlotId} is already in game. Pick the other account to open a second client.",
                    LauncherName);
                return;
            }

            var config = ReadFormConfig();
            if (startupInstallDirectory is not null)
            {
                _ = LauncherStartupOptions.Parse(
                    ["--game-install", startupInstallDirectory]);
            }

            ClientLauncher.ValidateForLaunch(config);
            SaveControlsToConfig();
            var result = ClientLauncher.Start(
                config,
                ClientLauncher.GetRepositoryToolsDirectory());
            TrackRuntimeGuard(result);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LauncherName);
        }
    }

    private void TrackRuntimeGuard(ClientLaunchResult result)
    {
        var guard = result.AntiTamperGuard;
        var process = result.Process;
        if (guard is null || process is null)
        {
            return;
        }

        lock (runtimeGuardLock)
        {
            runningSlots[result.SlotId] = new RunningSlot
            {
                SlotId = result.SlotId,
                Guard = guard,
                Process = process,
            };
        }

        RefreshSlotChips();

        var cleanupStarted = 0;
        void CleanupRuntimeGuard()
        {
            if (Interlocked.Exchange(ref cleanupStarted, 1) != 0)
            {
                return;
            }

            guard.Dispose();
            lock (runtimeGuardLock)
            {
                if (runningSlots.TryGetValue(result.SlotId, out var running) &&
                    ReferenceEquals(running.Guard, guard))
                {
                    runningSlots.Remove(result.SlotId);
                }
            }

            if (!IsDisposed)
            {
                try
                {
                    BeginInvoke(RefreshSlotChips);
                }
                catch (InvalidOperationException)
                {
                    // The launcher is already closing.
                }
            }
        }

        process.Exited += (_, _) => CleanupRuntimeGuard();
        try
        {
            process.EnableRaisingEvents = true;
            if (process.HasExited)
            {
                CleanupRuntimeGuard();
            }
        }
        catch
        {
            CleanupRuntimeGuard();
            throw;
        }
    }

    private LaunchConfig ReadFormConfig()
    {
        WriteFieldsToSelectedSlot();
        var selected = SelectedSlot();
        return new LaunchConfig
        {
            InstallPath = startupInstallDirectory ?? installPathTextBox.Text.Trim(),
            TicketKey = selected.Account.Trim(),
            Host = hostTextBox.Text.Trim(),
            Port = selected.Port,
            VArg = selected.Password,
            ZArg = zoneKeyTextBox.Text.Trim(),
            UseNoDisplayMode = useNoDisplayModeCheckBox.Checked,
            SeedSafeGraphics = seedSafeGraphicsCheckBox.Checked,
            Skin = currentSkin,
            Slots =
            [
                CopySlot(slotOne),
                CopySlot(slotTwo),
            ],
            SelectedSlotId = selectedSlotId,
            PreserveLegacyMulticlient = startupInstallDirectory is not null,
            AnotherClientRunning = IsAnotherSlotRunning(selectedSlotId),
        };
    }

    private void LaunchCompanionTool(string toolName, params string[] alternateNames)
    {
        var installDirectory = GetInstallDirectory();
        if (installDirectory is null)
        {
            MessageBox.Show(this, "Set a valid install folder first.", LauncherName);
            return;
        }

        var toolPath = FindFileCaseInsensitive(installDirectory, toolName, alternateNames);
        if (toolPath is null)
        {
            MessageBox.Show(this, $"Missing {toolName} in {installDirectory}", LauncherName);
            return;
        }

        // ACD3DSetup (and any other companion tool) needs the dgVoodoo DirectDraw
        // wrapper in place *before* it runs, the same way Play ensures it - otherwise
        // a player who checks 3D settings before ever clicking Play sees their real
        // GPU/driver instead of the wrapper, and the tool's saved settings won't
        // reflect what the wrapped client will actually use.
        GraphicsBootstrap.EnsureDirectDrawWrapper(installDirectory, ClientLauncher.GetRepositoryToolsDirectory());

        Process.Start(new ProcessStartInfo
        {
            FileName = toolPath,
            WorkingDirectory = installDirectory,
            UseShellExecute = true,
        });
    }

    private static string? FindFileCaseInsensitive(string directory, string primaryName, params string[] alternateNames)
    {
        var candidates = new[] { primaryName }.Concat(alternateNames);
        foreach (var candidate in candidates)
        {
            var exactPath = Path.Combine(directory, candidate);
            if (File.Exists(exactPath))
            {
                return exactPath;
            }
        }

        return Directory
            .EnumerateFiles(directory)
            .FirstOrDefault(path =>
                candidates.Any(candidate =>
                    Path.GetFileName(path).Equals(candidate, StringComparison.OrdinalIgnoreCase)));
    }

    private string? GetInstallDirectory()
    {
        return ClientLauncher.ResolveInstallDirectory(
            startupInstallDirectory ?? installPathTextBox.Text);
    }

    private void LoadConfigIntoControls()
    {
        var (config, configLoadedFromDisk) = LoadConfig();

        installPathTextBox.Text = config.InstallPath;
        hostTextBox.Text = config.Host;
        CopySlotsFrom(config);
        LoadSelectedSlotFields();
        zoneKeyTextBox.Text = config.ZArg;
        useNoDisplayModeCheckBox.Checked = config.UseNoDisplayMode;
        seedSafeGraphicsCheckBox.Checked = config.SeedSafeGraphics;
        ApplySkin(config.Skin, save: false);

        if (string.IsNullOrWhiteSpace(installPathTextBox.Text))
        {
            installPathTextBox.Text = FindDefaultInstallDirectory() ?? LaunchConfig.DefaultInstallPath;
        }

        if (!configLoadedFromDisk && string.IsNullOrWhiteSpace(config.Host))
        {
            ApplyLauncherIniDefaults();
        }

        ClientLauncher.RemoveLegacyProfileStore();
        var installDirectory = GetInstallDirectory();
        if (startupInstallDirectory is null && installDirectory is not null)
        {
            ClientLauncher.RemoveLegacyMulticlientFolder(installDirectory);
        }
    }

    private void ApplyLauncherIniDefaults()
    {
        var installDirectory = GetInstallDirectory();
        if (installDirectory is null)
        {
            return;
        }

        var dataCenter = LauncherIniReader.ReadPrimaryDataCenter(installDirectory);
        if (dataCenter is null)
        {
            return;
        }

        hostTextBox.Text = dataCenter.ServerAddress;
        serverChoice.SetRedSelected(dataCenter.ServerPort == AetheriumInstallationConfiguration.RedServerPort);
        WriteFieldsToSelectedSlot();
        RefreshSlotChips();
    }

    private void SaveControlsToConfig()
    {
        var config = ReadFormConfig();

        var configDirectory = Path.GetDirectoryName(GetConfigPath(config.InstallPath));
        if (!string.IsNullOrEmpty(configDirectory))
        {
            Directory.CreateDirectory(configDirectory);
        }

        File.WriteAllText(
            GetConfigPath(config.InstallPath),
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        // The installer writes this one-shot preference so its wizard choice wins on first run
        // without replacing an existing launcher.json (and its saved account/server fields).
        try
        {
            File.Delete(GetInstallerSkinPreferencePath());
        }
        catch
        {
            // The JSON setting still persists the user's choice if the install folder is read-only.
        }
    }

    private static string GetConfigPath(string installPath)
    {
        if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
        {
            return Path.Combine(installPath, LegacyConfigFileName);
        }

        return Path.Combine(LegacyConfigDirectory, LegacyConfigFileName);
    }

    private (LaunchConfig Config, bool LoadedFromDisk) LoadConfig()
    {
        var installPath = startupInstallDirectory ?? installPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(installPath))
        {
            installPath = FindDefaultInstallDirectory() ?? LaunchConfig.DefaultInstallPath;
        }

        foreach (var configPath in GetConfigPathCandidates(installPath))
        {
            if (!File.Exists(configPath))
            {
                continue;
            }

            try
            {
                var config = JsonSerializer.Deserialize<LaunchConfig>(File.ReadAllText(configPath)) ?? new LaunchConfig();
                if (startupInstallDirectory is not null)
                {
                    config.InstallPath = startupInstallDirectory;
                }
                else if (string.IsNullOrWhiteSpace(config.InstallPath))
                {
                    config.InstallPath = installPath;
                }

                config.Skin = ReadInstallerSkinPreference() ?? NormalizeSkinName(config.Skin);

                return (config, true);
            }
            catch
            {
                // Try the next config location.
            }
        }

        return (new LaunchConfig
        {
            InstallPath = installPath,
            Skin = ReadInstallerSkinPreference() ?? DefaultSkinName,
        }, false);
    }

    private static IEnumerable<string> GetConfigPathCandidates(string installPath)
    {
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            yield return Path.Combine(installPath, LegacyConfigFileName);
        }

        yield return Path.Combine(LegacyConfigDirectory, LegacyConfigFileName);
    }

    private static string? FindDefaultInstallDirectory()
    {
        var configuredDirectory = AetheriumInstallationConfiguration.TryReadGameInstallDirectory();
        if (configuredDirectory is not null)
        {
            return configuredDirectory;
        }

        var directCandidates = new[]
        {
            LaunchConfig.DefaultInstallPath,
            @"C:\Turbine\Asheron's Call",
            @"C:\Program Files (x86)\Turbine\Asheron's Call",
            @"C:\Program Files\Turbine\Asheron's Call",
            @"C:\Turbine Entertainment Software\Asheron's Call",
            @"C:\Program Files (x86)\Turbine Entertainment Software\Asheron's Call",
            @"C:\Program Files\Turbine Entertainment Software\Asheron's Call",
        };

        var direct = directCandidates.FirstOrDefault(candidate => File.Exists(Path.Combine(candidate, "client.exe")));
        if (direct is not null)
        {
            return direct;
        }

        // Fall back to scanning known publisher folders for whatever the actual
        // game subfolder is named. Different AC releases/locales can spell
        // "Asheron's Call" with a different apostrophe character, which would
        // silently miss the hardcoded candidates above even though the real
        // install folder is right there.
        var parentCandidates = new[]
        {
            @"C:\Turbine",
            @"C:\Turbine Entertainment Software",
            @"C:\Program Files\Turbine",
            @"C:\Program Files (x86)\Turbine",
            @"C:\Program Files\Turbine Entertainment Software",
            @"C:\Program Files (x86)\Turbine Entertainment Software",
        };

        foreach (var parent in parentCandidates)
        {
            if (!Directory.Exists(parent))
            {
                continue;
            }

            foreach (var subDir in Directory.EnumerateDirectories(parent))
            {
                if (File.Exists(Path.Combine(subDir, "client.exe")))
                {
                    return subDir;
                }
            }
        }

        return null;
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
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

    private static Color AdjustColor(Color color, int delta)
    {
        return Color.FromArgb(
            color.A,
            Math.Clamp(color.R + delta, 0, 255),
            Math.Clamp(color.G + delta, 0, 255),
            Math.Clamp(color.B + delta, 0, 255));
    }

    private sealed class TransparentOverlayPanel : Panel
    {
        public TransparentOverlayPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            try
            {
                BackColor = Color.Transparent;
            }
            catch (ArgumentException)
            {
                BackColor = EntryFill;
            }
        }
    }

    private sealed class ZoneEraSurface : Panel
    {
        private readonly Image? backdrop;
        private Image? featureArtwork;
        private readonly Image? playButton;
        private readonly Bitmap grain = CreateGrainBitmap(160);
        private bool usePkSkin;

        public ZoneEraSurface(string backdropPath, string featureArtworkPath, string playButtonPath)
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            if (File.Exists(backdropPath))
            {
                using var fullBackdrop = Image.FromFile(backdropPath);
                using var croppedShell = CropBackdropShell(fullBackdrop);
                backdrop = SharpenImage(croppedShell, 0.15f);
            }

            SetFeatureArtwork(featureArtworkPath, pkSkin: false);

            if (File.Exists(playButtonPath))
            {
                // The play PNG is only 234×92 but renders at roughly double that, so pre-upscale
                // it once with bicubic and sharpen; drawing it near 1:1 afterwards keeps it crisp.
                using var rawPlay = Image.FromFile(playButtonPath);
                using var upscaledPlay = ResizeImage(rawPlay, rawPlay.Width * 2, rawPlay.Height * 2);
                using var enhancedPlay = EnhanceImage(upscaledPlay, contrast: 1.1f, saturation: 1.15f, brightness: 0.02f);
                playButton = SharpenImage(enhancedPlay, 0.45f);
            }
        }

        public void SetFeatureArtwork(string featureArtworkPath, bool pkSkin)
        {
            Image? replacement = null;
            if (File.Exists(featureArtworkPath))
            {
                using var rawArtwork = Image.FromFile(featureArtworkPath);
                if (pkSkin)
                {
                    replacement = new Bitmap(rawArtwork);
                }
                else
                {
                    using var enhancedArtwork = EnhanceImage(rawArtwork, contrast: 1.12f, saturation: 1.18f, brightness: 0.02f);
                    replacement = SharpenImage(enhancedArtwork, 0.4f);
                }
            }

            var previous = featureArtwork;
            featureArtwork = replacement;
            usePkSkin = pkSkin;
            previous?.Dispose();
            Invalidate();
        }

        // Contrast/saturation/brightness lift applied once at load via a ColorMatrix. This only
        // enhances the existing pixels (no repainting or re-authoring); alpha is preserved so the
        // play button's soft glow stays intact.
        private static Bitmap EnhanceImage(Image source, float contrast, float saturation, float brightness)
        {
            var lumR = (1f - saturation) * 0.3086f;
            var lumG = (1f - saturation) * 0.6094f;
            var lumB = (1f - saturation) * 0.0820f;
            var c = contrast;
            var t = ((1f - contrast) / 2f) + brightness;
            var matrix = new ColorMatrix(new[]
            {
                new[] { c * (lumR + saturation), c * lumR, c * lumR, 0f, 0f },
                new[] { c * lumG, c * (lumG + saturation), c * lumG, 0f, 0f },
                new[] { c * lumB, c * lumB, c * (lumB + saturation), 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { t, t, t, 0f, 1f },
            });

            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            g.DrawImage(
                source,
                new Rectangle(0, 0, source.Width, source.Height),
                0, 0, source.Width, source.Height,
                GraphicsUnit.Pixel,
                attributes);
            return result;
        }

        private static Bitmap ResizeImage(Image source, int width, int height)
        {
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, width, height));
            return result;
        }

        // Simple unsharp mask (3×3 laplacian) applied once at load time; amount 0.15-0.4 gives a
        // gentle crispness boost without visible halos. Translucent pixels are left untouched so
        // the play button's soft glow edges do not pick up fringes.
        private static Bitmap SharpenImage(Image source, float amount)
        {
            var width = source.Width;
            var height = source.Height;
            using var src = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(src))
            {
                g.DrawImage(source, new Rectangle(0, 0, width, height));
            }

            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var srcData = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var stride = srcData.Stride;
                var input = new byte[stride * height];
                Marshal.Copy(srcData.Scan0, input, 0, input.Length);
                var output = (byte[])input.Clone();

                for (var y = 1; y < height - 1; y++)
                {
                    for (var x = 1; x < width - 1; x++)
                    {
                        var idx = (y * stride) + (x * 4);
                        if (input[idx + 3] != 255)
                        {
                            continue;
                        }

                        for (var channel = 0; channel < 3; channel++)
                        {
                            var center = input[idx + channel];
                            var neighbors =
                                input[idx - 4 + channel] +
                                input[idx + 4 + channel] +
                                input[idx - stride + channel] +
                                input[idx + stride + channel];
                            var sharpened = center + (amount * ((4 * center) - neighbors));
                            output[idx + channel] = (byte)Math.Clamp((int)MathF.Round(sharpened), 0, 255);
                        }
                    }
                }

                var resultData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    Marshal.Copy(output, 0, resultData.Scan0, output.Length);
                }
                finally
                {
                    result.UnlockBits(resultData);
                }
            }
            finally
            {
                src.UnlockBits(srcData);
            }

            return result;
        }

        // The authored PNG surrounds the launcher shell with a black margin; crop it away so the
        // gold window frame sits flush with the form edges instead of floating in a black box.
        private static Bitmap CropBackdropShell(Image fullBackdrop)
        {
            var crop = Rectangle.Intersect(
                BackdropShellCropRect,
                new Rectangle(0, 0, fullBackdrop.Width, fullBackdrop.Height));
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                crop = new Rectangle(0, 0, fullBackdrop.Width, fullBackdrop.Height);
            }

            var shell = new Bitmap(crop.Width, crop.Height);
            using var g = Graphics.FromImage(shell);
            g.DrawImage(fullBackdrop, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
            return shell;
        }

        public Rectangle SceneBounds
        {
            get
            {
                if (backdrop is null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
                {
                    return ClientRectangle;
                }

                var scale = Math.Min(
                    ClientSize.Width / (float)backdrop.Width,
                    ClientSize.Height / (float)backdrop.Height);

                var width = (int)Math.Round(backdrop.Width * scale);
                var height = (int)Math.Round(backdrop.Height * scale);
                var left = (ClientSize.Width - width) / 2;
                var top = (ClientSize.Height - height) / 2;
                return new Rectangle(left, top, width, height);
            }
        }

        public Rectangle MapFromDesign(RectangleF designRect, Size designSize)
        {
            var scene = SceneBounds;
            var scaleX = scene.Width / (float)designSize.Width;
            var scaleY = scene.Height / (float)designSize.Height;

            return new Rectangle(
                scene.Left + (int)Math.Round(designRect.X * scaleX),
                scene.Top + (int)Math.Round(designRect.Y * scaleY),
                Math.Max(1, (int)Math.Round(designRect.Width * scaleX)),
                Math.Max(1, (int)Math.Round(designRect.Height * scaleY)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.Clear(Color.Black);
            if (backdrop is null)
            {
                return;
            }

            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // 1. Draw the clean launcher shell.
            e.Graphics.DrawImage(backdrop, SceneBounds);
            // 2. Drop the Asheron's Call artwork into the left presentation panel.
            DrawTopOverlay(e.Graphics);
            // 3. Draw the title and menu labels directly onto the painted surface.
            DrawChromeText(e.Graphics);
            // 4. Draw the PLAY button artwork.
            DrawPlayButton(e.Graphics);
        }

        private void DrawTopOverlay(Graphics g)
        {
            if (featureArtwork is null)
            {
                return;
            }

            var destination = MapFromDesign(FeatureArtworkDestRect, BackdropDesignSize);
            using var baseBrush = new SolidBrush(Color.Black);
            g.FillRectangle(baseBrush, destination);

            var previousInterpolation = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(
                featureArtwork,
                destination,
                usePkSkin
                    ? new RectangleF(0, 0, featureArtwork.Width, featureArtwork.Height)
                    : FeatureArtworkSrcRect,
                GraphicsUnit.Pixel);

            if (!usePkSkin)
            {
                // The Default artwork needs its parchment enlarged for the full-size form. The PK
                // skin keeps the supplied image untouched and uses its original parchment instead.
                g.DrawImage(
                    featureArtwork,
                    MapFromDesign(FeatureParchmentDestRect, BackdropDesignSize),
                    FeatureParchmentSrcRect,
                    GraphicsUnit.Pixel);
            }
            g.InterpolationMode = previousInterpolation;

            var parchment = MapFromDesign(
                usePkSkin ? PkConfigOverlayRect : FeatureParchmentDestRect,
                BackdropDesignSize);
            using (var grainClip = new Region(destination))
            {
                grainClip.Exclude(parchment);
                g.SetClip(grainClip, CombineMode.Replace);
                DrawGrain(g, destination);
                g.ResetClip();
            }
        }

        private void DrawGrain(Graphics g, Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using var brush = new TextureBrush(grain, WrapMode.Tile);
            g.FillRectangle(brush, bounds);
        }

        private static Bitmap CreateGrainBitmap(int size)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, size, size);
            var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[Math.Abs(data.Stride) * size];
                var rng = new Random(0x6A17E91);
                for (var i = 0; i < bytes.Length; i += 4)
                {
                    var speckle = rng.Next(40, 220);
                    var alpha = rng.Next(0, 22);
                    if (rng.Next(0, 22) == 0)
                    {
                        alpha = rng.Next(24, 48);
                    }

                    bytes[i] = (byte)(speckle * 55 / 100);
                    bytes[i + 1] = (byte)(speckle * 72 / 100);
                    bytes[i + 2] = (byte)speckle;
                    bytes[i + 3] = (byte)alpha;
                }

                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        private void DrawChromeText(Graphics g)
        {
            var scene = SceneBounds;
            if (scene.IsEmpty)
            {
                return;
            }

            var sceneScale = scene.Height / (float)BackdropDesignSize.Height;
            var previousHint = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var textFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
            };

            // Launcher name centered vertically in the orange title-bar strip.
            var titleRect = MapFromDesign(WindowTitleTextRect, BackdropDesignSize);
            var titlePt = Math.Max(10.5f, 16.5f * sceneScale);
            using var titleBrush = new SolidBrush(ChromeTitleColor);
            using (var titleFont = new Font("Georgia", titlePt, FontStyle.Italic, GraphicsUnit.Point))
            {
                g.DrawString(LauncherName, titleFont, titleBrush, titleRect, textFormat);
            }

            // Room  Zone  Tools  Help — blue italic-serif "links" packed at the left, matching the
            // original Zone-era launcher. Left-aligned so they read as a tight menu, not spread tabs.
            var menuPt = Math.Max(9f, 11f * sceneScale);
            using var menuBrush = new SolidBrush(ChromeMenuColor);
            using (var menuFont = new Font("Georgia", menuPt, FontStyle.Bold | FontStyle.Italic | FontStyle.Underline, GraphicsUnit.Point))
            {
                g.DrawString("Room", menuFont, menuBrush, MapFromDesign(RoomTextRect, BackdropDesignSize), textFormat);
                g.DrawString("Zone", menuFont, menuBrush, MapFromDesign(ZoneTextRect, BackdropDesignSize), textFormat);
                g.DrawString("Tools", menuFont, menuBrush, MapFromDesign(ToolsTextRect, BackdropDesignSize), textFormat);
                g.DrawString("Help", menuFont, menuBrush, MapFromDesign(HelpTextRect, BackdropDesignSize), textFormat);
            }

            g.TextRenderingHint = previousHint;
        }

        private void DrawPlayButton(Graphics graphics)
        {
            if (playButton is null)
            {
                return;
            }

            var destination = MapFromDesign(PlayArtworkRect, BackdropDesignSize);
            graphics.DrawImage(playButton, destination);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                backdrop?.Dispose();
                featureArtwork?.Dispose();
                playButton?.Dispose();
                grain.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class LedgerField : Panel
    {
        private readonly TextBox inner;

        public LedgerField(TextBox inner)
        {
            this.inner = inner;
            DoubleBuffered = true;
            BackColor = EntryFill;
            Padding = new Padding(5, 3, 5, 3);
            Height = 26;
            Margin = Padding.Empty;
            inner.BorderStyle = BorderStyle.None;
            inner.Dock = DockStyle.Fill;
            inner.Margin = Padding.Empty;
            Controls.Add(inner);
        }

        public void ApplyScale(float scale)
        {
            Padding = ScalePadding(new Padding(5, 3, 5, 3), scale);
            Height = ScaleInt(26, scale);
            inner.Font = CreateScaledFont("Trebuchet MS", 9f, FontStyle.Bold, scale);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(EntryFill);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var dark = new Pen(EntryInsetDark);
            using var light = new Pen(EntryInsetLight);
            e.Graphics.DrawLine(dark, rect.Left, rect.Top, rect.Right, rect.Top);
            e.Graphics.DrawLine(dark, rect.Left, rect.Top, rect.Left, rect.Bottom);
            e.Graphics.DrawLine(light, rect.Left, rect.Bottom, rect.Right, rect.Bottom);
            e.Graphics.DrawLine(light, rect.Right, rect.Top, rect.Right, rect.Bottom);
        }
    }

    private sealed class InkToggle : Control
    {
        private bool isChecked;

        public bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value)
                {
                    return;
                }

                isChecked = value;
                Invalidate();
            }
        }

        public InkToggle()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            try
            {
                BackColor = Color.Transparent;
            }
            catch (ArgumentException)
            {
                // Wine's WinForms rejects Transparent on a raw Control.
                BackColor = EntryFill;
            }
            ForeColor = InkBrown;
            Font = new Font("Georgia", 9.25f, FontStyle.Bold, GraphicsUnit.Point);
            Cursor = Cursors.Hand;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            ResizeToText();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ResizeToText();
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        private void ResizeToText()
        {
            var text = string.IsNullOrEmpty(Text) ? "X" : Text;
            var textSize = TextRenderer.MeasureText(
                text,
                Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            Size = new Size(textSize.Width + 28, Math.Max(22, textSize.Height + 6));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var box = 13;
            var boxRect = new Rectangle(1, Math.Max(1, (Height - box) / 2), box, box);
            using (var fill = new SolidBrush(EntryFill))
            using (var border = new Pen(EntryInsetDark))
            {
                g.FillRectangle(fill, boxRect);
                g.DrawRectangle(border, boxRect);
            }

            if (Checked)
            {
                using var mark = new Pen(InkBrown, 2f);
                g.DrawLines(mark, new[]
                {
                    new Point(boxRect.Left + 2, boxRect.Top + 7),
                    new Point(boxRect.Left + 5, boxRect.Bottom - 3),
                    new Point(boxRect.Right - 2, boxRect.Top + 3),
                });
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                new Rectangle(boxRect.Right + 8, 0, Math.Max(1, Width - boxRect.Right - 8), Height),
                ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    private sealed class RunningSlot
    {
        public required string SlotId { get; init; }

        public required ClientAntiTamperRuntimeGuard Guard { get; init; }

        public required Process Process { get; init; }
    }

    private sealed class ServerChoiceBar : FlowLayoutPanel
    {
        private readonly ArcaneButton whiteButton;
        private readonly ArcaneButton redButton;
        private readonly ToolTip toolTip = new();

        public event EventHandler? SelectionChanged;

        public bool RedSelected { get; private set; }

        public ServerChoiceBar()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            try
            {
                BackColor = Color.Transparent;
            }
            catch (ArgumentException)
            {
                BackColor = EntryFill;
            }
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = false;
            Margin = Padding.Empty;
            Padding = Padding.Empty;

            whiteButton = CreateServerButton("White", ArcaneButton.ButtonPalette.Bone);
            redButton = CreateServerButton("Red", ArcaneButton.ButtonPalette.Blood);
            toolTip.SetToolTip(whiteButton, "White server · port 9000");
            toolTip.SetToolTip(redButton, "Red server · port 9100");
            Controls.Add(whiteButton);
            Controls.Add(redButton);
            SetRedSelected(false);
        }

        public void SetRedSelected(bool red)
        {
            RedSelected = red;
            whiteButton.Chosen = !red;
            redButton.Chosen = red;
            whiteButton.Invalidate();
            redButton.Invalidate();
        }

        public void ApplyScale(float scale, bool compact)
        {
            var width = Math.Max(compact ? 58 : 78, ScaleInt(90, scale));
            var height = Math.Max(compact ? 20 : 24, ScaleInt(28, scale));
            var font = CreateScaledFont("Georgia", 9.5f, FontStyle.Bold, scale, compact ? 7f : 8.5f);
            whiteButton.Width = width;
            redButton.Width = width;
            whiteButton.Height = height;
            redButton.Height = height;
            whiteButton.Margin = ScalePadding(new Padding(0, 0, 8, 0), scale);
            redButton.Margin = Padding.Empty;
            whiteButton.Font = font;
            redButton.Font = CreateScaledFont("Georgia", 9.5f, FontStyle.Bold, scale, compact ? 7f : 8.5f);
        }

        private ArcaneButton CreateServerButton(string text, ArcaneButton.ButtonPalette palette)
        {
            var button = new ArcaneButton
            {
                Text = text,
                Palette = palette,
                Width = 90,
                Height = 28,
                Margin = palette == ArcaneButton.ButtonPalette.Blood
                    ? Padding.Empty
                    : new Padding(0, 0, 8, 0),
                Font = new Font("Georgia", 9.5f, FontStyle.Bold, GraphicsUnit.Point),
            };
            button.Click += (_, _) => SelectRed(palette == ArcaneButton.ButtonPalette.Blood);
            return button;
        }

        private void SelectRed(bool red)
        {
            if (RedSelected == red)
            {
                return;
            }

            SetRedSelected(red);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class ArcaneButton : Button
    {
        private bool isHovering;
        private bool isPressed;

        public bool Primary { get; init; }

        public ButtonPalette Palette { get; init; }

        private bool chosen;

        public bool Chosen
        {
            get => chosen;
            set
            {
                if (chosen == value)
                {
                    return;
                }

                chosen = value;
                Invalidate();
            }
        }

        public enum ButtonPalette
        {
            Brass,
            Bone,
            Blood,
        }

        public ArcaneButton()
        {
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = ParchmentPatch;
            FlatAppearance.MouseDownBackColor = ParchmentPatch;
            BackColor = ParchmentPatch;
            ForeColor = Color.FromArgb(238, 210, 122);
            UseVisualStyleBackColor = false;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.Opaque |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovering = true;
            Invalidate(false);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovering = false;
            isPressed = false;
            Invalidate(false);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            isPressed = true;
            Invalidate(false);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            isPressed = false;
            Invalidate(false);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var patch = new SolidBrush(ParchmentPatch);
            e.Graphics.FillRectangle(patch, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var patch = new SolidBrush(ParchmentPatch);
            e.Graphics.FillRectangle(patch, ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.None;
            if (Primary)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                PaintPrimary(e);
            }
            else
            {
                PaintSecondary(e);
            }
        }

        private void PaintPrimary(PaintEventArgs e)
        {
            var rect = new Rectangle(4, 6, Math.Max(20, Width - 12), Math.Max(20, Height - 12));
            using (var shadowPath = CreateRoundedPath(new Rectangle(rect.Left + 5, rect.Top + 5, rect.Width, rect.Height), 28))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            var start = isPressed ? AdjustColor(VioletDark, -10) : isHovering ? AdjustColor(VioletDark, 6) : VioletDark;
            var end = isPressed ? AdjustColor(Color.Black, 10) : Color.Black;

            using (var path = CreateRoundedPath(rect, 28))
            using (var fillBrush = new LinearGradientBrush(rect, start, end, LinearGradientMode.Horizontal))
            {
                e.Graphics.FillPath(fillBrush, path);
                using var outlinePen = new Pen(Color.FromArgb(182, 109, 199), 2.5f);
                e.Graphics.DrawPath(outlinePen, path);
            }

            using (var orbPath = new GraphicsPath())
            {
                var orbRect = new Rectangle(Math.Max(10, Width - 88), 14, 58, 58);
                orbPath.AddEllipse(orbRect);
                using var orbBrush = new PathGradientBrush(orbPath)
                {
                    CenterColor = Color.FromArgb(240, AdjustColor(VioletGlow, isHovering ? 20 : 0)),
                    SurroundColors = [Color.Transparent],
                };
                e.Graphics.FillPath(orbBrush, orbPath);

                using var orbPen = new Pen(Color.FromArgb(235, 209, 140), 1.5f);
                e.Graphics.DrawArc(orbPen, orbRect, 15, 290);
                e.Graphics.DrawArc(orbPen, orbRect.X + 8, orbRect.Y + 8, orbRect.Width - 16, orbRect.Height - 16, 40, 280);
            }

            using var font = new Font("Georgia", 18f, FontStyle.Bold, GraphicsUnit.Point);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                font,
                new Rectangle(0, 6, Math.Max(10, Width - 34), Math.Max(10, Height - 8)),
                Color.FromArgb(238, 208, 126),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void PaintSecondary(PaintEventArgs e)
        {
            var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            var (start, end, border, text) = GetPaletteColors();
            if (isHovering && !Chosen)
            {
                start = AdjustColor(start, 10);
                end = AdjustColor(end, 8);
            }

            if (isPressed)
            {
                start = AdjustColor(start, -10);
                end = AdjustColor(end, -10);
            }

            using (var fillBrush = new LinearGradientBrush(rect, start, end, LinearGradientMode.Vertical))
            {
                e.Graphics.FillRectangle(fillBrush, rect);
                using var borderPen = new Pen(border);
                e.Graphics.DrawRectangle(borderPen, rect);
            }

            if (Chosen)
            {
                var inset = Rectangle.Inflate(rect, -3, -3);
                using var insetPen = new Pen(Color.FromArgb(140, 40, 18, 6));
                e.Graphics.DrawRectangle(insetPen, inset);
            }

            var textRect = rect;
            if (isPressed || Chosen)
            {
                textRect.Offset(0, 1);
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private (Color Start, Color End, Color Border, Color Text) GetPaletteColors()
        {
            if (Chosen && Palette == ButtonPalette.Bone)
            {
                return (
                    Color.FromArgb(248, 236, 204),
                    Color.FromArgb(214, 188, 132),
                    Color.FromArgb(92, 56, 18),
                    Color.FromArgb(32, 16, 6));
            }

            if (Chosen && Palette == ButtonPalette.Blood)
            {
                return (
                    Color.FromArgb(168, 36, 28),
                    Color.FromArgb(96, 12, 8),
                    Color.FromArgb(228, 176, 96),
                    Color.FromArgb(255, 236, 210));
            }

            return (
                Color.FromArgb(145, 92, 40),
                Color.FromArgb(82, 48, 19),
                Color.FromArgb(223, 186, 114),
                Color.FromArgb(246, 223, 171));
        }
    }

    private enum SurfaceHitTarget
    {
        None,
        FileMenu,
        ZoneLaunch,
        ToolsMenu,
        Help,
        PlayLaunch,
        TitleDrag,
        Minimize,
        Maximize,
        Close,
    }

    private static class Win32Native
    {
        public const int WmNcLButtonDown = 0xA1;
        public const int HtCaption = 0x02;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern nint SendMessage(IntPtr hWnd, int msg, nint wParam, nint lParam);
    }
}

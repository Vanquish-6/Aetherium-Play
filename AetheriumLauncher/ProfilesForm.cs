namespace AcLegacyLauncher;

/// <summary>
/// Separate dialog for saved account presets. Keeps the Zone parchment form unchanged.
/// </summary>
public sealed class ProfilesForm : Form
{
    private readonly ListView profileList = new();
    private readonly TextBox nameTextBox = new();
    private readonly TextBox accountTextBox = new();
    private readonly TextBox passwordTextBox = new();
    private readonly TextBox hostTextBox = new();
    private readonly NumericUpDown portNumeric = new();
    private readonly TextBox zoneTextBox = new();
    private readonly TextBox installPathTextBox = new();
    private readonly CheckBox useNoDisplayModeCheckBox = new();
    private readonly CheckBox seedSafeGraphicsCheckBox = new();
    private readonly Label statusLabel = new();

    private readonly Func<LaunchConfig> readCurrentFormConfig;
    private readonly Action<LaunchConfig> applyToForm;
    private readonly Action<string> reportStatus;
    private readonly string? dgVoodooToolsDirectory;

    private ProfileStoreData store = new();
    private string? selectedProfileId;

    public ProfilesForm(
        Func<LaunchConfig> readCurrentFormConfig,
        Action<LaunchConfig> applyToForm,
        Action<string> reportStatus,
        string? dgVoodooToolsDirectory = null)
    {
        this.readCurrentFormConfig = readCurrentFormConfig;
        this.applyToForm = applyToForm;
        this.reportStatus = reportStatus;
        this.dgVoodooToolsDirectory = dgVoodooToolsDirectory;

        Text = "Client Profiles";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 460);
        Font = new Font("Segoe UI", 9f);

        BuildLayout();
        ReloadStore();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        profileList.Dock = DockStyle.Fill;
        profileList.View = View.Details;
        profileList.FullRowSelect = true;
        profileList.HideSelection = false;
        profileList.MultiSelect = true;
        profileList.Columns.Add("Profile", 120);
        profileList.Columns.Add("Account", 120);
        profileList.SelectedIndexChanged += (_, _) => LoadSelectedIntoEditor();
        root.Controls.Add(profileList, 0, 0);

        root.Controls.Add(BuildEditorPanel(), 1, 0);
        var buttonBar = BuildButtonBar();
        root.Controls.Add(buttonBar, 0, 1);
        root.SetColumnSpan(buttonBar, 2);
    }

    private Control BuildEditorPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(8, 0, 0, 0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        passwordTextBox.UseSystemPasswordChar = true;
        portNumeric.Minimum = 1;
        portNumeric.Maximum = 65535;
        portNumeric.Value = 9000;
        useNoDisplayModeCheckBox.Text = "No display mode (-nd)";
        seedSafeGraphicsCheckBox.Text = "Safe graphics (shared registry)";

        AddEditorRow(panel, "Name", nameTextBox);
        AddEditorRow(panel, "Account", accountTextBox);
        AddEditorRow(panel, "Password", passwordTextBox);
        AddEditorRow(panel, "Host", hostTextBox);
        AddEditorRow(panel, "Port", portNumeric);
        AddEditorRow(panel, "Zone", zoneTextBox);
        AddEditorRow(panel, "Install", installPathTextBox);

        panel.Controls.Add(new Label()); // spacer cell
        var options = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Dock = DockStyle.Fill,
        };
        options.Controls.Add(useNoDisplayModeCheckBox);
        options.Controls.Add(seedSafeGraphicsCheckBox);
        panel.Controls.Add(options);

        return panel;
    }

    private static void AddEditorRow(TableLayoutPanel panel, string label, Control input)
    {
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 6, 0),
        });
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 4, 0, 0);
        panel.Controls.Add(input);
    }

    private Control BuildButtonBar()
    {
        var bar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 0),
        };

        bar.Controls.Add(MakeButton("Save Profile", SaveEditorProfile));
        bar.Controls.Add(MakeButton("New from Form", AddFromCurrentForm));
        bar.Controls.Add(MakeButton("Load into Form", LoadIntoMainForm));
        bar.Controls.Add(MakeButton("Delete", DeleteSelected));
        bar.Controls.Add(MakeButton("Launch", LaunchSelected));
        bar.Controls.Add(MakeButton("Close", (_, _) => Close()));

        statusLabel.AutoSize = true;
        statusLabel.ForeColor = Color.DimGray;
        statusLabel.Margin = new Padding(8, 8, 0, 0);
        bar.Controls.Add(statusLabel);

        return bar;
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 0, 6, 4),
        };
        button.Click += onClick;
        return button;
    }

    private void ReloadStore()
    {
        store = ProfileStore.Load();
        RefreshList();
        SetStatus($"{store.Profiles.Count} profile(s) — {ProfileStore.StorePath}");
    }

    private void RefreshList()
    {
        profileList.BeginUpdate();
        profileList.Items.Clear();

        foreach (var profile in store.Profiles.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ListViewItem(profile.DisplayName)
            {
                Tag = profile.Id,
            };
            item.SubItems.Add(profile.AccountName);
            profileList.Items.Add(item);

            if (profile.Id == selectedProfileId)
            {
                item.Selected = true;
            }
        }

        profileList.EndUpdate();

        if (profileList.SelectedItems.Count == 0 && profileList.Items.Count > 0)
        {
            profileList.Items[0].Selected = true;
        }
        else if (profileList.Items.Count == 0)
        {
            ClearEditor();
        }
    }

    private void LoadSelectedIntoEditor()
    {
        if (profileList.SelectedItems.Count != 1)
        {
            return;
        }

        var id = profileList.SelectedItems[0].Tag as string;
        var profile = store.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        selectedProfileId = profile.Id;
        nameTextBox.Text = profile.DisplayName;
        accountTextBox.Text = profile.AccountName;
        passwordTextBox.Text = profile.Password;
        hostTextBox.Text = profile.Host;
        portNumeric.Value = Math.Clamp(profile.Port, (int)portNumeric.Minimum, (int)portNumeric.Maximum);
        zoneTextBox.Text = profile.Zone;
        installPathTextBox.Text = profile.InstallPath;
        useNoDisplayModeCheckBox.Checked = profile.UseNoDisplayMode;
        seedSafeGraphicsCheckBox.Checked = profile.SeedSafeGraphics;
    }

    private void ClearEditor()
    {
        selectedProfileId = null;
        nameTextBox.Clear();
        accountTextBox.Clear();
        passwordTextBox.Clear();
        hostTextBox.Text = AetheriumInstallationConfiguration.DefaultHost;
        portNumeric.Value = AetheriumInstallationConfiguration.DefaultPort;
        zoneTextBox.Clear();
        installPathTextBox.Text = LaunchConfig.DefaultInstallPath;
        useNoDisplayModeCheckBox.Checked = false;
        seedSafeGraphicsCheckBox.Checked = false;
    }

    private ClientProfile ReadEditorProfile()
    {
        return new ClientProfile
        {
            Id = selectedProfileId ?? Guid.NewGuid().ToString("N"),
            DisplayName = string.IsNullOrWhiteSpace(nameTextBox.Text)
                ? accountTextBox.Text.Trim()
                : nameTextBox.Text.Trim(),
            AccountName = accountTextBox.Text.Trim(),
            Password = passwordTextBox.Text,
            Host = hostTextBox.Text.Trim(),
            Port = (int)portNumeric.Value,
            Zone = zoneTextBox.Text.Trim(),
            InstallPath = installPathTextBox.Text.Trim(),
            UseNoDisplayMode = useNoDisplayModeCheckBox.Checked,
            SeedSafeGraphics = seedSafeGraphicsCheckBox.Checked,
        };
    }

    private void SaveEditorProfile(object? sender, EventArgs e)
    {
        var profile = ReadEditorProfile();
        if (string.IsNullOrWhiteSpace(profile.AccountName))
        {
            MessageBox.Show(this, "Account name is required.", Text);
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            profile.DisplayName = profile.AccountName;
        }

        selectedProfileId = ProfileStore.AddOrUpdate(profile).Id;
        ReloadStore();
        SetStatus($"Saved '{profile.DisplayName}'.");
    }

    private void AddFromCurrentForm(object? sender, EventArgs e)
    {
        var config = readCurrentFormConfig();
        if (string.IsNullOrWhiteSpace(config.TicketKey))
        {
            MessageBox.Show(this, "Fill in Account Name on the main launcher first.", Text);
            return;
        }

        var profile = ClientProfile.FromLaunchConfig(config);
        selectedProfileId = ProfileStore.AddOrUpdate(profile).Id;
        ReloadStore();
        SetStatus($"Added profile from form: '{profile.DisplayName}'.");
        reportStatus($"Saved profile '{profile.DisplayName}'.");
    }

    private void LoadIntoMainForm(object? sender, EventArgs e)
    {
        var profile = GetSingleSelectedProfile();
        if (profile is null)
        {
            MessageBox.Show(this, "Select one profile to load into the main form.", Text);
            return;
        }

        applyToForm(profile.ToLaunchConfig());
        SetStatus($"Loaded '{profile.DisplayName}' into main form.");
        reportStatus($"Loaded profile '{profile.DisplayName}' into form.");
    }

    private void DeleteSelected(object? sender, EventArgs e)
    {
        var selected = GetSelectedProfiles();
        if (selected.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", selected.Select(p => p.DisplayName));
        if (MessageBox.Show(this, $"Delete {selected.Count} profile(s)?\n{names}", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        foreach (var profile in selected)
        {
            ProfileStore.Remove(profile.Id);
        }

        selectedProfileId = null;
        ReloadStore();
    }

    private void LaunchSelected(object? sender, EventArgs e)
    {
        var profile = GetSingleSelectedProfile();
        if (profile is null)
        {
            MessageBox.Show(this, "Select one profile to launch.", Text);
            return;
        }

        UseWaitCursor = true;
        try
        {
            var result = ClientLauncher.Start(
                profile.ToLaunchConfig(),
                dgVoodooToolsDirectory,
                report: reportStatus);
            if (!string.IsNullOrWhiteSpace(result.MulticlientDetail))
            {
                reportStatus(result.MulticlientDetail);
            }

            reportStatus($"Launched profile '{profile.DisplayName}' from {result.WorkingDirectory}");
            SetStatus($"Launched '{profile.DisplayName}'.");
        }
        catch (Exception ex)
        {
            SetStatus($"Launch failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, Text);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private ClientProfile? GetSingleSelectedProfile()
    {
        var selected = GetSelectedProfiles();
        return selected.Count == 1 ? selected[0] : null;
    }

    private List<ClientProfile> GetSelectedProfiles()
    {
        return profileList.SelectedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag as string)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => store.Profiles.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null)
            .Cast<ClientProfile>()
            .ToList();
    }

    private void SetStatus(string message)
    {
        statusLabel.Text = message;
    }
}

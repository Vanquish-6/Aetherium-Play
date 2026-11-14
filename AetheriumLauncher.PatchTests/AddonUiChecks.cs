using AcLegacyLauncher;
using Microsoft.Win32;

internal static class AddonUiChecks
{
    internal static int Run(string outputDirectory)
    {
        Exception? failure = null;
        var checks = 0;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Directory.CreateDirectory(outputDirectory);
                var cases = new[]
                {
                    ("unavailable",new AethTankAddonPresentation(false,false,false,false,null,null,"Release not available yet.")),
                    ("ready",new AethTankAddonPresentation(true,false,false,false,null,null,"Choose Check for updates.")),
                    ("installed",new AethTankAddonPresentation(true,true,true,false,"0.1.0","0.0.9","Enabled for the next launch.")),
                    ("game-running",new AethTankAddonPresentation(true,true,true,true,"0.1.0","0.0.9","A game is running.")),
                    ("invalid-state",new AethTankAddonPresentation(false,false,false,false,null,null,
                        "The saved add-on metadata is invalid. Uninstall to remove the broken installation; character settings are kept.",true))
                };
                foreach (var (name,presentation) in cases)
                {
                    using var form = AethTankAddonForm.CreatePreview(presentation);
                    form.SavePreview(Path.Combine(outputDirectory,$"addon-{name}.png"));
                    var actions = form.ActionAvailability;
                    Require(!actions.Install,"no unchecked release can be installed",ref checks);
                    Require(actions.Enable==presentation.Installed,"enable control reflects installation",ref checks);
                    Require(actions.Check==presentation.Available,"updates require a configured feed",ref checks);
                    Require(actions.Uninstall==((presentation.Installed||presentation.ManagedStateExists)&&!presentation.GameRunning),
                        "uninstall remains available for corrupt managed state unless a game is running",ref checks);
                    Require(actions.Rollback==(presentation.PreviousVersion is not null&&!presentation.GameRunning),"running game blocks rollback",ref checks);
                    form.Show();Application.DoEvents();CheckBounds(form,ref checks);form.Hide();
                    form.Size=form.MinimumSize;
                    form.SavePreview(Path.Combine(outputDirectory,$"addon-{name}-small.png"));
                    form.Show();Application.DoEvents();CheckBounds(form,ref checks);form.Hide();
                }
                using(var offer=new AethTankInstallOffer("0.1.0","900 KB"))
                {
                    offer.Show();offer.PerformLayout();Application.DoEvents();
                    Require(offer.AcceptButton is null,"Enter does not silently opt in",ref checks);
                    Require(offer.CancelButton is Button { DialogResult:DialogResult.No },"Escape chooses Not now",ref checks);
                    var choices=AllControls(offer).OfType<Button>().Select(button=>button.DialogResult).ToHashSet();
                    Require(choices.SetEquals([DialogResult.Yes,DialogResult.No,DialogResult.Abort]),"explicit install/later/never choices",ref checks);
                    CheckBounds(offer,ref checks);
                    using var bitmap=new Bitmap(offer.Width,offer.Height);
                    offer.DrawToBitmap(bitmap,new Rectangle(Point.Empty,offer.Size));
                    bitmap.Save(Path.Combine(outputDirectory,"addon-install-offer.png"));
                    offer.Hide();
                }
                RegistryChecks(ref checks);
                EnvironmentChecks(ref checks);
#if !DEBUG
                Require(AethTankPluginBootstrap.FindDevelopmentPlugin() is null,
                    "release build cannot use the private.enable developer bypass",ref checks);
#endif
            }
            catch(Exception ex){failure=ex;}
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();thread.Join();
        if(failure is not null){Console.Error.WriteLine(failure);return 1;}
        Console.WriteLine($"PASS: {checks} add-on UI, explicit opt-in, per-slot registry ownership and release-loader checks; no game actions.");
        return 0;
    }

    private static void RegistryChecks(ref int checks)
    {
        var subkey=@"Software\AetheriumPlay\AddonTests\"+Guid.NewGuid().ToString("N");
        try
        {
            using(var key=Registry.CurrentUser.CreateSubKey(subkey,true))
            {
                key.SetValue("ACPlugin",@"C:\Other\other.dll");
                AethTankPluginBootstrap.RestorePreviousPlugin(subkey);
                Require((string?)key.GetValue("ACPlugin")==@"C:\Other\other.dll","unrelated plugin retained",ref checks);
                key.SetValue("AethTankPreviousPlugin",@"C:\Other\other.dll");
                key.SetValue("AethTankManagedPlugin",@"C:\Managed\aeth-tank.dll");
                key.SetValue("ACPlugin",@"C:\Managed\aeth-tank.dll");
                AethTankPluginBootstrap.RestorePreviousPlugin(subkey);
                Require((string?)key.GetValue("ACPlugin")==@"C:\Other\other.dll","previous plugin restored",ref checks);
                Require(key.GetValue("AethTankManagedPlugin") is null&&key.GetValue("AethTankPreviousPlugin") is null,
                    "ownership markers cleared",ref checks);
                key.SetValue("AethTankPreviousPlugin",@"C:\Other\old.dll");
                key.SetValue("AethTankManagedPlugin",@"C:\Managed\aeth-tank.dll");
                key.SetValue("ACPlugin",@"C:\UserChoice\new.dll");
                AethTankPluginBootstrap.RestorePreviousPlugin(subkey);
                Require((string?)key.GetValue("ACPlugin")==@"C:\UserChoice\new.dll","new user choice not overwritten",ref checks);
                key.SetValue("ACPlugin",@"C:\Legacy\aeth-tank.dll");
                AethTankPluginBootstrap.RestorePreviousPlugin(subkey);
                Require(key.GetValue("ACPlugin") is null,"stale legacy registration cleared",ref checks);
                key.SetValue("ACPlugin",@"C:\Managed\aeth-tank.dll");
                key.SetValue("AethTankManagedPlugin",@"C:\Managed\aeth-tank.dll");
                Require(AethTankPluginBootstrap.TryEnable(IntPtr.Zero,"public-retail-1.0.69",subkey,null) is null,
                    "disabled plugin never accesses native process",ref checks);
                Require(key.GetValue("ACPlugin") is null,"disable clears registration before client can load it",ref checks);
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(subkey,false); }
    }

    private static void EnvironmentChecks(ref int checks)
    {
        const string name = "AETH_TANK_DATA_DIR";
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, @"C:\Inherited\WrongLocation");
            var empty = new Dictionary<string, string>();
            var removed = NativeProcess.BuildEnvironmentBlockText(empty, [name]);
            Require(!removed.Split('\0').Any(value => value.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)),
                "developer and disabled launches omit the inherited storage variable", ref checks);
            var managed = NativeProcess.BuildEnvironmentBlockText(
                new Dictionary<string, string> { [name] = @"C:\Managed\Data" }, [name]);
            Require(managed.Split('\0').Count(value => value.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) == 1
                && managed.Split('\0').Contains(name + @"=C:\Managed\Data"),
                "verified managed storage overrides the inherited value once", ref checks);
            var explicitEmpty = NativeProcess.BuildEnvironmentBlockText(
                new Dictionary<string, string> { [name] = string.Empty }, [name]);
            Require(explicitEmpty.Split('\0').Contains(name + "="),
                "explicit empty values stay distinct from removal", ref checks);
            Require(Environment.GetEnvironmentVariable(name) == @"C:\Inherited\WrongLocation",
                "child environment construction leaves the parent unchanged", ref checks);
            Require(managed.EndsWith("\0\0", StringComparison.Ordinal),
                "native environment block retains its double terminator", ref checks);
        }
        finally { Environment.SetEnvironmentVariable(name, previous); }
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach(Control child in root.Controls)
        {
            yield return child;
            foreach(var nested in AllControls(child))yield return nested;
        }
    }

    private static void CheckBounds(Control root,ref int checks)
    {
        foreach(Control child in root.Controls)
        {
            if(!child.Visible)continue;
            Require(child.Left>=0&&child.Top>=0&&child.Right<=root.ClientSize.Width+1&&child.Bottom<=root.ClientSize.Height+1,
                $"control within parent: {child.GetType().Name} / {child.Text}",ref checks);
            CheckBounds(child,ref checks);
        }
    }
    private static void Require(bool value,string reason,ref int checks)
    {
        checks++;if(!value)throw new InvalidOperationException("FAIL: "+reason);
    }
}

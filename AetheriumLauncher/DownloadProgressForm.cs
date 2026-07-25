namespace AcLegacyLauncher;

internal sealed class DownloadProgressForm : Form
{
    private readonly string operationName;
    private readonly Func<IProgress<double>, CancellationToken, Task> operation;
    private readonly ProgressBar progressBar = new();
    private readonly Label statusLabel = new();
    private readonly Button cancelButton = new();
    private readonly CancellationTokenSource cancellation = new();
    private bool allowClose;

    public int ExitCode { get; private set; } = 1;

    public DownloadProgressForm(
        string operationName,
        Func<IProgress<double>, CancellationToken, Task> operation)
    {
        this.operationName = operationName;
        this.operation = operation;

        Text = "Aetherium Play Setup";
        ClientSize = new Size(520, 155);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = true;

        statusLabel.AutoSize = false;
        statusLabel.Location = new Point(24, 20);
        statusLabel.Size = new Size(472, 44);
        statusLabel.Text = $"{operationName}\nConnecting to the disclosed community source...";

        progressBar.Location = new Point(24, 72);
        progressBar.Size = new Size(472, 24);
        progressBar.Minimum = 0;
        progressBar.Maximum = 1000;
        progressBar.Style = ProgressBarStyle.Continuous;

        cancelButton.Text = "Cancel";
        cancelButton.Location = new Point(409, 108);
        cancelButton.Size = new Size(87, 30);
        cancelButton.Click += (_, _) =>
        {
            cancelButton.Enabled = false;
            statusLabel.Text = $"{operationName}\nCancelling safely...";
            cancellation.Cancel();
        };

        Controls.Add(statusLabel);
        Controls.Add(progressBar);
        Controls.Add(cancelButton);
        Shown += async (_, _) => await RunOperationAsync();
        FormClosing += (_, e) =>
        {
            if (!allowClose)
            {
                e.Cancel = true;
                cancelButton.Enabled = false;
                statusLabel.Text = $"{operationName}\nCancelling safely...";
                cancellation.Cancel();
            }
        };
    }

    private async Task RunOperationAsync()
    {
        var progress = new Progress<double>(value =>
        {
            var percent = Math.Clamp(value, 0, 100);
            progressBar.Value = Math.Clamp((int)Math.Round(percent * 10), 0, 1000);
            statusLabel.Text = $"{operationName}\n{percent:0.0}% complete";
        });

        try
        {
            await operation(progress, cancellation.Token);
            progressBar.Value = progressBar.Maximum;
            statusLabel.Text = $"{operationName}\nVerified successfully.";
            ExitCode = 0;
            await Task.Delay(350);
            allowClose = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            ExitCode = 2;
            allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Aetherium Play Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ExitCode = 1;
            allowClose = true;
            Close();
        }
    }
}

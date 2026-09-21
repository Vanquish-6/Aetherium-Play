using System.Net.Sockets;

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
    public Exception? Failure { get; private set; }

    public DownloadProgressForm(
        string operationName,
        Func<IProgress<double>, CancellationToken, Task> operation)
    {
        this.operationName = operationName;
        this.operation = operation;

        Text = "Aetherium Play Setup";
        ClientSize = new Size(520, 168);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = true;

        statusLabel.AutoSize = false;
        statusLabel.Location = new Point(24, 16);
        statusLabel.Size = new Size(472, 56);
        statusLabel.Text =
            $"{operationName}\nConnecting. A slow connection can take several minutes. Leave this window open.";

        progressBar.Location = new Point(24, 80);
        progressBar.Size = new Size(472, 24);
        progressBar.Minimum = 0;
        progressBar.Maximum = 1000;
        progressBar.Style = ProgressBarStyle.Continuous;

        cancelButton.Text = "Cancel";
        cancelButton.Location = new Point(409, 120);
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
            statusLabel.Text =
                $"{operationName}\n{percent:0.0}% complete. A slow connection can take several minutes.";
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
            Failure = ex;
            MessageBox.Show(
                this,
                FormatSetupFailure(ex),
                "Aetherium Play Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ExitCode = 1;
            allowClose = true;
            Close();
        }
    }

    internal static string FormatSetupFailure(Exception exception)
    {
        var root = Unwrap(exception);
        if (LooksLikeMegaStorageDnsFailure(exception))
        {
            return
                "Aetherium Play could not reach MEGA's file servers." +
                Environment.NewLine + Environment.NewLine +
                "Windows could not find the MEGA download host. This is usually a DNS or network problem, or MEGA handed out a storage server that is briefly unavailable." +
                Environment.NewLine + Environment.NewLine +
                "Run Setup again. If it keeps failing, switch DNS to 1.1.1.1 or 8.8.8.8, or briefly disable VPN/filter software that may block mega.co.nz." +
                Environment.NewLine + Environment.NewLine +
                "Details: " + root.Message;
        }

        return root.Message;
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is AggregateException aggregate &&
               aggregate.InnerException is not null)
        {
            exception = aggregate.Flatten().InnerException!;
        }

        return exception;
    }

    private static bool LooksLikeMegaStorageDnsFailure(Exception exception)
    {
        foreach (var current in Flatten(exception))
        {
            if (current is SocketException socketException &&
                socketException.SocketErrorCode is
                    SocketError.HostNotFound or
                    SocketError.TryAgain or
                    SocketError.NoData)
            {
                return true;
            }

            var message = current.Message;
            if (message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("mega.co.nz", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        var remaining = new Stack<Exception>();
        remaining.Push(exception);
        while (remaining.Count > 0)
        {
            var current = remaining.Pop();
            yield return current;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    remaining.Push(inner);
                }
            }
            else if (current.InnerException is not null)
            {
                remaining.Push(current.InnerException);
            }
        }
    }
}

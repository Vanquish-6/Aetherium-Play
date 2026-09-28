using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace AcLegacyLauncher;

internal sealed class SharedDatBusyException : IOException
{
    internal SharedDatBusyException(string message)
        : base(message)
    {
    }
}

internal sealed class SharedDatLaunchLease : IDisposable
{
    private readonly ManualResetEventSlim releaseRequested;
    private Thread? ownerThread;

    internal SharedDatLaunchLease(
        string mutexName,
        bool recoveredAbandonedOwner,
        ManualResetEventSlim releaseRequested,
        Thread ownerThread)
    {
        MutexName = mutexName;
        RecoveredAbandonedOwner = recoveredAbandonedOwner;
        this.releaseRequested = releaseRequested;
        this.ownerThread = ownerThread;
    }

    internal string MutexName { get; }

    internal bool RecoveredAbandonedOwner { get; }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref ownerThread, null);
        if (owner is null)
        {
            return;
        }

        releaseRequested.Set();
        if (!ReferenceEquals(Thread.CurrentThread, owner))
        {
            owner.Join();
        }

        releaseRequested.Dispose();
    }
}

/// <summary>
/// Serializes the login-time DAT writer for every launcher process using the
/// same game install. The old client keeps portal.dat/cell.dat shared between
/// account slots, so two simultaneous DDD handshakes can otherwise mutate the
/// same allocator/B-tree concurrently.
///
/// The lease is a machine-wide named Windows mutex keyed from the normalized
/// game-install path. A dedicated launcher thread owns the mutex because named
/// mutex release is thread-affine; the DDD monitor only signals that owner
/// thread after A10 proves CLCache and both native DAT writers are fully
/// drained. If the launcher is killed, Windows abandons the mutex and the next
/// launcher can become the updater without treating the interrupted DAT as
/// successfully completed.
/// </summary>
internal static class SharedDatLaunchGate
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private const string MutexPrefix = @"Global\AetheriumPlay.SharedDat.";

    internal static SharedDatLaunchLease Acquire(string installDirectory) =>
        AcquireCore(installDirectory, waitUntilAvailable: false, CancellationToken.None);

    internal static SharedDatLaunchLease AcquireWhenAvailable(
        string installDirectory,
        CancellationToken cancellationToken) =>
        AcquireCore(installDirectory, waitUntilAvailable: true, cancellationToken);

    private static SharedDatLaunchLease AcquireCore(
        string installDirectory,
        bool waitUntilAvailable,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var mutexName = GetMutexName(installDirectory);
        var acquiredSignal = new ManualResetEventSlim(false);
        var releaseRequested = new ManualResetEventSlim(false);
        Exception? acquisitionFailure = null;
        var acquired = false;
        var recoveredAbandonedOwner = false;

        var ownerThread = new Thread(() =>
        {
            Mutex? mutex = null;
            var ownsMutex = false;
            try
            {
                mutex = new Mutex(initiallyOwned: false, mutexName);
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        ownsMutex = mutex.WaitOne(
                            waitUntilAvailable ? PollInterval : TimeSpan.Zero);
                    }
                    catch (AbandonedMutexException)
                    {
                        // WaitOne transfers ownership to this thread when reporting
                        // an abandoned mutex. Treat that as crash recovery, never as
                        // proof that the interrupted client's DAT update completed.
                        ownsMutex = true;
                        recoveredAbandonedOwner = true;
                    }
                }
                while (waitUntilAvailable && !ownsMutex);

                if (ownsMutex && cancellationToken.IsCancellationRequested)
                {
                    mutex.ReleaseMutex();
                    ownsMutex = false;
                    cancellationToken.ThrowIfCancellationRequested();
                }

                acquired = ownsMutex;
            }
            catch (Exception error)
            {
                acquisitionFailure = error;
            }
            finally
            {
                acquiredSignal.Set();
            }

            if (!ownsMutex)
            {
                mutex?.Dispose();
                return;
            }

            try
            {
                releaseRequested.Wait();
            }
            finally
            {
                try
                {
                    mutex!.ReleaseMutex();
                }
                finally
                {
                    mutex!.Dispose();
                }
            }
        })
        {
            IsBackground = true,
            Name = "Aetherium shared DAT mutex owner",
        };

        ownerThread.Start();
        acquiredSignal.Wait();
        acquiredSignal.Dispose();

        if (acquisitionFailure is not null)
        {
            releaseRequested.Set();
            ownerThread.Join();
            releaseRequested.Dispose();
            if (acquisitionFailure is OperationCanceledException canceled)
            {
                throw canceled;
            }

            throw new InvalidOperationException(
                "Aetherium Play cannot secure the machine-wide shared-DAT update mutex. " +
                "The client was not started because launching without that mutex could corrupt portal.dat/cell.dat.",
                acquisitionFailure);
        }

        if (!acquired)
        {
            ownerThread.Join();
            releaseRequested.Dispose();
            throw new SharedDatBusyException(
                "Another Aetherium client is still finishing the shared portal.dat/cell.dat update. " +
                "This client will wait instead of starting a second DAT writer.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            releaseRequested.Set();
            ownerThread.Join();
            releaseRequested.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        return new SharedDatLaunchLease(
            mutexName,
            recoveredAbandonedOwner,
            releaseRequested,
            ownerThread);
    }

    internal static void ReleaseWhenClientDatSafe(
        SharedDatLaunchLease lease,
        Process process,
        NativeClientDddAccelerationInstallation installation,
        Action<string>? report,
        Action? onDatSafe = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(installation);

        if (lease.RecoveredAbandonedOwner)
        {
            report?.Invoke(
                "Recovered an abandoned shared-DAT updater; this launch owns the retry and will " +
                "run normal revision checking/repair before another client may start.");
        }

        var monitor = new Thread(() =>
        {
            var readFailureReported = false;
            try
            {
                while (true)
                {
                    process.Refresh();
                    if (process.HasExited)
                    {
                        report?.Invoke(
                            "Shared DAT update owner exited before/after completion; " +
                            "the next client will re-check/repair the DAT normally.");
                        return;
                    }

                    NativeDatDrainState state;
                    try
                    {
                        state = NativeClientDddAcceleration.ReadDatDrainState(
                            process.Handle,
                            installation);
                    }
                    catch (Exception error) when (
                        error is Win32Exception or InvalidOperationException)
                    {
                        if (!readFailureReported)
                        {
                            report?.Invoke(
                                "Shared DAT drain state is temporarily unreadable; keeping the " +
                                "second-client gate closed until the first client exits or drain state recovers.");
                            readFailureReported = true;
                        }

                        Thread.Sleep(PollInterval);
                        continue;
                    }

                    switch (state)
                    {
                        case NativeDatDrainState.Busy:
                            break;

                        case NativeDatDrainState.ReadyForPromotion:
                            // The A10 hook deliberately keeps OpeningUI
                            // incomplete and freezes CLCache consumption while
                            // the launcher promotes this slot's fully-drained DAT
                            // pair back to the install seed.
                            onDatSafe?.Invoke();
                            report?.Invoke(
                                "Private DAT update promoted to the shared seed; " +
                                "another account slot may now launch.");
                            return;

                        case NativeDatDrainState.PromotionComplete:
                            return;

                        default:
                            break;
                    }

                    Thread.Sleep(PollInterval);
                }
            }
            catch (Exception error)
            {
                // Fail closed while the first process is alive. If monitoring
                // itself breaks, retain the named mutex until that process exits.
                report?.Invoke(
                    "Shared DAT safety monitor failed; keeping the second-client gate closed " +
                    "until the first client exits. " + error.Message);
                try
                {
                    process.WaitForExit();
                }
                catch
                {
                    // Process disposal/exit races are safe: releasing after this
                    // point only lets the next login perform the idempotent repair.
                }
            }
            finally
            {
                lease.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = $"Aetherium shared DAT drain {process.Id}",
        };
        monitor.Start();
    }

    internal static void ReleaseWhenProcessExits(
        SharedDatLaunchLease lease,
        Process process,
        Action<string>? report = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(process);

        var monitor = new Thread(() =>
        {
            try
            {
                process.WaitForExit();
            }
            catch (Exception error)
            {
                report?.Invoke(
                    "Private DAT workspace lifetime monitor ended early: " + error.Message);
            }
            finally
            {
                lease.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = $"Aetherium private DAT lifetime {process.Id}",
        };
        monitor.Start();
    }

    internal static string GetMutexNameForTest(string installDirectory) =>
        GetMutexName(installDirectory);

    private static string GetMutexName(string installDirectory)
    {
        var normalized = NormalizeInstallPath(installDirectory);
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return MutexPrefix + Convert.ToHexString(keyBytes);
    }

    private static string NormalizeInstallPath(string installDirectory)
    {
        var fullPath = Path.GetFullPath(installDirectory);
        var trimmed = Path.TrimEndingDirectorySeparator(fullPath);

        // AC launcher paths are Windows paths and therefore case-insensitive.
        // Hash a stable uppercase representation so aliases that differ only by
        // casing or a trailing separator map to the same machine-wide mutex.
        return trimmed.ToUpperInvariant();
    }
}

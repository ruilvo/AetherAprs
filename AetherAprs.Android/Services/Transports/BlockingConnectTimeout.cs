// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services.Transports;

/// <summary>
/// Runs a blocking native connect (for example RFCOMM <c>BluetoothSocket.Connect</c>)
/// on the thread pool with a bounded, cancellable wait. Kept free of Android
/// types so the timeout and cancellation behaviour can be unit-tested.
/// </summary>
/// <remarks>
/// The blocking call itself cannot be cancelled, so the caller must release the
/// underlying handle when the wait ends. Without the bound, RFCOMM can block for
/// 10–30 seconds or indefinitely on a hung peer (audit P-19).
/// </remarks>
internal static class BlockingConnectTimeout
{
    /// <summary>
    /// Waits for <paramref name="connect"/> to finish, at most
    /// <paramref name="timeout"/>, and observes the blocking task if the wait
    /// ends early so a late native failure is not unobserved.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// The blocking call did not finish within <paramref name="timeout"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled while waiting.
    /// </exception>
    public static async Task RunAsync(
        Action connect,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        // The blocking native call cannot observe a token, so the wait below
        // owns cancellation and timeout; the caller releases the handle when
        // the wait ends. Passing the token to Task.Run would only prevent the
        // call from starting, not interrupt it.
        var connectTask = Task.Run(connect, CancellationToken.None);

        try
        {
            await connectTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            ObserveFailure(connectTask);
            throw new TimeoutException(
                $"The blocking connect did not complete within {timeout.TotalSeconds:0} seconds.",
                exception);
        }
        catch
        {
            ObserveFailure(connectTask);
            throw;
        }
    }

    /// <summary>
    /// Ensures a late native failure is observed instead of surfacing as an
    /// unobserved task exception after the caller has already moved on.
    /// </summary>
    private static void ObserveFailure(Task connectTask)
    {
        _ = connectTask.ContinueWith(
            static task =>
            {
                _ = task.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}

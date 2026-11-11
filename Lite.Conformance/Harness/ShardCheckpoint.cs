namespace Lite.Conformance.Harness;

/// <summary>Last-chance evidence writer for shard parents. The 11 November html5 baseline run
/// lost three whole shard queues because an unhandled exception on a Kestrel request thread
/// (an OOM escaping HttpProtocol.ProcessRequests) killed the parent before the queue-end
/// report write; reports only write at queue end. Registering the live run state here lets
/// the AppDomain's unhandled-exception notification save the outcomes gathered so far.
/// The checkpoint is recorded not-completed (identity verification is skipped while the
/// process dies), so it can never pass for finished evidence — it exists so a dead shard
/// loses minutes of work, not hours.</summary>
internal static class ShardCheckpoint
{
    private static (string Destination, EvidenceIdentity Identity, DateTime Started,
        IReadOnlyList<TestEvidence> Outcomes)? _active;

    /// <summary>Starts guarding <paramref name="outcomes"/>: every unhandled exception from
    /// now on writes the current outcome list to <paramref name="destination"/>.</summary>
    internal static void Activate(string destination, EvidenceIdentity identity, DateTime started,
        IReadOnlyList<TestEvidence> outcomes)
    {
        _active = (destination, identity, started, outcomes);
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
    }

    /// <summary>Stops guarding: the queue finished and the verifying final write supersedes
    /// any checkpoint.</summary>
    internal static void Deactivate()
    {
        _active = null;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
    }

    /// <summary>Writes the guarded outcomes right now — for the unwinding-exception path,
    /// where the finally that removes the last-chance handler runs before the AppDomain
    /// handler would fire. Safe under an OOM: every failure leaves the last periodic
    /// checkpoint on disk.</summary>
    internal static void WriteNow()
    {
        if (_active is not { } state) return;
        try
        {
            ExecutionEvidence.Write(state.Destination, state.Identity, state.Started, state.Outcomes,
                verifyIdentity: false);
            Console.Error.WriteLine($"[shard] checkpoint written to {state.Destination}");
        }
        catch { /* an OOM at write time leaves whatever the periodic checkpoints saved */ }
    }

    private static void OnUnhandled(object? sender, UnhandledExceptionEventArgs e)
    {
        if (_active is not { } state) return;
        try
        {
            var detail = e.ExceptionObject is Exception ex
                ? ex.GetType().Name + (ex.Message.Length > 0 ? $": {ex.Message}" : "")
                : "unknown exception object";
            var outcomes = new List<TestEvidence>(state.Outcomes)
            {
                new("wpt", "(harness)", "crash",
                    $"shard terminated by an unhandled {detail} before the queue finished", []),
            };
            ExecutionEvidence.Write(state.Destination, state.Identity, state.Started, outcomes,
                verifyIdentity: false);
            Console.Error.WriteLine($"[shard] last-chance checkpoint written to {state.Destination}");
        }
        catch
        {
            // An OOM at write time leaves whatever the periodic checkpoints saved on disk.
        }
    }
}

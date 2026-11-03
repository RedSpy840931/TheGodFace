// Version: 1 (zero-token background availability checker)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TheGodFace.Utils
{
    // Availability of one model, derived from the metadata endpoint response.
    public enum ModelStatus
    {
        // No probe has finished yet, routing must not trust this state.
        Unknown,
        // Metadata answered 200, so the model accepts requests.
        Available,
        // Rate limited or temporarily overloaded, the model exists but cannot take a prompt now.
        Overloaded,
        // Network failure, unknown model id or a rejected key.
        Offline
    }

    // Polls the model metadata endpoint in the background; these GET requests spend zero tokens.
    public sealed class ModelAvailabilityMonitor
    {
        // Time between full status sweeps, short enough to notice a recovered model quickly.
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(3);
        // Per-probe timeout, shorter than the interval so a hung probe never delays the next sweep.
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

        // Sends the metadata GET requests, it holds the API key and nothing else does.
        private readonly GoogleApiClient _client;
        // Priority order coming from GeminiService, index 0 is the preferred model.
        private readonly string[] _models;
        // Last known status per model, read from the UI thread and from the routing code.
        private readonly ConcurrentDictionary<string, ModelStatus> _states = new();
        // Cancels the polling loop when the window closes.
        private readonly CancellationTokenSource _cts = new();
        // Callers of WaitForAvailableAsync, released as soon as a model becomes usable.
        private readonly List<TaskCompletionSource<bool>> _waiters = new();
        // Guards _waiters, which is not a concurrent collection.
        private readonly object _waiterLock = new();
        // Aggregate flag written by the sweep loop and read by the face, hence volatile.
        private volatile bool _hasAvailableModel;
        // Background sweep loop, null until Start runs.
        private Task? _loopTask;

        // Stores the client and the priority list, every model begins as Unknown.
        public ModelAvailabilityMonitor(GoogleApiClient client, string[] models)
        {
            _client = client;
            _models = models;
            // Seeding the dictionary here keeps GetStatus lock free before the first sweep.
            foreach (string model in models) _states[model] = ModelStatus.Unknown;
        }

        // True when at least one model answered 200 during the last sweep.
        public bool HasAvailableModel => _hasAvailableModel;

        // Starts the sweep loop, repeated calls are ignored so start-up paths can both call it.
        public void Start()
        {
            if (_loopTask != null) return;
            _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
        }

        // Model for the next request: first available in priority order, otherwise the preferred one.
        public string GetActiveModel()
        {
            foreach (string model in _models)
                if (GetStatus(model) == ModelStatus.Available) return model;
            // Nothing is confirmed available, so the request goes to the preferred model anyway.
            return _models[0];
        }

        // Last known status of one model, Unknown when the id is not monitored.
        public ModelStatus GetStatus(string model) => _states.TryGetValue(model, out var status) ? status : ModelStatus.Unknown;

        // Number of models currently marked available, used for diagnostics.
        public int CountAvailable() => _models.Count(m => GetStatus(m) == ModelStatus.Available);

        // Snapshot of every status in priority order, for logs and the future status indicator.
        public IReadOnlyList<(string Model, ModelStatus Status)> Snapshot() => _models.Select(m => (m, GetStatus(m))).ToList();

        // Completes once any model is available, cancellation removes the pending waiter.
        public Task WaitForAvailableAsync(CancellationToken token)
        {
            if (_hasAvailableModel) return Task.CompletedTask;

            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_waiterLock) _waiters.Add(waiter);

            if (token.CanBeCanceled)
            {
                // Cancellation drops the waiter so a closed window leaves no dangling callback.
                var registration = token.Register(() =>
                {
                    lock (_waiterLock) _waiters.Remove(waiter);
                    waiter.TrySetCanceled(token);
                });
                // The registration lives only as long as the waiter itself.
                waiter.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
            }

            return waiter.Task;
        }

        // Stops the loop and cancels every pending waiter, called when the window closes.
        public void Stop()
        {
            _cts.Cancel();
            ReleaseWaiters(cancel: true);
        }

        // Runs sweeps back to back with CheckInterval pauses until the token fires.
        private async Task PollLoopAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(CheckInterval);
            try
            {
                // The first sweep runs immediately, so the splash already collects the status.
                do
                {
                    await SweepAsync(token);
                } while (await timer.WaitForNextTickAsync(token));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // A broken loop must not crash the app, it only stops refreshing statuses.
                Debug.WriteLine($"model availability loop stopped: {ex.Message}");
            }
        }

        // Probes every model in parallel, so one slow endpoint does not stretch the sweep.
        private Task SweepAsync(CancellationToken token)
        {
            return Task.WhenAll(_models.Select(model => ProbeAsync(model, token)));
        }

        // One probe: HTTP result becomes a status, a network failure counts as Offline.
        private async Task ProbeAsync(string model, CancellationToken token)
        {
            ModelStatus status;
            try
            {
                int code = await _client.ProbeModelAsync(model, ProbeTimeout, token);
                status = code switch
                {
                    // The metadata request succeeded, so the model id and the key are valid.
                    200 => ModelStatus.Available,
                    // The model exists but refuses work right now.
                    429 or 500 or 502 or 503 or 504 => ModelStatus.Overloaded,
                    // 0 for timeouts and network errors, other codes mean the id or the key is rejected.
                    _ => ModelStatus.Offline
                };
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // App shutdown, not a model problem.
                return;
            }
            catch
            {
                // Any unexpected probe failure is treated as unreachable.
                status = ModelStatus.Offline;
            }

            SetStatus(model, status);
        }

        // Stores one status and refreshes the aggregate flag that wakes the face up.
        private void SetStatus(string model, ModelStatus status)
        {
            _states[model] = status;
            _hasAvailableModel = _models.Any(m => GetStatus(m) == ModelStatus.Available);
            if (_hasAvailableModel) ReleaseWaiters(cancel: false);
        }

        // Completes every pending waiter, either as success or as cancellation.
        private void ReleaseWaiters(bool cancel)
        {
            List<TaskCompletionSource<bool>> pending;
            lock (_waiterLock)
            {
                if (_waiters.Count == 0) return;
                pending = new List<TaskCompletionSource<bool>>(_waiters);
                _waiters.Clear();
            }

            foreach (var waiter in pending)
            {
                if (cancel) waiter.TrySetCanceled();
                else waiter.TrySetResult(true);
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.Core
{
    /// <summary>
    /// Centralized FIFO task queue. Ensures only one Selenium operation runs at a time.
    /// </summary>
    public sealed class TaskQueue
    {
        private static readonly Lazy<TaskQueue> _instance = new(() => new TaskQueue());
        public static TaskQueue Instance => _instance.Value;

        private readonly SemaphoreSlim _singleWorkerSemaphore;
        private readonly ConcurrentQueue<(Func<Task> work, string? description)> _queue = new();
        private readonly ConcurrentDictionary<string, byte> _keysInFlight = new();
        private int _isWorkerRunning = 0;

        private TaskQueue()
        {
            _singleWorkerSemaphore = new SemaphoreSlim(1, 1);
        }

        public Task Enqueue(Func<Task> work, string? description = null)
        {
            if (work == null) return Task.CompletedTask;
            _queue.Enqueue((work, description));
            StartWorkerIfNeeded();
            return Task.CompletedTask;
        }

        public Task EnqueueOnce(string key, Func<Task> work, string? description = null)
        {
            if (string.IsNullOrWhiteSpace(key) || work == null) return Task.CompletedTask;
            if (!_keysInFlight.TryAdd(key, 0))
            {
                // duplicate task is already queued or running; silently ignore
                return Task.CompletedTask;
            }

            // Wrap work to ensure key cleanup when done
            async Task Wrapped()
            {
                try { await work().ConfigureAwait(false); }
                finally { _keysInFlight.TryRemove(key, out _); }
            }

            _queue.Enqueue((Wrapped, description));
            StartWorkerIfNeeded();
            return Task.CompletedTask;
        }

        private void StartWorkerIfNeeded()
        {
            if (Interlocked.CompareExchange(ref _isWorkerRunning, 1, 0) == 0)
            {
                _ = Task.Run(RunWorkerAsync);
            }
        }

        private async Task RunWorkerAsync()
        {
            try
            {
                while (_queue.TryDequeue(out var item))
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(item.description))
                            TerminalLogger.Instance.Log($"[Queue] Start: {item.description}");
                        await item.work().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        TerminalLogger.Instance.LogError("[Queue] Ошибка выполнения задачи", ex);
                    }
                    finally
                    {
                        if (!string.IsNullOrWhiteSpace(item.description))
                            TerminalLogger.Instance.Log($"[Queue] Done: {item.description}");
                    }
                }
            }
            finally
            {
                // Mark worker as stopped; if items arrived concurrently, restart.
                Interlocked.Exchange(ref _isWorkerRunning, 0);
                if (!_queue.IsEmpty)
                {
                    StartWorkerIfNeeded();
                }
            }
        }
    }
}



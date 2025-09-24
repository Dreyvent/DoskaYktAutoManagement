using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.Core
{
    /// <summary>
    /// Centralized FIFO task queue. Limited parallelism for browser operations.
    /// </summary>
    public sealed class TaskQueue
    {
        private static readonly Lazy<TaskQueue> _instance = new(() => new TaskQueue());
        public static TaskQueue Instance => _instance.Value;

        private readonly ConcurrentQueue<(Func<Task> work, string? description)> _queue = new();
        private readonly ConcurrentDictionary<string, byte> _keysInFlight = new();
        private int _activeWorkers = 0;
        private int _maxConcurrency = 1;

        private TaskQueue()
        {
            // Read max concurrency from settings, clamp 1..3 (безопасный диапазон)
            try
            {
                _maxConcurrency = Math.Clamp(DoskaYkt_AutoManagement.Properties.Settings.Default.TaskQueueMaxConcurrency, 1, 3);
            }
            catch { _maxConcurrency = 1; }
        }

        public Task Enqueue(Func<Task> work, string? description = null)
        {
            if (work == null) return Task.CompletedTask;
            _queue.Enqueue((work, description));
            StartWorkersIfNeeded();
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
            StartWorkersIfNeeded();
            return Task.CompletedTask;
        }

        private void StartWorkersIfNeeded()
        {
            while (_activeWorkers < _maxConcurrency)
            {
                var started = Interlocked.Increment(ref _activeWorkers);
                if (started <= _maxConcurrency)
                {
                    _ = Task.Run(RunWorkerAsync);
                }
                else
                {
                    Interlocked.Decrement(ref _activeWorkers);
                    break;
                }
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
                Interlocked.Decrement(ref _activeWorkers);
                if (!_queue.IsEmpty) StartWorkersIfNeeded();
            }
        }

        public void SetMaxConcurrency(int value)
        {
            var clamped = Math.Clamp(value, 1, 3);
            _maxConcurrency = clamped;
            // Если уменьшили — лишние воркеры сами завершатся после задач
            // Если увеличили — можно запустить дополнительные
            if (!_queue.IsEmpty) StartWorkersIfNeeded();
        }
    }
}



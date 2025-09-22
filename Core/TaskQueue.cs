using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.Core
{
    /// <summary>
    /// Centralized task queue to serialize Selenium operations and cap concurrency.
    /// </summary>
    public sealed class TaskQueue
    {
        private static readonly Lazy<TaskQueue> _instance = new(() => new TaskQueue());
        public static TaskQueue Instance => _instance.Value;

        private readonly SemaphoreSlim _parallelLimiter;

        private int _maxParallel;
        public int MaxParallel
        {
            get => _maxParallel;
            set
            {
                if (value < 1) value = 1;
                if (value == _maxParallel) return;
                // Recreate limiter to apply new capacity
                var newLimiter = new SemaphoreSlim(value, value);
                // Drain old permits (best-effort)
                _parallelLimiter.Dispose();
                _maxParallel = value;
                // Note: this is a simplistic reset; callers should set before active use
            }
        }

        private TaskQueue()
        {
            _maxParallel = 1; // default to single browser/session at a time
            _parallelLimiter = new SemaphoreSlim(_maxParallel, _maxParallel);
        }

        public async Task Enqueue(Func<Task> work, string? description = null)
        {
            if (work == null) return;
            await _parallelLimiter.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!string.IsNullOrWhiteSpace(description))
                    TerminalLogger.Instance.Log($"[Queue] Start: {description}");
                await work().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TerminalLogger.Instance.LogError("[Queue] Ошибка выполнения задачи", ex);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(description))
                    TerminalLogger.Instance.Log($"[Queue] Done: {description}");
                _parallelLimiter.Release();
            }
        }
    }
}



using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Channels;
using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public sealed class DatabaseWorker : IDatabaseWorker
    {
        private const int Capacity = 1024;
        private static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(500);

        private readonly Channel<DbWorkItem> _channel = Channel.CreateBounded<DbWorkItem>(
            new BoundedChannelOptions(Capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        private readonly CancellationTokenSource _cts = new();
        private readonly ILogger<DatabaseWorker> _logger;
        private Task _worker = Task.CompletedTask;
        private int _started;

        public DatabaseWorker(ILogger<DatabaseWorker> logger)
        {
            _logger = logger;
        }

        public Task Completion => _worker;

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
                return;

            _worker = Task.Run(ProcessAsync);
        }

        public bool TryEnqueue(DbWorkItem item) => _channel.Writer.TryWrite(item);

        public bool Shutdown(TimeSpan drainTimeout, TimeSpan cancelGrace)
        {
            _channel.Writer.TryComplete();

            if (Volatile.Read(ref _started) == 0)
                return true;

            if (WaitForWorker(drainTimeout))
                return true;

            _logger.LogWarning("Database worker did not drain within {timeout}, requesting cancellation.", drainTimeout);
            _cts.Cancel();

            return WaitForWorker(cancelGrace);
        }

        private bool WaitForWorker(TimeSpan timeout)
        {
            try
            {
                return _worker.Wait(timeout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database worker terminated with an exception.");
                return _worker.IsCompleted;
            }
        }

        private async Task ProcessAsync()
        {
            var dropped = 0;

            try
            {
                await foreach (var item in _channel.Reader.ReadAllAsync())
                {
                    if (_cts.IsCancellationRequested)
                    {
                        dropped++;
                        continue;
                    }

                    await ExecuteAsync(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database worker loop terminated unexpectedly.");
            }

            if (dropped > 0)
                _logger.LogInformation("Database worker dropped {count} pending work item(s) after cancellation.", dropped);
        }

        private async Task ExecuteAsync(DbWorkItem item)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await item.Work(_cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                _logger.LogInformation("Database work cancelled: {name}, SteamID: {steamId}", item.Name, item.SteamId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database work failed: {name}, SteamID: {steamId}", item.Name, item.SteamId);
            }

            if (stopwatch.Elapsed > SlowThreshold)
            {
                _logger.LogWarning("Slow database work: {name}, SteamID: {steamId}, {elapsed} ms",
                    item.Name, item.SteamId, stopwatch.ElapsedMilliseconds);
            }
        }
    }
}

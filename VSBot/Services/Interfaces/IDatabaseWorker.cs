namespace VSBot.Services.Interfaces
{
    public sealed record DbWorkItem(string Name, ulong? SteamId, Func<CancellationToken, Task> Work);

    public interface IDatabaseWorker
    {
        void Start();
        bool TryEnqueue(DbWorkItem item);
        Task Completion { get; }
        bool Shutdown(TimeSpan drainTimeout, TimeSpan cancelGrace);
    }
}

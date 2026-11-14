using Microsoft.EntityFrameworkCore;
using VSBot.Domain.Entities;
using VSBot.Domain.Repositories.Interfaces;
using VSBot.Infrastructure;

namespace VSBot.Domain.Repositories
{
    public class PlayerRepository : IPlayerRepository
    {
        private readonly IDbContextFactory<VSBotDbContext> _factory;

        public PlayerRepository(IDbContextFactory<VSBotDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<Player?> GetByKeyAsync(ulong kay, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);

            return await dbContext.Players
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.SteamId == kay, cancellationToken);
        }

        public async Task AddAsync(Player entity, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);

            dbContext.Players.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task AddRangeAsync(IEnumerable<Player> entities, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);

            dbContext.Players.AddRange(entities);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateAsync(Player entity, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);

            dbContext.Players.Update(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateRangeAsync(IEnumerable<Player> entities, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);

            dbContext.Players.UpdateRange(entities);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(Player entity, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);

            dbContext.Players.Remove(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteRangeAsync(IEnumerable<Player> entities, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);

            dbContext.Players.RemoveRange(entities);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

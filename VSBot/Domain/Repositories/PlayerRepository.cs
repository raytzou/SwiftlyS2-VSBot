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

        public Player? GetByKey(ulong kay)
        {
            using var dbContext = _factory.CreateDbContext();

            return dbContext.Players
                .AsNoTracking()
                .FirstOrDefault(f => f.SteamId == kay);
        }

        public void Add(Player entity)
        {
            using var dbContext = _factory.CreateDbContext();

            dbContext.Players.Add(entity);
            dbContext.SaveChanges();
        }

        public void AddRange(IEnumerable<Player> entities)
        {
            using var dbContext = _factory.CreateDbContext();

            dbContext.Players.AddRange(entities);
            dbContext.SaveChanges();
        }

        public void Update(Player entity)
        {
            using var dbContext = _factory.CreateDbContext();

            dbContext.Players.Update(entity);
            dbContext.SaveChanges();
        }

        public void UpdateRange(IEnumerable<Player> entities)
        {
            using var dbContext = _factory.CreateDbContext();

            dbContext.Players.UpdateRange(entities);
            dbContext.SaveChanges();
        }

        public void Delete(Player entity)
        {
            using var dbContext = _factory.CreateDbContext();

            dbContext.Players.Remove(entity);
            dbContext.SaveChanges();
        }

        public void DeleteRange(IEnumerable<Player> entities)
        {
            using var dbContext = _factory.CreateDbContext();

            dbContext.Players.RemoveRange(entities);
            dbContext.SaveChanges();
        }
    }
}

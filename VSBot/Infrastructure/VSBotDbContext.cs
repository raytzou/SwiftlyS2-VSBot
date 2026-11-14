using Microsoft.EntityFrameworkCore;
using VSBot.Domain.Entities;

namespace VSBot.Infrastructure
{
    public class VSBotDbContext : DbContext
    {
        public VSBotDbContext(DbContextOptions<VSBotDbContext> options) : base(options) { }


        public DbSet<Player> Players => Set<Player>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Player>()
                .Property(p => p.SteamId)
                .HasConversion<long>();
        }
    }
}

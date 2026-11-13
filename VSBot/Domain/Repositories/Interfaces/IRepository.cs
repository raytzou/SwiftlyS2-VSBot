namespace VSBot.Domain.Repositories.Interfaces
{
    public interface IRepository<TEntity, TKey> where TEntity : class where TKey : notnull
    {
        TEntity? GetByKey(TKey id);
        void Add(TEntity entity);
        void AddRange(IEnumerable<TEntity> entities);
        void Update(TEntity entity);
        void UpdateRange(IEnumerable<TEntity> entities);
        void Delete(TEntity entity);
        void DeleteRange(IEnumerable<TEntity> entities);
    }
}

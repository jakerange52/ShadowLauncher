namespace ShadowLauncher.Core.Interfaces;

// TODO: Re-evaluate this interface. Only one implementation exists per T,
// and the Func<T,bool> predicate forces in-memory evaluation. Consider deleting
// the interface and depending on the concrete repository directly.
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(string id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<IEnumerable<T>> FindAsync(Func<T, bool> predicate);
    Task<T> AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(string id);
}

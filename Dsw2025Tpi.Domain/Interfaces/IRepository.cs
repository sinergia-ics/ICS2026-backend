using Dsw2025Tpi.Domain.Entities;
using System.Linq.Expressions;

namespace Dsw2025Tpi.Domain.Interfaces;

public interface IRepository
{
    /// <summary>Ejecuta una operación completa en una transacción de persistencia.</summary>
    /// <param name="operation">Operación que se confirma sólo cuando finaliza correctamente.</param>
    /// <returns>Una tarea que representa la confirmación o reversión de la operación.</returns>
    Task ExecuteInTransaction(Func<Task> operation);
    Task<T?> GetById<T>(Guid id, params string[] include) where T : EntityBase;
    Task<IEnumerable<T>?> GetAll<T>(params string[] include) where T : EntityBase;
    Task<T?> First<T>(Expression<Func<T, bool>> predicate, params string[] include) where T : EntityBase;
    Task<IEnumerable<T>?> GetFiltered<T>(Expression<Func<T, bool>> predicate, params string[] include) where T : EntityBase;
    Task<T> Add<T>(T entity) where T : EntityBase;
    Task<T> Update<T>(T entity) where T : EntityBase;
    Task<T> Delete<T>(T entity) where T : EntityBase;
}

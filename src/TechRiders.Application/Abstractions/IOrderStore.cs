using TechRiders.Domain;

namespace TechRiders.Application;

public interface IOrderSession
{
    IReadOnlyList<Order> All();
    Order? Find(string id);
    void Save(Order order);
    void Delete(string id);
    StoredCancellation? FindCancellation(string key);
    void SaveCancellation(string key, StoredCancellation value);
}

public interface IOrderStore
{
    T InTransaction<T>(Func<IOrderSession, T> action);
}

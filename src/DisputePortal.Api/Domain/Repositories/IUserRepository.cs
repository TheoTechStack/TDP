namespace DisputePortal.Api.Domain.Repositories;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);

    Task<User?> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

using DisputePortal.Api.Domain;
using DisputePortal.Api.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct)
    {
        var normalised = email.Trim().ToLowerInvariant();
        return db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == normalised, ct);
    }

    public Task<User?> GetAsync(Guid id, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }
}

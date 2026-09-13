using Backend_API.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Repositories;

public sealed record UserSummary(int Id, string DisplayName);

public interface IUserRepository
{
    Task<List<UserSummary>> GetAllAsync(CancellationToken cancellationToken);
    Task<UserSummary?> FindAsync(int userId, CancellationToken cancellationToken);
}

public sealed class UserRepository(VehicleDbContext dbContext) : IUserRepository
{
    public Task<List<UserSummary>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Select(user => new UserSummary(user.Id, user.DisplayName))
            .ToListAsync(cancellationToken);

    public Task<UserSummary?> FindAsync(int userId, CancellationToken cancellationToken) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserSummary(user.Id, user.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);
}

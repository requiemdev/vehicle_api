using Backend_API.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Repositories;

public sealed record VehicleSummary(string DeviceId, string DisplayName);

public interface IVehicleRepository
{
    Task<List<VehicleSummary>> GetByOwnerAsync(
        int ownerId,
        CancellationToken cancellationToken);

    Task<bool> IsOwnedByAsync(
        string deviceId,
        int ownerId,
        CancellationToken cancellationToken);
}

public sealed class VehicleRepository(VehicleDbContext dbContext) : IVehicleRepository
{
    public Task<List<VehicleSummary>> GetByOwnerAsync(
        int ownerId,
        CancellationToken cancellationToken) =>
        dbContext.Vehicles
            .AsNoTracking()
            .Where(vehicle => vehicle.OwnerId == ownerId)
            .OrderBy(vehicle => vehicle.DisplayName)
            .ThenBy(vehicle => vehicle.DeviceId)
            .Select(vehicle => new VehicleSummary(vehicle.DeviceId, vehicle.DisplayName))
            .ToListAsync(cancellationToken);

    public Task<bool> IsOwnedByAsync(
        string deviceId,
        int ownerId,
        CancellationToken cancellationToken) =>
        dbContext.Vehicles
            .AsNoTracking()
            .AnyAsync(
                vehicle => vehicle.DeviceId == deviceId && vehicle.OwnerId == ownerId,
                cancellationToken);
}

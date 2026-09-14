using Backend_API.Model;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Data;

public class VehicleDbContext : DbContext
{
    public VehicleDbContext(DbContextOptions<VehicleDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Vehicle> Vehicles { get; set; } = null!;

}

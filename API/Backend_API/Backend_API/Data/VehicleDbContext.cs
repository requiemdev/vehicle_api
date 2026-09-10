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

    // define Vehicle relations
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Vehicle>()
            .HasOne(vehicle => vehicle.Owner)
            .WithMany(user => user.Vehicles)
            .HasForeignKey(vehicle => vehicle.OwnerId)
            .HasPrincipalKey(user => user.Id);
    }
}

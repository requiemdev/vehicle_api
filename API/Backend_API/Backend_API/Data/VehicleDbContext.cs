using Microsoft.EntityFrameworkCore;
using System;
using Backend_API.Model;

namespace Backend_API.Data;
public class VehicleDbContext:DbContext
{
	
	public VehicleDbContext(DbContextOptions<VehicleDbContext> options) : base(options)
	{
	}

	public DbSet<User> Users;
	public DbSet<Vehicle> Vehicles;
}

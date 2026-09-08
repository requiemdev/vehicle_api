using System;
using System.ComponentModel.DataAnnotations;


namespace Backend_API.Model;

public class Vehicle
{
    [Key]
    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Guid OwenerId { get; set; }
    public User Owner { get; set; } = null!;
}

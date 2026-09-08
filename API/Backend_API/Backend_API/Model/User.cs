using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Backend_API.Model;
public class User
{

	[Key]
		public int Id { get; set; }
		public string DisplayName { get; set; } = string.Empty;
	    public ICollection<Vehicle> Vehicles { get; set; } = [];


}

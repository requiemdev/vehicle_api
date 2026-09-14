using System;
using System.ComponentModel.DataAnnotations;

namespace Backend_API.Model;
public class User
{

	[Key]
		public int Id { get; set; }
		public string DisplayName { get; set; } = string.Empty;

}

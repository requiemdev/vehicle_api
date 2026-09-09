using Backend_API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("users")]
public sealed class UsersController(VehicleDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var users = await dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Select(user => new
            {
                user.Id,
                user.DisplayName
            })
            .ToListAsync(cancellationToken);

        return Ok(users);
    }
}

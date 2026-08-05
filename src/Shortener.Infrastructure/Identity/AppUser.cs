using Microsoft.AspNetCore.Identity;

namespace Shortener.Infrastructure.Identity;

public class AppUser : IdentityUser
{
    public required string FullName { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

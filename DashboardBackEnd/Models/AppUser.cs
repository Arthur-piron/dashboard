using Microsoft.AspNetCore.Identity;

namespace DashboardBackEnd.Models;

// IdentityUser already brings Id, UserName, Email, EmailConfirmed, PasswordHash, lockout, etc.
// Add here only what is specific to this application.
public class AppUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

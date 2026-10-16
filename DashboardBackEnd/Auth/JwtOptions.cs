using System.ComponentModel.DataAnnotations;

namespace DashboardBackEnd.Auth;

// Bound from the "Jwt" configuration section (appsettings or Jwt__* env vars).
public class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";
    [Required, MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters (256 bits) for HS256.")]
    public string Key { get; set; } = "";
    public int LifetimeMinutes { get; set; } = 60;
}

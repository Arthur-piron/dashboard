namespace DashboardBackEnd.Models;

public class ServiceSubscription
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";
    public AppUser? User { get; set; }

    public string ServiceKey { get; set; } = "";

    public string ProtectedCredentials { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }
}

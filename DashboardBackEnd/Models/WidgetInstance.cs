namespace DashboardBackEnd.Models;

public class WidgetInstance
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";
    public AppUser? User { get; set; }

    public string WidgetKey { get; set; } = "";

    public string ConfigJson { get; set; } = "{}";

    public int RefreshRateSeconds { get; set; }

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

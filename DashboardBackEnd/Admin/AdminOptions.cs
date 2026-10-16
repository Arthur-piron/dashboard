namespace DashboardBackEnd.Admin;

// Not validated on start: a clone with no administrator configured must still boot.
public class AdminOptions
{
    public const string SectionName = "Admin";

    public string? Email { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Email);

    public bool Matches(string? email) =>
        IsConfigured && string.Equals(Email, email, StringComparison.OrdinalIgnoreCase);
}

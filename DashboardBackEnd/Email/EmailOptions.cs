using System.ComponentModel.DataAnnotations;

namespace DashboardBackEnd.Email;

// Bound from the "Email" configuration section. Mailpit needs no credentials or TLS.
public class EmailOptions
{
    public const string SectionName = "Email";

    [Required] public string Host { get; set; } = "";
    public int Port { get; set; } = 1025;
    [Required] public string From { get; set; } = "";
}

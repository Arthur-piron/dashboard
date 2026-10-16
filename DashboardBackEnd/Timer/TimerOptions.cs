using System.ComponentModel.DataAnnotations;

namespace DashboardBackEnd.Timer;

public class TimerOptions
{
    public const string SectionName = "Timer";

    // the same floor as the fastest schedule, so clicking refresh cannot call a provider more often
    [Range(0, 3600)]
    public int ManualRefreshCooldownSeconds { get; set; } = 10;
}

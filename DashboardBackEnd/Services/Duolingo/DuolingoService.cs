using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Duolingo;

public class DuolingoService : IService
{
    public const string ServiceKey = "duolingo";

    public string Key => ServiceKey;
    public string Name => "Duolingo";
    public string Description => "Public learning profile of any Duolingo user (no account needed).";
    public AuthKind Auth => AuthKind.None;
}

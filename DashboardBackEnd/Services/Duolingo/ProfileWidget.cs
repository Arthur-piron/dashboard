using System.Text.Json;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Duolingo;

public class ProfileWidget : IWidget
{
    private readonly DuolingoClient _duolingo;

    public ProfileWidget(DuolingoClient duolingo)
    {
        _duolingo = duolingo;
    }

    public string Key => "duolingo.profile";
    public string ServiceKey => DuolingoService.ServiceKey;
    public string Name => "Learning profile";
    public string Description => "Streak, total XP and the language a Duolingo user is learning.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("username", ParamType.String, Description: "Public Duolingo username, e.g. test"),
    ];

    public async Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct)
    {
        var username = config.GetProperty("username").GetString()!;

        var profile = await _duolingo.FetchProfileAsync(username, ct)
            ?? throw new WidgetDataException($"No public Duolingo profile for '{username}'.");

        return new
        {
            profile.Username,
            profile.Name,
            profile.Streak,
            profile.TotalXp,
            profile.LearningLanguage,
            profile.FromLanguage,
            CourseCount = profile.Courses.Count,
        };
    }
}

using System.Text.Json;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Duolingo;

public class CourseWidget : IWidget
{
    private readonly DuolingoClient _duolingo;

    public CourseWidget(DuolingoClient duolingo)
    {
        _duolingo = duolingo;
    }

    public string Key => "duolingo.course";
    public string ServiceKey => DuolingoService.ServiceKey;
    public string Name => "Course progress";
    public string Description => "XP and crowns for one course of a Duolingo user.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("username", ParamType.String, Description: "Public Duolingo username, e.g. test"),
        new("language", ParamType.String, Required: false,
            Description: "Language code of the course, e.g. es. Defaults to the one currently being learnt."),
    ];

    public async Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct)
    {
        var username = config.GetProperty("username").GetString()!;
        var language = config.TryGetProperty("language", out var l) ? l.GetString() : null;

        var profile = await _duolingo.FetchProfileAsync(username, ct)
            ?? throw new WidgetDataException($"No public Duolingo profile for '{username}'.");

        language ??= profile.LearningLanguage;

        var course = profile.Courses.FirstOrDefault(c =>
            string.Equals(c.LearningLanguage, language, StringComparison.OrdinalIgnoreCase))
            ?? throw new WidgetDataException(
                profile.Courses.Count == 0
                    ? $"'{username}' has no public course."
                    : $"'{username}' is not learning '{language}'. Available: {string.Join(", ", profile.Courses.Select(c => c.LearningLanguage))}.");

        return new
        {
            profile.Username,
            course.Title,
            course.LearningLanguage,
            FromLanguage = profile.FromLanguage,
            course.Xp,
            course.Crowns,
            IsCurrentCourse = string.Equals(course.LearningLanguage, profile.LearningLanguage, StringComparison.OrdinalIgnoreCase),
        };
    }
}

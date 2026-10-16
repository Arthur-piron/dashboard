using System.Text.Json;

namespace DashboardBackEnd.Services.ApiClients;

public class DuolingoClient
{
    public const string HttpClientName = "duolingo";

    private readonly IHttpClientFactory _http;

    public DuolingoClient(IHttpClientFactory http)
    {
        _http = http;
    }

    public record DuolingoCourse(string Id, string Title, string LearningLanguage, int Xp, int Crowns);

    public record DuolingoProfile(
        int Id,
        string Username,
        string? Name,
        int Streak,
        int TotalXp,
        string LearningLanguage,
        string FromLanguage,
        IReadOnlyList<DuolingoCourse> Courses);

    public async Task<DuolingoProfile?> FetchProfileAsync(string username, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);
        var url = $"https://www.duolingo.com/2017-06-30/users?username={Uri.EscapeDataString(username)}";

        using var doc = await client.GetFromJsonDocumentAsync(url, ct);

        if (!doc.RootElement.TryGetProperty("users", out var users) || users.GetArrayLength() == 0)
        {
            return null;
        }

        var user = users[0];

        return new DuolingoProfile(
            Id: GetInt(user, "id"),
            Username: GetString(user, "username") ?? username,
            Name: GetString(user, "name"),
            Streak: GetInt(user, "streak"),
            TotalXp: GetInt(user, "totalXp"),
            LearningLanguage: GetString(user, "learningLanguage") ?? "",
            FromLanguage: GetString(user, "fromLanguage") ?? "",
            Courses: ReadCourses(user));
    }

    private static IReadOnlyList<DuolingoCourse> ReadCourses(JsonElement user)
    {
        if (!user.TryGetProperty("courses", out var courses) || courses.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return courses.EnumerateArray()
            .Select(c => new DuolingoCourse(
                Id: GetString(c, "id") ?? "",
                Title: GetString(c, "title") ?? "",
                LearningLanguage: GetString(c, "learningLanguage") ?? "",
                Xp: GetInt(c, "xp"),
                Crowns: GetInt(c, "crowns")))
            .ToList();
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
}

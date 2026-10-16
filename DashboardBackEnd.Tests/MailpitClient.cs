using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace DashboardBackEnd.Tests;

// Reads emails back from Mailpit's HTTP API (http://localhost:8025 locally, http://mailpit:8025 in compose).
public class MailpitClient
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("MAILPIT_URL") ?? "http://localhost:8025"),
    };

    public async Task<string> WaitForLinkAsync(string recipient, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (DateTime.UtcNow < deadline)
        {
            var search = await _http.GetFromJsonAsync<SearchResult>(
                $"/api/v1/search?query={Uri.EscapeDataString($"to:{recipient}")}");

            var id = search?.Messages.FirstOrDefault()?.ID;
            if (id is not null)
            {
                var message = await _http.GetFromJsonAsync<Message>($"/api/v1/message/{id}");
                var match = Regex.Match(message!.HTML, "href=\"([^\"]+)\"");
                if (match.Success) return match.Groups[1].Value;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"No email with a link received for {recipient}.");
    }

    private record SearchResult(List<MessageSummary> Messages);
    private record MessageSummary(string ID);
    private record Message(string HTML);
}

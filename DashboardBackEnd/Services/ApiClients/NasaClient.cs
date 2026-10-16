using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.ApiClients;

public partial class NasaClient
{
    public const string HttpClientName = "nasa";

    private readonly IHttpClientFactory _http;

    public NasaClient(IHttpClientFactory http)
    {
        _http = http;
    }

    public record ApodPicture(
        string Date,
        string Title,
        string Explanation,
        string MediaType,
        string Url,
        string? HdUrl,
        string? Copyright);

    public record MarsPhoto(int Id, string Camera, string ImageUrl);

    public record MarsRoverPhotos(
        string Rover,
        string Status,
        string EarthDate,
        int Sol,
        IReadOnlyList<MarsPhoto> Photos);

    // only 401/403 means a bad key: a 429 or a 5xx says nothing about it, so it is reported as such
    public async Task<bool> IsValidKeyAsync(string apiKey, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);
        using var response = await client.GetAsync(Url("planetary/apod", apiKey), ct);

        if (response.IsSuccessStatusCode) return true;

        return response.StatusCode switch
        {
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => false,
            HttpStatusCode.TooManyRequests => throw new WidgetDataException(
                "NASA's rate limit for this key is used up right now. Try again later."),
            _ => throw new WidgetDataException(
                $"NASA could not check this key (it answered {(int)response.StatusCode}). Try again in a moment."),
        };
    }

    public async Task<ApodPicture> ApodAsync(string apiKey, string? date, CancellationToken ct)
    {
        var url = Url("planetary/apod", apiKey);
        if (!string.IsNullOrWhiteSpace(date)) url += $"&date={Uri.EscapeDataString(date)}";

        using var doc = await GetAsync(url, ct);
        var picture = doc.RootElement;

        var result = new ApodPicture(
            Date: GetString(picture, "date") ?? date ?? "",
            Title: GetString(picture, "title") ?? "",
            Explanation: GetString(picture, "explanation") ?? "",
            MediaType: GetString(picture, "media_type") ?? "image",
            Url: GetString(picture, "url") ?? "",
            HdUrl: GetString(picture, "hdurl"),
            Copyright: GetString(picture, "copyright"));

        if (!IsPlaceholder(result)) return result;

        // apod moved to science.nasa.gov and the api now answers with the site logo, the real entry lives there
        return await ApodFromScienceSiteAsync(result, ct) ?? result;
    }

    private static bool IsPlaceholder(ApodPicture picture) =>
        picture.Url.Contains("nasa-logo", StringComparison.OrdinalIgnoreCase);

    // the wordpress api behind science.nasa.gov/apod, one image-article per day since 1995
    private async Task<ApodPicture?> ApodFromScienceSiteAsync(ApodPicture fallback, CancellationToken ct)
    {
        if (!DateOnly.TryParse(fallback.Date, out var day)) return null;

        // old entries are posted at midnight and "after" is exclusive, so only "before" is used
        var url = $"{ScienceSiteUrl}/wp-json/wp/v2/image-article?categories={ApodCategory}&per_page=1"
            + $"&before={day:yyyy-MM-dd}T23:59:59&_fields=date,title,content";

        try
        {
            var client = _http.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return null;

            var post = doc.RootElement[0];
            if (GetString(post, "date")?.StartsWith($"{day:yyyy-MM-dd}") != true) return null;

            var content = Rendered(post, "content");
            var image = ApodImage().Match(content);
            if (!image.Success) return null;

            var src = WebUtility.HtmlDecode(image.Groups[1].Value);
            var description = ApodDescription().Match(content);

            return fallback with
            {
                Title = ApodTitlePrefix().Replace(WebUtility.HtmlDecode(Rendered(post, "title")), ""),
                Explanation = description.Success ? PlainText(description.Groups[1].Value) : fallback.Explanation,
                MediaType = "image",
                Url = src,
                HdUrl = src.Split('?')[0],
                // the api's credit belongs to the placeholder, not to this picture
                Copyright = null,
            };
        }
        // the fallback is best effort: if the site fails too, the api's answer is shown as is
        catch (Exception ex) when ((ex is HttpRequestException or JsonException or TaskCanceledException)
            && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string Rendered(JsonElement post, string name) =>
        post.TryGetProperty(name, out var field) ? GetString(field, "rendered") ?? "" : "";

    // tags out, entities decoded, whitespace collapsed, the "Explanation:" label dropped
    private static string PlainText(string html)
    {
        var text = WebUtility.HtmlDecode(HtmlTag().Replace(html, ""));
        text = Whitespace().Replace(text, " ").Trim();
        return text.StartsWith("Explanation:") ? text["Explanation:".Length..].TrimStart() : text;
    }

    private const string ScienceSiteUrl = "https://science.nasa.gov";
    private const int ApodCategory = 22766;

    [GeneratedRegex(@"<img[^>]*\ssrc=""([^""]*/apod/[^""]*)""")]
    private static partial Regex ApodImage();

    [GeneratedRegex("""<p class="[^"]*media-detail-hero__description[^"]*">(.*?)</p>""", RegexOptions.Singleline)]
    private static partial Regex ApodDescription();

    // "APOD: 2026 October 5 – M104: The Sombrero Galaxy" -> "M104: The Sombrero Galaxy"
    [GeneratedRegex(@"^APOD:\s*\d{4}\s+\w+\s+\d{1,2}\s*[–-]\s*")]
    private static partial Regex ApodTitlePrefix();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public async Task<MarsRoverPhotos> LatestMarsPhotosAsync(string apiKey, string rover, int count, CancellationToken ct)
    {
        using var doc = await GetAsync(Url($"mars-photos/api/v1/rovers/{rover}/latest_photos", apiKey), ct);

        if (!doc.RootElement.TryGetProperty("latest_photos", out var photos) || photos.GetArrayLength() == 0)
        {
            throw new WidgetDataException($"NASA has no published photo for '{rover}' right now.");
        }

        var first = photos[0];
        var roverInfo = first.GetProperty("rover");

        return new MarsRoverPhotos(
            Rover: GetString(roverInfo, "name") ?? rover,
            Status: GetString(roverInfo, "status") ?? "",
            EarthDate: GetString(first, "earth_date") ?? "",
            Sol: GetInt(first, "sol"),
            Photos: photos.EnumerateArray()
                .Take(count)
                .Select(p => new MarsPhoto(
                    Id: GetInt(p, "id"),
                    Camera: p.TryGetProperty("camera", out var camera)
                        ? GetString(camera, "full_name") ?? GetString(camera, "name") ?? ""
                        : "",
                    ImageUrl: GetString(p, "img_src") ?? ""))
                .ToList());
    }

    // NASA answers a request a user can fix — or wait out — with a 4xx and a message of its own.
    // Those become WidgetDataException so both widgets surface the same wording; anything else is ours.
    private async Task<JsonDocument> GetAsync(string url, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => new WidgetDataException(
                    "NASA's hourly rate limit for this key is used up. Wait an hour, or subscribe again with your own key."),
                HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => new WidgetDataException(
                    "NASA rejected this API key. Subscribe again with a valid one."),
                HttpStatusCode.BadRequest or HttpStatusCode.NotFound => new WidgetDataException(
                    await RefusalMessageAsync(response, ct)),
                _ => new HttpRequestException($"NASA returned {(int)response.StatusCode}."),
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static async Task<string> RefusalMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (GetString(doc.RootElement, "msg") is { Length: > 0 } msg) return msg;
            if (GetString(doc.RootElement, "error") is { Length: > 0 } error) return error;
            if (doc.RootElement.TryGetProperty("error", out var nested)
                && GetString(nested, "message") is { Length: > 0 } message) return message;
        }
        catch (JsonException)
        {
            // Not every NASA error is JSON; fall through to the generic wording.
        }

        return "NASA refused this request.";
    }

    private static string Url(string path, string apiKey) =>
        $"https://api.nasa.gov/{path}?api_key={Uri.EscapeDataString(apiKey)}";

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
}

using System.Net;
using System.Text;
using System.Web;

namespace DashboardBackEnd.Tests;

// Stands in for api.nasa.gov so the suite never touches the network, and so the key
// pasted at subscribe time decides what comes back — which is what the real API does.
public class FakeNasaHandler : HttpMessageHandler
{
    public const string ValidKey = "nasa-valid-key";

    // Accepted at subscribe time, then out of quota on the rover endpoint: the way a key
    // rate-limits between two calls rather than up front.
    public const string ThrottledKey = "nasa-throttled-key";

    // valid, but NASA is out of quota for it on every endpoint, the key check included
    public const string ExhaustedKey = "nasa-exhausted-key";

    public const string KnownDate = "2026-09-18";
    public const string DateWithoutPicture = "1995-06-15"; // before the first APOD
    public const string RoverWithoutPhotos = "spirit";

    // the api answers this day with the site logo, the real entry is only on science.nasa.gov
    public const string PlaceholderDate = "2026-10-05";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);
        var apiKey = query["api_key"];

        if (uri.Host == "science.nasa.gov")
        {
            return ScienceSiteApod(query["before"]);
        }

        if (apiKey == ExhaustedKey)
        {
            return Json(HttpStatusCode.TooManyRequests, """
            {"error":{"code":"OVER_RATE_LIMIT","message":"You have exceeded your rate limit."}}
            """);
        }

        if (apiKey is not (ValidKey or ThrottledKey))
        {
            return Json(HttpStatusCode.Forbidden, """
            {"error":{"code":"API_KEY_INVALID","message":"An invalid api_key was supplied."}}
            """);
        }

        if (uri.AbsolutePath.StartsWith("/planetary/apod"))
        {
            return Apod(query["date"]);
        }

        if (apiKey == ThrottledKey)
        {
            return Json(HttpStatusCode.TooManyRequests, """
            {"error":{"code":"OVER_RATE_LIMIT","message":"You have exceeded your rate limit."}}
            """);
        }

        return MarsPhotos(uri.AbsolutePath.Split('/')[5]);
    }

    private static Task<HttpResponseMessage> Apod(string? date)
    {
        if (date == DateWithoutPicture)
        {
            return Json(HttpStatusCode.BadRequest, """
            {"code":400,"msg":"Date must be between Jun 16, 1995 and Sep 26, 2026.","service_version":"v1"}
            """);
        }

        if (date == PlaceholderDate)
        {
            return Json(HttpStatusCode.OK, """
            {"date":"2026-10-05","title":"NASA Science",
            "explanation":"Discover the cosmos! Each day a different image or photograph of our fascinating universe is featured.",
            "media_type":"image",
            "url":"https://science.nasa.gov/wp-content/themes/nasa-child/assets/images/nasa-logo@2x.png",
            "hdurl":"https://science.nasa.gov/wp-content/themes/nasa-child/assets/images/nasa-logo@2x.png",
            "copyright":"Clear Skies","service_version":"v1"}
            """);
        }

        return Json(HttpStatusCode.OK, """
        {"date":"%DATE%","title":"Pillars of Creation",
        "explanation":"A stellar nursery in the Eagle Nebula.","media_type":"image",
        "url":"https://apod.nasa.gov/apod/image/pillars.jpg",
        "hdurl":"https://apod.nasa.gov/apod/image/pillars_hd.jpg",
        "copyright":"NASA, ESA, CSA, STScI","service_version":"v1"}
        """.Replace("%DATE%", date ?? KnownDate));
    }

    private static Task<HttpResponseMessage> ScienceSiteApod(string? before)
    {
        if (before?.StartsWith(PlaceholderDate) != true)
        {
            return Json(HttpStatusCode.OK, "[]");
        }

        return Json(HttpStatusCode.OK, """
        [{"date":"2026-10-05T00:05:00",
        "title":{"rendered":"APOD: 2026 October 5 &#8211; M104: The Sombrero Galaxy&#8217;s Tidal Streams"},
        "content":{"rendered":"<figure><a href=\"https://science.nasa.gov/image-article/x/\"><img width=\"4222\" src=\"https://assets.science.nasa.gov/dynamicimage/assets/science/cds/apod/apod/2026/october/M104.jpg?w=4222&amp;h=2817\" alt=\"\"></a></figure><p class=\"p-md margin-0 media-detail-hero__description\">\n\t<strong>Explanation: </strong>A deep image of the <a href=\"https://en.wikipedia.org/wiki/Sombrero_Galaxy\">Sombrero galaxy</a> reveals surprises.</p>"}}]
        """);
    }

    private static Task<HttpResponseMessage> MarsPhotos(string rover)
    {
        if (rover == RoverWithoutPhotos)
        {
            return Json(HttpStatusCode.OK, """{"latest_photos":[]}""");
        }

        var name = char.ToUpper(rover[0]) + rover[1..];

        var photos = string.Join(",", Enumerable.Range(1, 5).Select(i => """
        {"id":%ID%,"sol":4102,"earth_date":"2026-09-17",
        "camera":{"id":20,"name":"FHAZ","full_name":"Front Hazard Avoidance Camera"},
        "img_src":"https://mars.nasa.gov/photo-%ID%.jpg",
        "rover":{"id":5,"name":"%ROVER%","status":"active"}}
        """.Replace("%ID%", i.ToString()).Replace("%ROVER%", name)));

        return Json(HttpStatusCode.OK, $"{{\"latest_photos\":[{photos}]}}");
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
}

using System.Net;
using System.Text;
using System.Web;

namespace DashboardBackEnd.Tests;

// Stands in for Duolingo's undocumented profile endpoint so the suite never touches the network.
// The payloads mirror what the real endpoint returns, including the empty-array answer
// it gives (with HTTP 200) for an unknown username.
public class FakeDuolingoHandler : HttpMessageHandler
{
    public const string KnownUser = "test";
    public const string SingleCourseUser = "solo";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var username = HttpUtility.ParseQueryString(request.RequestUri!.Query)["username"] ?? "";

        var json = username switch
        {
            KnownUser => TwoCourses,
            SingleCourseUser => OneCourse,
            _ => """{"users":[]}""",
        };

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    // Shaped like the real response for username=test, with a second course added
    // so the course widget's language parameter has something to choose between.
    private const string TwoCourses = """
    {"users":[{"id":60320,"username":"test","name":"john","streak":7,"totalXp":5643,
    "learningLanguage":"es","fromLanguage":"en","creationDate":1335829581,
    "courses":[
      {"authorId":"duolingo","fromLanguage":"en","id":"DUOLINGO_ES_EN","learningLanguage":"es","title":"Spanish","xp":5000,"crowns":42},
      {"authorId":"duolingo","fromLanguage":"en","id":"DUOLINGO_JA_EN","learningLanguage":"ja","title":"Japanese","xp":643,"crowns":9}
    ]}]}
    """;

    private const string OneCourse = """
    {"users":[{"id":99,"username":"solo","name":null,"streak":0,"totalXp":10,
    "learningLanguage":"de","fromLanguage":"en","courses":[]}]}
    """;
}

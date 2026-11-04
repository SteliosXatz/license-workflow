using DrivingLicenceWorkflow.Api.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;

namespace DrivingLicenceWorkflow.Tests.Logging;

public class PiiMaskingHttpLoggingInterceptorTests
{
    private static HttpLoggingInterceptorContext CreateContext(string path = "/api/applications") =>
        new() { HttpContext = new DefaultHttpContext { Request = { Path = path } } };

    [Fact]
    public async Task OnResponseAsync_MasksRequestAndResponseBodies()
    {
        var context = CreateContext();
        context.AddParameter("RequestBody", """{"email":"jane@example.com","licenceCategory":"B"}""");
        context.AddParameter("ResponseBody", """{"nationalId":"AB123","status":"Submitted"}""");

        await new PiiMaskingHttpLoggingInterceptor().OnResponseAsync(context);

        var logged = string.Join(' ', context.Parameters.Select(p => p.Value));
        Assert.DoesNotContain("jane@example.com", logged);
        Assert.DoesNotContain("AB123", logged);
        Assert.Contains("\"licenceCategory\":\"B\"", logged);
        Assert.Contains("\"status\":\"Submitted\"", logged);
    }

    [Fact]
    public async Task OnResponseAsync_DropsBodiesThatAreNotValidJson()
    {
        var context = CreateContext();
        context.AddParameter("RequestBody", """{"email":"jane@exam""");

        await new PiiMaskingHttpLoggingInterceptor().OnResponseAsync(context);

        Assert.DoesNotContain("jane", (string)context.Parameters.Single().Value!);
    }

    [Fact]
    public async Task OnResponseAsync_MasksSensitiveQueryValues()
    {
        var context = CreateContext();
        context.AddParameter("QueryString", "?email=jane%40example.com&page=2");

        await new PiiMaskingHttpLoggingInterceptor().OnResponseAsync(context);

        var query = (string)context.Parameters.Single().Value!;
        Assert.DoesNotContain("jane", query);
        Assert.Contains("page=2", query);
    }

    [Fact]
    public async Task OnRequestAsync_DisablesLoggingForSwagger()
    {
        var context = CreateContext("/swagger/index.html");
        context.LoggingFields = HttpLoggingFields.All;

        await new PiiMaskingHttpLoggingInterceptor().OnRequestAsync(context);

        Assert.Equal(HttpLoggingFields.None, context.LoggingFields);
    }
}

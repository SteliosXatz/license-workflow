using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.WebUtilities;

namespace DrivingLicenceWorkflow.Api.Logging;

/// <summary>Masks PII in the bodies and query string captured by ASP.NET Core HTTP logging.</summary>
public sealed class PiiMaskingHttpLoggingInterceptor : IHttpLoggingInterceptor
{
    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext)
    {
        if (logContext.HttpContext.Request.Path.StartsWithSegments("/swagger"))
        {
            logContext.Disable(HttpLoggingFields.All);
        }

        return ValueTask.CompletedTask;
    }

    // With CombineLogs enabled, the request and response bodies are both in Parameters by now.
    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext)
    {
        for (var i = 0; i < logContext.Parameters.Count; i++)
        {
            var (key, value) = logContext.Parameters[i];
            if (value is not string text)
            {
                continue;
            }

            var masked = key switch
            {
                "RequestBody" or "ResponseBody" => MaskBody(text),
                "QueryString" => MaskQueryString(text),
                _ => text
            };
            logContext.Parameters[i] = new KeyValuePair<string, object?>(key, masked);
        }

        return ValueTask.CompletedTask;
    }

    private static string MaskBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        // A truncated body is not valid JSON, so it is dropped rather than logged unmasked.
        return PiiMasker.MaskJson(body) ?? "[unparseable body omitted]";
    }

    private static string MaskQueryString(string queryString)
    {
        if (queryString.Length <= 1)
        {
            return queryString;
        }

        var pairs = QueryHelpers.ParseQuery(queryString)
            .SelectMany(pair => pair.Value.Select(value =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(PiiMasker.MaskValue(pair.Key, value ?? string.Empty))}"));
        return "?" + string.Join('&', pairs);
    }
}

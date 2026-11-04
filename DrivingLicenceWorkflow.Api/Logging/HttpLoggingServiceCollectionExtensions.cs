using Microsoft.AspNetCore.HttpLogging;

namespace DrivingLicenceWorkflow.Api.Logging;

public static class HttpLoggingServiceCollectionExtensions
{
    /// <summary>Adds ASP.NET Core HTTP logging of requests and responses with PII masked.</summary>
    public static IServiceCollection AddPiiMaskedHttpLogging(this IServiceCollection services)
    {
        services.AddHttpLogging(options =>
        {
            options.LoggingFields =
                HttpLoggingFields.RequestPropertiesAndHeaders |
                HttpLoggingFields.RequestBody |
                HttpLoggingFields.ResponsePropertiesAndHeaders |
                HttpLoggingFields.ResponseBody;
            options.CombineLogs = true;
            options.RequestBodyLogLimit = 1024 * 1024;
            options.ResponseBodyLogLimit = 1024 * 1024;
            options.MediaTypeOptions.Clear();
            options.MediaTypeOptions.AddText("application/json");
            options.MediaTypeOptions.AddText("application/problem+json");

            // Headers not listed here are logged as [Redacted].
            options.RequestHeaders.Add("Content-Type");
            options.RequestHeaders.Add("Content-Length");
            options.RequestHeaders.Add("Accept");
            options.ResponseHeaders.Add("Content-Type");
            options.ResponseHeaders.Add("Content-Length");
        });
        services.AddHttpLoggingInterceptor<PiiMaskingHttpLoggingInterceptor>();

        return services;
    }
}

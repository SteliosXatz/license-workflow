using System.Text.Json;
using System.Text.Json.Nodes;

namespace DrivingLicenceWorkflow.Api.Logging;

public static class PiiMasker
{
    public const string Mask = "***";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "firstName",
        "lastName",
        "dateOfBirth",
        "nationalId",
        "email",
        "phoneNumber",
        "address",
        "photoBase64",
        "authorization",
        "cookie",
        "set-cookie",
        "x-api-key"
    };

    public static bool IsSensitive(string name) => SensitiveNames.Contains(name);

    public static string MaskValue(string name, string value) => IsSensitive(name) ? Mask : value;

    /// <summary>Returns the JSON with sensitive property values replaced, or null if it is not valid JSON.</summary>
    public static string? MaskJson(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        MaskNode(node);
        return node?.ToJsonString() ?? "null";
    }

    private static void MaskNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToArray())
                {
                    if (IsSensitive(property.Key) && property.Value is not null)
                    {
                        obj[property.Key] = Mask;
                    }
                    else
                    {
                        MaskNode(property.Value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    MaskNode(item);
                }

                break;
        }
    }
}

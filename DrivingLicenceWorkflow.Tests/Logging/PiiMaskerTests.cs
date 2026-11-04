using DrivingLicenceWorkflow.Api.Logging;

namespace DrivingLicenceWorkflow.Tests.Logging;

public class PiiMaskerTests
{
    [Fact]
    public void MaskJson_MasksAllPiiFields()
    {
        const string json = """
            {"firstName":"Jane","lastName":"Doe","dateOfBirth":"1990-01-01","nationalId":"AB123",
             "email":"jane@example.com","phoneNumber":"555","address":"1 Main St",
             "licenceCategory":"B","photoBase64":"AAAA"}
            """;

        var masked = PiiMasker.MaskJson(json)!;

        foreach (var secret in new[] { "Jane", "Doe", "1990-01-01", "AB123", "jane@example.com", "555", "1 Main St", "AAAA" })
        {
            Assert.DoesNotContain(secret, masked);
        }

        Assert.Contains("\"licenceCategory\":\"B\"", masked);
    }

    [Fact]
    public void MaskJson_MasksNestedObjectsAndArrays()
    {
        var masked = PiiMasker.MaskJson("""{"items":[{"email":"a@b.com","id":1}]}""")!;

        Assert.DoesNotContain("a@b.com", masked);
        Assert.Contains("\"id\":1", masked);
    }

    [Fact]
    public void MaskJson_ReturnsNullForInvalidJson() =>
        Assert.Null(PiiMasker.MaskJson("not json"));

    [Theory]
    [InlineData("Authorization", "***")]
    [InlineData("email", "***")]
    [InlineData("Accept", "application/json")]
    public void MaskValue_MasksOnlySensitiveNames(string name, string expected) =>
        Assert.Equal(expected, PiiMasker.MaskValue(name, "application/json"));
}

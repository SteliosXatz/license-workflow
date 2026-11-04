using System.Security.Cryptography;
using DrivingLicenceWorkflow.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace DrivingLicenceWorkflow.Shared.Services;

public class PhotoCheckService : IPhotoCheckService
{
    private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly PhotoCheckOptions _options;

    public PhotoCheckService(IOptions<PhotoCheckOptions> options)
    {
        _options = options.Value;
    }

    public PhotoCheckResult Check(string? photoBase64)
    {
        if (string.IsNullOrWhiteSpace(photoBase64))
        {
            return Fail(0, "No photo provided.");
        }

        if (!TryDecode(photoBase64, out var bytes))
        {
            return Fail(0, "Photo is not valid base64.");
        }

        if (!StartsWith(bytes, JpegSignature) && !StartsWith(bytes, PngSignature))
        {
            return Fail(0, "Unsupported photo file type; only JPEG and PNG are accepted.");
        }

        // The score depends only on the content, so the same photo always scores the same.
        var score = SHA256.HashData(bytes)[0] % 100;

        if (bytes.Length < _options.MinPhotoFileSizeBytes)
        {
            return Fail(score, $"Photo file size {bytes.Length} bytes is below the minimum of {_options.MinPhotoFileSizeBytes} bytes.");
        }

        if (score < _options.MinPhotoQualityScore)
        {
            return Fail(score, $"Photo quality score {score} is below the minimum of {_options.MinPhotoQualityScore}.");
        }

        return new PhotoCheckResult(true, score, null);
    }

    private static PhotoCheckResult Fail(int score, string reason) => new(false, score, reason);

    private static bool StartsWith(byte[] bytes, byte[] signature) =>
        bytes.AsSpan().StartsWith(signature);

    // Accepts a bare base64 string or a data URI ("data:image/png;base64,...").
    private static bool TryDecode(string value, out byte[] bytes)
    {
        var payload = value.Trim();
        var comma = payload.IndexOf(',');
        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
        {
            payload = payload[(comma + 1)..];
        }

        try
        {
            bytes = Convert.FromBase64String(payload);
            return true;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }
}

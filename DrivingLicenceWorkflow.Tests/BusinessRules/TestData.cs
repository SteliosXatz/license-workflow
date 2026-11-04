using System.Security.Cryptography;
using DrivingLicenceWorkflow.Shared.Services;
using Microsoft.Extensions.Time.Testing;

namespace DrivingLicenceWorkflow.Tests.BusinessRules;

internal static class TestData
{
    // Fixed "now" so age boundaries do not depend on the day the tests run.
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    public static FakeTimeProvider Clock(DateTimeOffset? now = null) => new(now ?? Now);

    public static ApplicationData ValidApplication(
        DateTime? dateOfBirth = null,
        string? firstName = "Jane",
        string? lastName = "Doe",
        string? nationalId = "NID-1",
        string? email = "jane.doe@example.com",
        string? phoneNumber = "+30 210 0000000",
        string? address = "1 Main Street",
        string? licenceCategory = "B",
        string? photoBase64 = "cGhvdG8=") =>
        new(firstName, lastName, dateOfBirth ?? new DateTime(1990, 1, 1), nationalId, email,
            phoneNumber, address, licenceCategory, photoBase64);

    public static class Photo
    {
        public static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0 };
        public static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        // Same formula as the service, written independently so the tests pin the contract.
        public static int ExpectedScore(byte[] bytes) => SHA256.HashData(bytes)[0] % 100;

        // Builds a photo of exactly `size` bytes whose score satisfies `scoreFilter`, by varying filler bytes.
        public static byte[] Create(byte[] signature, int size, Func<int, bool>? scoreFilter = null)
        {
            scoreFilter ??= _ => true;
            for (var seed = 0; seed < 100_000; seed++)
            {
                var bytes = new byte[size];
                signature.CopyTo(bytes, 0);
                BitConverter.GetBytes(seed).CopyTo(bytes, signature.Length);
                if (scoreFilter(ExpectedScore(bytes)))
                {
                    return bytes;
                }
            }

            throw new InvalidOperationException("No photo found for the requested score.");
        }

        public static string ToBase64(byte[] bytes) => Convert.ToBase64String(bytes);
    }
}

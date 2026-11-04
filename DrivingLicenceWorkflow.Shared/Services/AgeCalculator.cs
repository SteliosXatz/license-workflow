namespace DrivingLicenceWorkflow.Shared.Services;

internal static class AgeCalculator
{
    public static int AgeOn(DateTime dateOfBirth, DateTime utcNow)
    {
        var today = utcNow.Date;
        var dob = dateOfBirth.Date;
        var age = today.Year - dob.Year;
        if (dob > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }
}

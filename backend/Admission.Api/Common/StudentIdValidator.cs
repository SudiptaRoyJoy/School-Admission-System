using System.Text.RegularExpressions;

namespace Admission.Api.Common;

public static partial class StudentIdValidator
{
    [GeneratedRegex("^[0-9]{17}$", RegexOptions.CultureInvariant)]
    private static partial Regex StudentIdPattern();

    public static string FromRequest(HttpRequest request)
    {
        var value = request.Headers["X-Student-Id"].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw ApiException.BadRequest("MISSING_STUDENT_ID", "Enter the student's 17-digit birth-registration number.");

        if (!StudentIdPattern().IsMatch(value))
            throw ApiException.BadRequest("INVALID_STUDENT_ID", "The student ID must contain exactly 17 digits (0–9).");

        return value;
    }
}

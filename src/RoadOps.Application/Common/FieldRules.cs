using System.Text.RegularExpressions;

namespace RoadOps.Application.Common;

/// <summary>Field limits and validation helpers shared by the services. Limits match the database column sizes.</summary>
public static partial class FieldRules
{
    public const int MaxNameLength = 255;
    public const int MaxCorridorLength = 12;
    public const int MaxNotesLength = 1000;
    public const int MaxRecommendedActionLength = 500;
    public const int MaxImagePaths = 20;
    public const int MaxImagePathLength = 500;

    public const int MinSurveyYear = 1990;
    public const int MaxSurveyYear = 2100;

    public const int MinDegree = 0;
    public const int MaxDegree = 5;

    public const double MaxRutDepthMm = 200;
    public const double MaxLengthM = 1000;
    public const double MaxWidthM = 50;
    public const double MaxDepthMm = 500;

    /// <summary>Tolerance for floating-point chainage comparisons (1 mm).</summary>
    public const double ChainageTolerance = 1e-6;

    [GeneratedRegex("^[A-Z0-9]{1,12}$")]
    private static partial Regex CorridorPattern();

    /// <summary>Trims and upper-cases a corridor code ("n1 " → "N1") and checks it is 1–12 letters/digits.</summary>
    public static string NormaliseCorridor(string? corridor, string paramName)
    {
        var normalised = (corridor ?? string.Empty).Trim().ToUpperInvariant();
        if (!CorridorPattern().IsMatch(normalised))
        {
            throw new ArgumentException("Corridor must be 1 to 12 letters or digits, e.g. \"N1\".", paramName);
        }

        return normalised;
    }

    public static void RequireText(string? value, int maxLength, string message, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, paramName);
        }

        RequireMaxLength(value, maxLength, paramName);
    }

    public static void RequireMaxLength(string? value, int maxLength, string paramName)
    {
        if (value is not null && value.Length > maxLength)
        {
            throw new ArgumentException($"{paramName} must be at most {maxLength} characters.", paramName);
        }
    }

    public static void RequireRange(double value, double min, double max, string paramName)
    {
        if (double.IsNaN(value) || value < min || value > max)
        {
            throw new ArgumentException($"{paramName} must be between {min} and {max}.", paramName);
        }
    }

    public static void RequireRange(double? value, double min, double max, string paramName)
    {
        if (value is { } v)
        {
            RequireRange(v, min, max, paramName);
        }
    }

    public static void RequireSurveyYear(int year, string paramName)
    {
        if (year is < MinSurveyYear or > MaxSurveyYear)
        {
            throw new ArgumentException($"Survey year must be between {MinSurveyYear} and {MaxSurveyYear}.", paramName);
        }
    }

    /// <summary>Checks a km range is non-negative and ordered. <paramref name="allowEmpty"/> permits From == To.</summary>
    public static void RequireChainageRange(double from, double to, bool allowEmpty, string fromParam, string toParam)
    {
        if (double.IsNaN(from) || from < 0)
        {
            throw new ArgumentException("Chainage from must be non-negative.", fromParam);
        }

        if (double.IsNaN(to) || to < 0)
        {
            throw new ArgumentException("Chainage to must be non-negative.", toParam);
        }

        if (from > to || (!allowEmpty && from == to))
        {
            throw new ArgumentException(allowEmpty
                ? "Chainage from must be less than or equal to chainage to."
                : "Chainage from must be less than chainage to.", fromParam);
        }
    }

    /// <summary>
    /// Free text is stored as data. Control characters (other than new lines and tabs) are rejected so notes cannot smuggle
    /// terminal escapes or invisible formatting into logs and model context.
    /// </summary>
    public static string? CleanNotes(string? notes, string paramName)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var trimmed = notes.Trim();
        RequireMaxLength(trimmed, MaxNotesLength, paramName);
        if (trimmed.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
        {
            throw new ArgumentException("Notes must not contain control characters.", paramName);
        }

        return trimmed;
    }
}

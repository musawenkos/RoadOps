using RoadOps.Application.Analytics;
using RoadOps.Application.Common;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Enum;

namespace RoadOps.Application.Services;

/// <summary>
/// Turns human references ("N1", 2026, "S03", km 40–60) into workspaces, sections and scopes, so callers never need ids.
/// Unresolvable or ambiguous references throw <see cref="ArgumentException"/> with a message that lists the options.
/// </summary>
public class SurveyResolver
{
    public const int MaxCandidates = 20;

    private readonly IConditionAnalyticsRepository _analytics;

    public SurveyResolver(IConditionAnalyticsRepository analytics)
    {
        _analytics = analytics;
    }

    public async Task<IReadOnlyList<WorkspaceOverview>> FindSurveysAsync(SurveyReference reference, WorkspaceStatus? status = null, int limit = MaxCandidates, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaxCandidates)
        {
            throw new ArgumentException($"Limit must be between 1 and {MaxCandidates}.", nameof(limit));
        }

        return await _analytics.FindWorkspacesAsync(ToSearch(reference, status, limit), cancellationToken);
    }

    /// <summary>
    /// Resolves a reference to exactly one survey. An exact name match wins. Otherwise the most recent survey year is taken
    /// (so "N1" means the latest N1 survey), preferring an Active survey when a year has several.
    /// </summary>
    public async Task<WorkspaceOverview> ResolveSurveyAsync(SurveyReference reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference.Corridor) && reference.SurveyYear is null && string.IsNullOrWhiteSpace(reference.Name))
        {
            throw new ArgumentException("Name a survey by corridor (e.g. N1), survey year and/or name.", nameof(reference));
        }

        var candidates = await _analytics.FindWorkspacesAsync(ToSearch(reference, null, MaxCandidates), cancellationToken);
        if (candidates.Count == 0)
        {
            throw new ArgumentException($"No survey matches {Describe(reference)}.", nameof(reference));
        }

        var exact = candidates.Where(c => string.Equals(c.Name, reference.Name?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
        {
            return exact[0];
        }

        var latestYear = candidates.Max(c => c.SurveyYear);
        var latest = candidates.Where(c => c.SurveyYear == latestYear).ToList();
        if (latest.Count > 1)
        {
            var active = latest.Where(c => c.Status == WorkspaceStatus.Active).ToList();
            if (active.Count == 1)
            {
                return active[0];
            }
        }

        if (latest.Count == 1)
        {
            return latest[0];
        }

        throw new ArgumentException(
            $"{Describe(reference)} matches {latest.Count} surveys: {string.Join("; ", latest.Take(5).Select(c => c.Name))}. Please be more specific.",
            nameof(reference));
    }

    public async Task<ResolvedScope> ResolveScopeAsync(ScopeRequest request, CancellationToken cancellationToken = default)
    {
        var survey = await ResolveSurveyAsync(request.Survey, cancellationToken);

        if (request.FromKm is { } from && request.ToKm is { } to)
        {
            FieldRules.RequireChainageRange(from, to, allowEmpty: false, nameof(request.FromKm), nameof(request.ToKm));
        }
        else
        {
            FieldRules.RequireRange(request.FromKm, 0, double.MaxValue, nameof(request.FromKm));
            FieldRules.RequireRange(request.ToKm, 0, double.MaxValue, nameof(request.ToKm));
        }

        SectionRef? section = null;
        if (!string.IsNullOrWhiteSpace(request.SectionName))
        {
            section = await ResolveSectionAsync(survey, request.SectionName, cancellationToken);
        }

        return new ResolvedScope(survey, section, request.FromKm, request.ToKm);
    }

    /// <summary>Finds a section by (part of) its name, e.g. "S03" or "Hammanskraal".</summary>
    public async Task<SectionRef> ResolveSectionAsync(WorkspaceOverview survey, string sectionName, CancellationToken cancellationToken = default)
    {
        FieldRules.RequireText(sectionName, FieldRules.MaxNameLength, "Section name is required.", nameof(sectionName));
        var name = sectionName.Trim();

        var sections = await _analytics.GetSectionOverviewsAsync(survey.Id, cancellationToken);
        var matches = sections.Where(s => string.Equals(s.SectionName, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            matches = sections.Where(s => s.SectionName.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return matches.Count switch
        {
            1 => new SectionRef(matches[0].Id, matches[0].SectionName, matches[0].ChainageFrom, matches[0].ChainageTo),
            0 => throw new ArgumentException($"Survey '{survey.Name}' has no section matching '{name}'.", nameof(sectionName)),
            _ => throw new ArgumentException(
                $"'{name}' matches {matches.Count} sections: {string.Join("; ", matches.Take(5).Select(s => s.SectionName))}. Please be more specific.",
                nameof(sectionName)),
        };
    }

    private static WorkspaceSearch ToSearch(SurveyReference reference, WorkspaceStatus? status, int limit)
    {
        var corridor = string.IsNullOrWhiteSpace(reference.Corridor) ? null : FieldRules.NormaliseCorridor(reference.Corridor, nameof(reference.Corridor));

        if (reference.SurveyYear is { } year)
        {
            FieldRules.RequireSurveyYear(year, nameof(reference.SurveyYear));
        }

        var name = string.IsNullOrWhiteSpace(reference.Name) ? null : reference.Name.Trim();
        FieldRules.RequireMaxLength(name, FieldRules.MaxNameLength, nameof(reference.Name));

        return new WorkspaceSearch(corridor, reference.SurveyYear, name, status, limit);
    }

    private static string Describe(SurveyReference reference)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(reference.Corridor)) parts.Add($"corridor {reference.Corridor.Trim().ToUpperInvariant()}");
        if (reference.SurveyYear is { } year) parts.Add($"year {year}");
        if (!string.IsNullOrWhiteSpace(reference.Name)) parts.Add($"name '{reference.Name.Trim()}'");
        return parts.Count == 0 ? "the request" : string.Join(", ", parts);
    }
}

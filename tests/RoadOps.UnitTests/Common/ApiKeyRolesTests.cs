using RoadOps.Auth;

namespace RoadOps.UnitTests.Common;

public class ApiKeyRolesTests
{
    [Theory]
    [InlineData(null, "reader")]
    [InlineData("", "reader")]
    [InlineData("superuser", "reader")]
    [InlineData("reader", "reader")]
    [InlineData("Editor", "editor")]
    [InlineData(" ADMIN ", "admin")]
    public void Resolve_MissingOrUnknownRoles_AreReader(string? configured, string expected)
    {
        Assert.Equal(expected, ApiKeyRoles.Resolve(configured));
    }

    [Theory]
    [InlineData(null, new[] { "reader" })]
    [InlineData("editor", new[] { "reader", "editor" })]
    [InlineData("admin", new[] { "reader", "editor", "admin" })]
    public void Expand_IncludesEveryLowerRole(string? role, string[] expected)
    {
        Assert.Equal(expected, ApiKeyRoles.Expand(role));
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("Reader", true)]
    [InlineData("superuser", false)]
    [InlineData(null, false)]
    public void IsKnown_IsCaseInsensitive(string? role, bool expected)
    {
        Assert.Equal(expected, ApiKeyRoles.IsKnown(role));
    }
}

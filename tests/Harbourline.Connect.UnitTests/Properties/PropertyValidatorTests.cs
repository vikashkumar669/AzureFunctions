using Harbourline.Connect.Contracts.Properties;
using Harbourline.Connect.Core.Properties;

namespace Harbourline.Connect.UnitTests.Properties;

public sealed class PropertyValidatorTests
{
    private static CreatePropertyRequest Valid() => new(
        "HBR-0042", "1 Quay Street", "Leeds", "LS1 4AP", 2, 950m,
        new DateOnly(2026, 11, 1));

    [Fact]
    public void ValidRequest_HasNoErrors() =>
        Assert.True(PropertyValidator.Validate(Valid()).IsValid);

    // <listing:test-theory>
    [Theory]
    [InlineData("hbr-0042")]   // lower case
    [InlineData("HBR-42")]     // too short
    [InlineData("HBR_0042")]   // wrong separator
    [InlineData("")]
    public void BadCode_IsRejected(string code)
    {
        var errors = PropertyValidator.Validate(
            Valid() with { Code = code });

        Assert.False(errors.IsValid);
        Assert.Contains("code", errors.ToDictionary().Keys);
    }
    // </listing:test-theory>

    [Fact]
    public void AllProblems_AreReportedTogether()
    {
        var errors = PropertyValidator.Validate(new CreatePropertyRequest(
            null, null, null, null, -1, 0m, null)).ToDictionary();

        Assert.Equal(
            new[] { "bedrooms", "city", "code", "monthlyRent", "name",
                "postcode" },
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EmptyPatch_IsRejected()
    {
        var errors = PropertyValidator.Validate(
            new UpdatePropertyRequest(null, null, null, null, null));
        Assert.Contains("body", errors.ToDictionary().Keys);
    }

    [Theory]
    [InlineData("available", true)]
    [InlineData("LET", true)]
    [InlineData("100000000", false)]
    [InlineData("Sold", false)]
    public void StatusNames_AreParsedCaseInsensitively(
        string value, bool expected) =>
        Assert.Equal(expected,
            PropertyValidator.TryParseStatus(value, out _));
}

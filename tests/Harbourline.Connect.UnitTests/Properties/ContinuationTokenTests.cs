using Harbourline.Connect.Contracts.Properties;
using Harbourline.Connect.Core.Properties;

namespace Harbourline.Connect.UnitTests.Properties;

public sealed class ContinuationTokenTests
{
    [Fact]
    public void Token_RoundTrips()
    {
        var filter = ContinuationToken.FilterOf(
            new PropertyQuery("Leeds", null, 2));
        var token = ContinuationToken.Encode(
            new PagePosition(3, "<cookie page=\"2\"/>", filter));

        var position = ContinuationToken.Decode(token, filter);

        Assert.Equal(3, position.Page);
        Assert.Equal("<cookie page=\"2\"/>", position.Cookie);
        Assert.DoesNotContain("+", token, StringComparison.Ordinal);
    }

    [Fact]
    public void NoToken_StartsAtPageOne() =>
        Assert.Equal(1, ContinuationToken.Decode(null, "x").Page);

    [Fact]
    public void TokenFromAnotherQuery_IsRejected()
    {
        var leeds = ContinuationToken.FilterOf(
            new PropertyQuery("Leeds", null, null));
        var york = ContinuationToken.FilterOf(
            new PropertyQuery("York", null, null));
        var token = ContinuationToken.Encode(
            new PagePosition(2, null, leeds));

        Assert.Throws<FormatException>(
            () => ContinuationToken.Decode(token, york));
    }

    [Fact]
    public void Garbage_IsRejected() =>
        Assert.Throws<FormatException>(
            () => ContinuationToken.Decode("not-a-token!", "x"));
}

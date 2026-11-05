using Harbourline.Connect.Core.Integrations;

namespace Harbourline.Connect.UnitTests.Integrations;

public sealed class CallbackSignatureTests
{
    private const string Secret = "test-secret-that-is-at-least-32-characters";
    private const string Body = """{"envelopeId":"ENV-1","status":"Signed"}""";

    [Fact]
    public void CorrectSignature_IsAccepted() =>
        Assert.True(CallbackSignature.IsValid(
            Body, CallbackSignature.Sign(Body, Secret), Secret));

    [Fact]
    public void ChangedBody_IsRejected() =>
        Assert.False(CallbackSignature.IsValid(
            Body.Replace("Signed", "Declined", StringComparison.Ordinal),
            CallbackSignature.Sign(Body, Secret), Secret));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zz-not-hex")]
    public void MissingOrMalformedSignature_IsRejected(string? signature) =>
        Assert.False(CallbackSignature.IsValid(Body, signature, Secret));
}

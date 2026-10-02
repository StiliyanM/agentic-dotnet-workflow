using System.Security.Cryptography;
using System.Text;
using AgenticPayments.Application.Webhooks;
using AutoFixture;

namespace AgenticPayments.UnitTests.Webhooks;

public sealed class WebhookPayloadHashTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void Compute_KnownPayload_ReturnsLowercaseSha256OfCanonicalString()
    {
        var paymentId = _fixture.Create<Guid>();
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{paymentId:D}|Succeeded")));

        var hash = WebhookPayloadHash.Compute(paymentId, ProviderPaymentStatus.Succeeded);

        Assert.Multiple(
            () => Assert.Equal(expected, hash),
            () => Assert.Equal(64, hash.Length));
    }

    [Theory]
    [InlineData("other paymentId")]
    [InlineData("other status")]
    public void Compute_DifferentPaymentIdOrStatus_ReturnsDifferentHash(string difference)
    {
        var paymentId = _fixture.Create<Guid>();
        var hash = WebhookPayloadHash.Compute(paymentId, ProviderPaymentStatus.Succeeded);

        var otherHash = difference switch
        {
            "other paymentId" => WebhookPayloadHash.Compute(_fixture.Create<Guid>(), ProviderPaymentStatus.Succeeded),
            "other status" => WebhookPayloadHash.Compute(paymentId, ProviderPaymentStatus.Failed),
            _ => throw new ArgumentOutOfRangeException(nameof(difference), difference, null),
        };

        Assert.NotEqual(hash, otherHash);
    }
}

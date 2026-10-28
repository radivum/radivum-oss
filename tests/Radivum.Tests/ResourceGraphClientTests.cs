using System.Net;
using System.Text;
using System.Text.Json;
using Radivum.Azure;
using Xunit;

namespace Radivum.Tests;

/// <summary>
/// Covers the wire contract of the Resource Graph request: every selected subscription travels in one query, and the
/// truncation flag is read in either encoding Azure emits for it.
/// </summary>
/// <remarks>
/// A live tenant answered with the string <c>"resultTruncated": "false"</c> on every page while a continuation token
/// still led to more rows. Reading only booleans therefore ignores a true string, and treating the flag as the paging
/// decision would stop a collection early. The continuation token is what says more rows exist.
/// </remarks>
public sealed class ResourceGraphClientTests
{
    private const string FirstSubscription = "11111111-1111-1111-1111-111111111111";
    private const string SecondSubscription = "22222222-2222-2222-2222-222222222222";

    [Theory]
    [InlineData("\"false\"", false)]
    [InlineData("\"true\"", true)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public async Task QueryAsync_GivenTheTruncationFlagInEitherEncoding_ReadsIt(string flag, bool expected)
    {
        var client = CreateClient(
            $$"""{"count":0,"data":[],"resultTruncated":{{flag}},"$skipToken":"next-page"}""");

        var result = await client.QueryAsync("token", "resources", [FirstSubscription], skipToken: null);

        Assert.Equal(expected, result.IsTruncated);
    }

    [Fact]
    public async Task QueryAsync_GivenAFlagThatSaysFalseButAContinuationToken_KeepsTheTokenForPaging()
    {
        var client = CreateClient(
            """{"count":0,"data":[],"resultTruncated":"false","$skipToken":"next-page"}""");

        var result = await client.QueryAsync("token", "resources", [FirstSubscription], skipToken: null);

        Assert.False(result.IsTruncated);
        Assert.Equal("next-page", result.SkipToken);
    }

    [Fact]
    public async Task QueryAsync_GivenSeveralSubscriptions_SendsEveryOneInTheRequestBody()
    {
        var handler = new RecordingHandler("""{"count":0,"data":[],"resultTruncated":"false"}""");
        var client = new ResourceGraphClient(new StubHttpClientFactory(handler));

        await client.QueryAsync(
            "token",
            "resources",
            [FirstSubscription, SecondSubscription],
            skipToken: null);

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var sent = document.RootElement
            .GetProperty("subscriptions")
            .EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .ToArray();

        Assert.Equal([FirstSubscription, SecondSubscription], sent);
    }

    [Fact]
    public async Task QueryAsync_GivenNoSubscription_Throws()
    {
        var client = CreateClient("""{"count":0,"data":[],"resultTruncated":"false"}""");

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.QueryAsync("token", "resources", [], skipToken: null));
    }

    private static ResourceGraphClient CreateClient(string responseBody)
        => new(new StubHttpClientFactory(new RecordingHandler(responseBody)));

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://management.azure.com/"),
            };
    }

    /// <summary>Answers with a fixed body and captures the request body it was sent.</summary>
    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}

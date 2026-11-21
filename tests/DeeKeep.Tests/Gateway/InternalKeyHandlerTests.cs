using System.Net;
using DeeKeep.Common.Hosting;
using Xunit;

namespace DeeKeep.Tests.Gateway;

/// <summary>Clé interne entre services, y compris quand la résilience réessaie la même requête.</summary>
public class InternalKeyHandlerTests
{
    /// <summary>Retient les valeurs de l'en-tête reçues à chaque envoi.</summary>
    private sealed class Recorder : HttpMessageHandler
    {
        public List<string[]> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add(request.Headers.TryGetValues(ServiceDefaults.InternalKeyHeader, out var v) ? v.ToArray() : []);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task A_retried_request_still_carries_the_key_once()
    {
        var recorder = new Recorder();
        using var invoker = new HttpMessageInvoker(new InternalKeyHandler(new InternalKey("k")) { InnerHandler = recorder });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1/api/tracks");
        var ct = TestContext.Current.CancellationToken;

        // Premier essai expiré puis réessai : la même requête repasse par le gestionnaire.
        await invoker.SendAsync(request, ct);
        await invoker.SendAsync(request, ct);

        Assert.All(recorder.Seen, values => Assert.Equal(["k"], values));
    }
}

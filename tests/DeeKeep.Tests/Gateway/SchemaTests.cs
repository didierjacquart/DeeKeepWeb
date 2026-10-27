using DeeKeep.Common.Events;
using DeeKeep.Common.Hosting;
using DeeKeep.Gateway.Infrastructure;
using HotChocolate.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DeeKeep.Tests.Gateway;

public class SchemaTests
{
    /// <summary>
    /// Le schéma GraphQL complet se construit (types de tous les domaines, sans
    /// collision de noms). Écrit le SDL dans docs/schema.graphql pour le client.
    /// </summary>
    [Fact]
    public async Task Schema_builds()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<ServiceEndpoints>();
        services.AddHttpClient();
        services.AddSingleton<ServiceDirectory>();
        var schema = await services.AddDeeKeepGraphQL(development: true).BuildSchemaAsync(cancellationToken: TestContext.Current.CancellationToken);
        var sdl = schema.ToString();
        Assert.Contains("type Query", sdl);
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "DeeKeep.slnx"))) root = Path.GetDirectoryName(root);
        if (root is not null) await File.WriteAllTextAsync(Path.Combine(root, "docs", "schema.graphql"), sdl, TestContext.Current.CancellationToken);
    }
}

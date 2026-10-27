using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;

namespace DeeKeep.Gateway.Infrastructure;

public static class GraphQLSetup
{
    /// <summary>Schéma GraphQL de la passerelle (partagé par Program.cs et les tests de construction du schéma).</summary>
    public static IRequestExecutorBuilder AddDeeKeepGraphQL(this IServiceCollection services, bool development) =>
        services
            .AddGraphQLServer()
            .AddGatewayTypes()
            .AddInMemorySubscriptions()
            // Charges utiles libres (paramètres, résultats de tâches) : scalaire « Any ».
            .BindRuntimeType<JsonNode, AnyType>()
            .AddTypeConverter<JsonNode, JsonElement>(n => JsonSerializer.SerializeToElement(n))
            .AddTypeConverter<JsonElement, JsonNode>(e => JsonNode.Parse(e.GetRawText())!)
            .AddErrorFilter<ServiceErrorFilter>()
            .ModifyRequestOptions(o => o.IncludeExceptionDetails = development);
}

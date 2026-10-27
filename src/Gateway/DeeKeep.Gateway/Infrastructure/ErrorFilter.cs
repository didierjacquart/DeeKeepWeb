using DeeKeep.Contracts.Clients;
using HotChocolate;
using HotChocolate.Execution;

namespace DeeKeep.Gateway.Infrastructure;

/// <summary>Traduit les pannes de service en erreurs GraphQL lisibles : le reste de la réponse reste servi.</summary>
public sealed class ServiceErrorFilter : IErrorFilter
{
    public IError OnError(IError error) => error.Exception switch
    {
        ServiceUnavailableException ex when ex.Status is null || (int)ex.Status >= 500 =>
            error.WithMessage($"Service « {ex.Service} » momentanément indisponible.").WithCode("SERVICE_UNAVAILABLE"),
        ServiceUnavailableException ex => error.WithMessage(ex.Message).WithCode("SERVICE_ERROR"),
        ArgumentException ex => error.WithMessage(ex.Message).WithCode("INVALID_ARGUMENT"),
        InvalidOperationException ex => error.WithMessage(ex.Message).WithCode("INVALID_OPERATION"),
        _ => error,
    };
}

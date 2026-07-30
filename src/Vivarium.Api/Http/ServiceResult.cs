namespace Vivarium.Api.Http;

public enum ErrorKind { BadRequest, NotFound, Conflict }

/// <summary>
/// Resultado de uma operação de serviço: sucesso (com payload opcional) ou erro
/// com um "kind" que o endpoint traduz pro status HTTP. Mantém a lógica de negócio
/// fora dos handlers e o mapeamento de erro num lugar só (<see cref="ToHttp"/>).
/// </summary>
public sealed record ServiceResult(object? Value, ErrorKind? Kind, string? Error)
{
    public bool Ok => Kind is null;

    public static ServiceResult Success(object? value = null) => new(value, null, null);
    public static ServiceResult Bad(string message) => new(null, ErrorKind.BadRequest, message);
    public static ServiceResult NotFound(string message) => new(null, ErrorKind.NotFound, message);
    public static ServiceResult Conflict(string message) => new(null, ErrorKind.Conflict, message);
}

public static class ServiceResultExtensions
{
    /// <summary>Traduz um ServiceResult pra resposta HTTP (payload no sucesso; { error } no erro).</summary>
    public static IResult ToHttp(this ServiceResult r)
    {
        if (r.Ok)
            return r.Value is null ? Results.Ok() : Results.Ok(r.Value);
        return r.Kind switch
        {
            ErrorKind.NotFound => ApiError.NotFound(r.Error!),
            ErrorKind.Conflict => ApiError.Conflict(r.Error!),
            _ => ApiError.BadRequest(r.Error!),
        };
    }
}

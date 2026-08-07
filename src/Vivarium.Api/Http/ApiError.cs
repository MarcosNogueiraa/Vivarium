namespace Vivarium.Api.Http;

/// <summary>
/// Respostas de erro padronizadas no formato { "error": "mensagem" } — o mesmo
/// contrato que o cliente (api.js) lê. Centraliza o shape num lugar só.
/// </summary>
public static class ApiError
{
    public static IResult BadRequest(string message) => Results.BadRequest(new { error = message });
    public static IResult NotFound(string message) => Results.NotFound(new { error = message });
    public static IResult Conflict(string message) => Results.Conflict(new { error = message });
    public static IResult Forbidden(string message) => Results.Json(new { error = message }, statusCode: 403);
    public static IResult Unauthorized(string message) => Results.Json(new { error = message }, statusCode: 401);
}

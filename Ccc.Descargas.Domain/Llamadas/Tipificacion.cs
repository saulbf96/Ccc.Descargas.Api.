namespace Ccc.Descargas.Domain.Llamadas;

// elemento de dispositions
public sealed record Tipificacion(
    string Clave,
    long DispId,
    long? ListDispId,
    string? Name,
    string? Description,
    string? Code,
    string? Text,
    string? Type,
    string? Status,
    string? InputType);

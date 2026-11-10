namespace Ccc.Descargas.Domain.Llamadas;

/// <summary>
/// Un elemento del arreglo "dispositions" de CCC:
/// [{"&lt;Clave&gt;": [{"disp_id": ..., "list_disp_id": ..., "name": ..., "text": ..., ...}]}]
/// </summary>
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

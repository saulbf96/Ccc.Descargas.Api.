namespace Ccc.Descargas.Application.Llamadas;

/// <summary>
/// CCC respondió algo distinto de 200 (401 credenciales, 500 error de CCC, etc.).
/// Ocupado = CCC siguió atendiendo otra consulta (code 101) después de todos los reintentos.
/// </summary>
public sealed class CccApiException(int statusCode, string mensaje, bool ocupado = false) : Exception(mensaje)
{
    public int StatusCode { get; } = statusCode;

    public bool Ocupado { get; } = ocupado;
}

/// <summary>
/// No se pudo guardar en la base de datos (sin conexión, timeout, permisos...).
/// </summary>
public sealed class ErrorBaseDatosException(string mensaje, Exception interna) : Exception(mensaje, interna);

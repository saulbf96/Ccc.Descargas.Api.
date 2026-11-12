namespace Ccc.Descargas.Application.Llamadas;

// CCC respondió distinto de 200. Ocupado = code 101
public sealed class CccApiException(int statusCode, string mensaje, bool ocupado = false) : Exception(mensaje)
{
    public int StatusCode { get; } = statusCode;

    public bool Ocupado { get; } = ocupado;
}

// error al guardar en la BD
public sealed class ErrorBaseDatosException(string mensaje, Exception interna) : Exception(mensaje, interna);

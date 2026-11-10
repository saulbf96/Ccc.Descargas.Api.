using System.Data;
using Ccc.Descargas.Application.Llamadas;
using Microsoft.Data.SqlClient;

namespace Ccc.Descargas.Infrastructure.Persistencia;

/// <summary>
/// Llama al SP ÚNICO de una cuenta (dbo.USP_CCC_&lt;cuenta&gt;) con su @OPCION. El nombre del SP sale de la lista
/// fija de cuentas (TablasCuenta), nunca de un texto recibido, y todos los valores van como parámetros tipados.
/// </summary>
internal static class ProcedimientoCuenta
{
    public const byte GuardarLlamadas = 1;
    public const byte GuardarEstatus = 2;
    public const byte GuardarTiempos = 3;
    public const byte RegistrarBitacora = 4;
    public const byte ConsultarAvance = 5;
    public const byte Resumen = 6;

    public static TablaCuenta Tabla(string clientId) =>
        int.TryParse(clientId, out var id) && TablasCuenta.PorCuenta.TryGetValue(id, out var tabla)
            ? tabla
            : throw new InvalidOperationException($"La cuenta {clientId} no tiene tablas en la BD.");

    public static TablaCuenta Tabla(int clientId) => Tabla(clientId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static async Task<T> EjecutarAsync<T>(
        BaseDatosOptions opciones, TablaCuenta tabla, byte opcion, Action<SqlParameterCollection> parametros,
        Func<SqlDataReader, CancellationToken, Task<T>> leer, CancellationToken cancellationToken)
    {
        try
        {
            await using var conexion = new SqlConnection(opciones.CadenaConexion);
            await conexion.OpenAsync(cancellationToken);

            await using var comando = new SqlCommand(tabla.Procedimiento, conexion)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = opciones.TimeoutComandoSegundos,
            };
            comando.Parameters.Add("@OPCION", SqlDbType.TinyInt).Value = opcion;
            parametros(comando.Parameters);

            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            return await leer(lector, cancellationToken);
        }
        catch (SqlException ex)
        {
            throw Traducir(ex);
        }
    }

    /// <summary>Lee la primera fila: (Insertadas, Actualizadas).</summary>
    public static async Task<(int, int)> LeerConteosAsync(SqlDataReader lector, CancellationToken cancellationToken)
    {
        await lector.ReadAsync(cancellationToken);
        return (lector.GetInt32(0), lector.GetInt32(1));
    }

    public static object Nulo(object? valor) => valor ?? DBNull.Value;

    // Application no conoce SqlClient: se traduce a un error de BD con un mensaje claro
    private static ErrorBaseDatosException Traducir(SqlException ex)
    {
        var motivo = ex.Number switch
        {
            -2 => "la BD tardó demasiado en responder (timeout)",
            53 or -1 or 2 or 10054 or 10060 => "no hay conexión con el servidor de BD",
            18456 => "usuario o contraseña de la BD incorrectos",
            229 => "el usuario de la BD no tiene permiso para ejecutar el SP",
            2812 => "no existe el SP de la cuenta: faltan los scripts de la versión 2",
            _ => $"error de SQL Server {ex.Number}: {ex.Message}",
        };
        return new ErrorBaseDatosException(motivo, ex);
    }
}

using System.Data;
using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Application.Llamadas;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using static Ccc.Descargas.Infrastructure.Persistencia.ProcedimientoCuenta;

namespace Ccc.Descargas.Infrastructure.Persistencia;

// bitácora y avance: @OPCION 4 registra, 5 avance, 6 resumen
internal sealed class BitacoraDescargasRepository(IOptions<BaseDatosOptions> options) : IBitacoraDescargas
{
    public Task RegistrarAsync(RegistroTramo r, bool moverAvance, CancellationToken cancellationToken) =>
        EjecutarAsync(options.Value, Tabla(r.ClientId), RegistrarBitacora, p =>
        {
            p.Add("@Tipo", SqlDbType.VarChar, 20).Value = TiposDescarga.Texto(r.Tipo);
            p.Add("@Desde", SqlDbType.DateTime2).Value = r.Desde;
            p.Add("@Hasta", SqlDbType.DateTime2).Value = r.Hasta;
            p.Add("@TamanoTramoSegundos", SqlDbType.Int).Value = Nulo(r.TamanoTramoSegundos);
            p.Add("@Origen", SqlDbType.VarChar, 20).Value = r.Origen == OrigenDescarga.Automatica ? "AUTOMATICA" : "MANUAL";
            p.Add("@Estado", SqlDbType.VarChar, 20).Value = r.Exitosa ? "COMPLETADA" : "ERROR";
            p.Add("@Paginas", SqlDbType.Int).Value = r.Paginas;
            p.Add("@Registros", SqlDbType.Int).Value = r.Registros;
            p.Add("@Insertadas", SqlDbType.Int).Value = r.Insertadas;
            p.Add("@Actualizadas", SqlDbType.Int).Value = r.Actualizadas;
            p.Add("@Inicio", SqlDbType.DateTime2).Value = r.Inicio;
            p.Add("@Fin", SqlDbType.DateTime2).Value = r.Fin;
            p.Add("@MsCcc", SqlDbType.Int).Value = Nulo(r.MsCcc is { } ccc ? (int)Math.Min(ccc, int.MaxValue) : null);
            p.Add("@MsBd", SqlDbType.Int).Value = Nulo(r.MsBd is { } bd ? (int)Math.Min(bd, int.MaxValue) : null);
            p.Add("@Mensaje", SqlDbType.NVarChar, 1000).Value = Nulo(r.Mensaje);
            p.Add("@Servidor", SqlDbType.NVarChar, 100).Value = Environment.MachineName;
            p.Add("@MoverAvance", SqlDbType.Bit).Value = moverAvance;
        }, async (lector, ct) => await lector.ReadAsync(ct), cancellationToken);

    public Task<AvanceDescarga?> ObtenerAvanceAsync(string clientId, TipoDescarga tipo, CancellationToken cancellationToken) =>
        EjecutarAsync(options.Value, Tabla(clientId), ConsultarAvance,
            p => p.Add("@Tipo", SqlDbType.VarChar, 20).Value = TiposDescarga.Texto(tipo),
            async (lector, ct) => await lector.ReadAsync(ct)
                ? new AvanceDescarga(
                    lector.GetDateTime(0),
                    lector.IsDBNull(1) ? null : lector.GetInt32(1),
                    lector.GetInt32(2),
                    lector.IsDBNull(3) ? null : lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetDateTime(4))
                : null,
            cancellationToken);

    public Task<ResumenCuenta> ObtenerResumenAsync(string clientId, DateTime desde, DateTime hasta, CancellationToken cancellationToken) =>
        EjecutarAsync(options.Value, Tabla(clientId), Resumen, p =>
        {
            p.Add("@Desde", SqlDbType.DateTime2).Value = desde;
            p.Add("@Hasta", SqlDbType.DateTime2).Value = hasta;
        }, async (lector, ct) =>
        {
            var tipos = new List<ResumenTipo>();
            while (await lector.ReadAsync(ct))
            {
                tipos.Add(new ResumenTipo(
                    TiposDescarga.Desde(lector.GetString(0)),
                    lector.GetInt32(1),
                    lector.GetInt32(2),
                    Convert.ToInt64(lector.GetValue(3)),
                    lector.IsDBNull(4) ? null : lector.GetDateTime(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5)));
            }

            var avance = new Dictionary<TipoDescarga, AvanceDescarga>();
            await lector.NextResultAsync(ct);
            while (await lector.ReadAsync(ct))
            {
                avance[TiposDescarga.Desde(lector.GetString(0))] = new AvanceDescarga(
                    lector.GetDateTime(1), null, lector.GetInt32(2), lector.IsDBNull(3) ? null : lector.GetString(3), null);
            }

            return new ResumenCuenta(tipos, avance);
        }, cancellationToken);
}

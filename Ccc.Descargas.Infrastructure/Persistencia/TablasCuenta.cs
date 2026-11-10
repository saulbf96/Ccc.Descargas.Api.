// ARCHIVO GENERADO por CCC\TEST\generador_scripts\generar.py: no editar a mano (se sobrescribe).
using System.Collections.Frozen;

namespace Ccc.Descargas.Infrastructure.Persistencia;

/// <summary>
/// Objetos de BD de una cuenta: su SP único dbo.USP_CCC_&lt;Sufijo&gt; (todo va por @OPCION), el tipo de sus
/// llamadas dbo.TT_LLAMADAS_CCC_&lt;Sufijo&gt; y los ref_str_* que tienen columna en su tabla de llamadas,
/// con el mismo nombre que manda CCC y en el MISMO orden que su tipo tabla.
/// </summary>
internal sealed record TablaCuenta(string Sufijo, IReadOnlyList<string> ColumnasLead)
{
    public string Procedimiento => $"dbo.USP_CCC_{Sufijo}";

    public string TipoLlamadas => $"dbo.TT_LLAMADAS_CCC_{Sufijo}";

    public const string TipoEstatus = "dbo.TT_ESTATUS_AGENTES_CCC";

    public const string TipoTiempos = "dbo.TT_TIEMPOS_AGENTES_CCC";
}

/// <summary>
/// Cada cuenta CCC tiene sus propias tablas. Un ref_str_ que no esté en la lista de su cuenta se guarda en
/// CAMPOS_EXTRA. Para darle columna: agregarlo en generar.py y regenerar. Una cuenta que no esté aquí no
/// tiene tablas y no se puede guardar.
/// </summary>
internal static class TablasCuenta
{
    public static readonly FrozenDictionary<int, TablaCuenta> PorCuenta = new Dictionary<int, TablaCuenta>
    {
        [2520] = new("BAZ_SEM_2520",
            [
                "ref_str_IDUNICO",
                "ref_str_ID_CAMPANIA",
                "ref_str_LINK_BAZ",
                "ref_str_LINK_EDITABLE",
                "ref_str_NOMBRE",
                "ref_str_NOMBRE_CAMPAA",
                "ref_str_NOMBRE_PILA",
                "ref_str_PAGO_REQUERIDO",
                "ref_str_PREFIJO",
                "ref_str_SALDO",
                "ref_str_TELEFONO1",
                "ref_str_TEXTO_1",
                "ref_str_TIPO_TELEFONO1",
            ]),
        [2586] = new("BAZ_MOV_ESPECIAL_2586",
            [
                "ref_str_ATRASO_MAXIMO",
                "ref_str_CATEGO",
                "ref_str_CLASIFICACION_CTE",
                "ref_str_CLIENTE_UNICO",
                "ref_str_D",
                "ref_str_DIAS_ATRASO",
                "ref_str_DIQUE",
                "ref_str_ESTATUS_PLAN",
                "ref_str_FECHA_ULTIMO_PAGO",
                "ref_str_FIDIAPAGO",
                "ref_str_IMP_ULTIMO_PAGO",
                "ref_str_NOMBRE_CTE",
                "ref_str_PAGO_NORMAL",
                "ref_str_PLATAFORMA",
                "ref_str_PREFIJO",
                "ref_str_PRODUCTO",
                "ref_str_SALDO_REQUERIDO",
                "ref_str_SALDO_TOTAL",
                "ref_str_TEL",
                "ref_str_TELEFONO1",
                "ref_str_TEXTO",
            ]),
        [2812] = new("CONSU_2812",
            [
                "ref_str_ACCOUNTID",
                "ref_str_AO_PENSION",
                "ref_str_ARQUETIPOS_IMSS",
                "ref_str_CLV",
                "ref_str_CONVENIO",
                "ref_str_COTIZADOR",
                "ref_str_CP",
                "ref_str_CURP",
                "ref_str_CURP_1",
                "ref_str_DESCUENTO_OFERTA",
                "ref_str_ENTIDAD",
                "ref_str_GNERO",
                "ref_str_ID_OFERTA",
                "ref_str_INICIATIVA",
                "ref_str_INTERESADO",
                "ref_str_MES_PENSION",
                "ref_str_MONTO_BRUTO",
                "ref_str_MONTO_LIQUIDACIN",
                "ref_str_MONTO_NETO",
                "ref_str_MUNICIPIO",
                "ref_str_NIVEL_PROPENSIN_A_LA_RETENCION_6",
                "ref_str_NIVEL_RECUPERACION",
                "ref_str_NOMATRICULA",
                "ref_str_NOMBRE",
                "ref_str_NSS",
                "ref_str_OCUPACIN",
                "ref_str_OCUPACION",
                "ref_str_PIRA",
                "ref_str_PLAZO",
                "ref_str_PLAZO_2",
                "ref_str_PROPENCION_DE_COMPRA",
                "ref_str_PROPENSIN_DE_COMPRA",
                "ref_str_PROPENSION_DE_FUGA",
                "ref_str_RFC",
                "ref_str_RFCX",
                "ref_str_RFC_1",
                "ref_str_RFC_ANONIMIZADO",
                "ref_str_TASA",
                "ref_str_TASA_ACTUAL",
                "ref_str_TELEFONO_1",
                "ref_str_TELEFONO_2",
                "ref_str_TELFONO1",
                "ref_str_TELFONO_2",
                "ref_str_TEL_1",
                "ref_str_TEL_1_",
                "ref_str_TEL_2",
                "ref_str_TEL_3",
                "ref_str_TEL_4",
                "ref_str_TEL_5",
                "ref_str_TIPO_1",
                "ref_str_TIPO_2",
                "ref_str_TIPO_3",
                "ref_str_TIPO_4",
                "ref_str_TIPO_5",
                "ref_str_TIPO_DE_CREDITO",
                "ref_str_TIPO_PENSIN",
                "ref_str_TRANSCURRIDO",
                "ref_str_VALOR_DEL_CLIENTE",
            ]),
        [3622] = new("MULTICAMPANAS_3622",
            [
            ]),
        [200928] = new("ISB_CDMX_200928",
            [
                "ref_str_ATRASO_MAXIMO",
                "ref_str_CLASIFICACION_CTE",
                "ref_str_CLIENTE_UNICO",
                "ref_str_DIAS_ATRASO",
                "ref_str_DIQUE",
                "ref_str_FIDIAPAGO",
                "ref_str_NOMBRE_CTE",
                "ref_str_ORIGEN",
                "ref_str_PAGO_NORMAL",
                "ref_str_PREFIJO",
                "ref_str_PRODUCTO",
                "ref_str_SALDO_REQUERIDO",
                "ref_str_SALDO_TOTAL",
                "ref_str_TELEFONO1",
                "ref_str_TEXTO",
            ]),
        [200929] = new("ISB_CDMX_200929",
            [
                "ref_str_ATRASO_MAXIMO",
                "ref_str_CAMPANIA",
                "ref_str_CATEGO",
                "ref_str_CLASIFICACION_CTE",
                "ref_str_CLIENTE_UNICO",
                "ref_str_ESTRATEGIA",
                "ref_str_FECHA_ULTIMO_PAGO",
                "ref_str_FIDIAPAGO",
                "ref_str_ID",
                "ref_str_MENSAJE",
                "ref_str_NOMBRE_CTE",
                "ref_str_NOMBRE_DESPACHO_",
                "ref_str_PRED",
                "ref_str_PRODUCTO",
                "ref_str_PROMESA_VIGENTE",
                "ref_str_REFERENCIA",
                "ref_str_SALDO_TOTAL",
                "ref_str_SMS",
                "ref_str_SUBTIPO",
                "ref_str_SUB_TIPO",
                "ref_str_TEL",
                "ref_str_TERRITORIO",
                "ref_str_TERRITORIO_2",
                "ref_str_TIPO_ASIG",
                "ref_str_ZONA",
                "ref_str_ZONA_2",
            ]),
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, FrozenSet<string>> Columnas =
        PorCuenta.ToFrozenDictionary(c => c.Key, c => c.Value.ColumnasLead.ToFrozenSet(StringComparer.Ordinal));

    public static bool TieneColumna(int clientId, string campo) =>
        Columnas.TryGetValue(clientId, out var campos) && campos.Contains(campo);
}

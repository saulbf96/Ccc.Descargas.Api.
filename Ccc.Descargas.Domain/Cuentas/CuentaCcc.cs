namespace Ccc.Descargas.Domain.Cuentas;

//Una cuenta(instancia) de CCC de la que se descargan llamadas.
//Plataforma = servidor de CCC (api.ccc.uno = CCC1, app2.ccc.uno = CCC2): cada plataforma atiende
//una sola consulta de CallHistory a la vez, pero las dos plataformas pueden trabajar en paralelo.
public sealed record CuentaCcc(string ClientId, string Nombre, string Plataforma);

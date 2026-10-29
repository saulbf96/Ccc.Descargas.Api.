

namespace Ccc.Descargas.Application.Cuentas;


public sealed record ResultadoPing(

    string ClientId,
    string Nombre,
    bool Exitoso,
    int? StatusCode,
    string Mensaje,
    long DuracionMs

    );




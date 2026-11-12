using System.Text.Json.Serialization;
using Ccc.Descargas.Api.Seguridad;
using Ccc.Descargas.Api.Trabajos;
using Ccc.Descargas.Application;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// en el servidor corre como servicio de Windows
builder.Host.UseWindowsService(opciones => opciones.ServiceName = "CccDescargas");

// Add services to the container.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// sin ApiKey no arranca
builder.Services.AddOptions<SeguridadOptions>()
    .Bind(builder.Configuration.GetSection(SeguridadOptions.Seccion))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IPostConfigureOptions<SeguridadOptions>, DescifrarApiKey>();

// descarga automática
builder.Services.AddOptions<DescargaOptions>()
    .Bind(builder.Configuration.GetSection(DescargaOptions.Seccion))
    .ValidateDataAnnotations()
    .Validate(o => o.TramoMinimoSegundos <= o.TramoInicialSegundos && o.TramoInicialSegundos <= o.TramoMaximoSegundos,
        "DescargaAutomatica: debe cumplirse TramoMinimoSegundos <= TramoInicialSegundos <= TramoMaximoSegundos.")
    .ValidateOnStart();
builder.Services.AddHostedService<DescargaAutomaticaWorker>();

// correos de inicio y fin del día
builder.Services.AddOptions<ResumenDiarioOptions>()
    .Bind(builder.Configuration.GetSection(ResumenDiarioOptions.Seccion))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<ResumenDiarioWorker>();

builder.Services.AddControllers(opciones => opciones.Filters.Add<ApiKeyFilter>())
    .AddJsonOptions(opciones =>
        // enums como texto
        opciones.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(opciones => opciones.AddDocumentTransformer((documento, _, _) =>
{
    // X-Api-Key en Swagger
    documento.Components ??= new OpenApiComponents();
    documento.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    documento.Components.SecuritySchemes[ApiKeyFilter.Encabezado] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ApiKeyFilter.Encabezado,
        Description = "Clave de la API (Seguridad:ApiKey)",
    };
    documento.Security ??= [];
    documento.Security.Add(new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(ApiKeyFilter.Encabezado, documento)] = [],
    });
    return Task.CompletedTask;
}));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(opciones => opciones.SwaggerEndpoint("/openapi/v1.json", "Ccc.Descargas API v1"));
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

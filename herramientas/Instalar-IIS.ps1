<#
  Publica Ccc.Descargas.Api en IIS en un solo paso:
    1. Pide las 6 SecretKey de CCC y la cadena de conexión (ocultas) y genera la ApiKey.
    2. Cifra los 3 tipos de secreto con DPAPI de ESTE servidor (en otra máquina no se pueden descifrar).
    3. Crea/actualiza el App Pool "CccDescargas" para que la descarga de cada hora NUNCA se duerma:
       sin código administrado, AlwaysRunning, inactividad 0, sin reciclaje por tiempo, sin reciclaje superpuesto.
    4. Guarda los secretos cifrados como variables del App Pool.
    5. Crea el sitio con precarga, da permiso de lectura a la carpeta, arranca y prueba el ping.

  Requisitos en el servidor (una vez): ASP.NET Core Hosting Bundle .NET 10 e "Inicialización de aplicaciones".

  Uso, en el servidor, en PowerShell COMO ADMINISTRADOR, desde la carpeta publicada:
    .\Instalar-IIS.ps1
  Opciones: -Carpeta "C:\inetpub\CccDescargas"  -Puerto 8092  -Ip "*" (por defecto solo 127.0.0.1)
            -ArchivoCredenciales ".\credenciales.ps1": toma las claves de ese archivo en lugar de preguntarlas
             y LO BORRA en cuanto las cifra (para que no quede ningún secreto en texto plano en el servidor).

  Para publicar una versión nueva NO hace falta: Stop-WebAppPool CccDescargas -> reemplazar archivos -> Start-WebAppPool CccDescargas
#>
#Requires -RunAsAdministrator
param(
    [string]$Carpeta = $PSScriptRoot,
    [string]$Nombre = "CccDescargas",
    [int]$Puerto = 8092,
    # 127.0.0.1 = solo se puede llamar desde el mismo servidor. "*" = desde la red (la ApiKey sigue protegiendo).
    [string]$Ip = "127.0.0.1",
    # Reportes .txt de las descargas (fuera del sitio: no se borran al publicar una versión nueva)
    [string]$CarpetaReportes = "C:\CccDescargas\reportes",
    [string[]]$Cuentas = @("200929", "200928", "2812", "3622", "2586", "2520"),
    # Opcional: archivo con $Credenciales (SecretKeys, CadenaConexion, Correo). Se borra al cifrar.
    [string]$ArchivoCredenciales
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Security
Import-Module WebAdministration

# ---------------------------------------------------------------- funciones
function Leer-Secreto([string]$mensaje) {
    $seguro = Read-Host $mensaje -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($seguro)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Leer-ConDefecto([string]$mensaje, [string]$defecto) {
    $valor = Read-Host "$mensaje [$defecto]"
    if ([string]::IsNullOrWhiteSpace($valor)) { return $defecto } else { return $valor.Trim() }
}

function Cifrar([string]$texto) {
    $entropia = [Text.Encoding]::UTF8.GetBytes("Ccc.Descargas")   # debe ser igual a la de ProteccionDpapi.cs
    $cifrado = [Security.Cryptography.ProtectedData]::Protect(
        [Text.Encoding]::UTF8.GetBytes($texto), $entropia,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
    return "dpapi:" + [Convert]::ToBase64String($cifrado)
}

# ---------------------------------------------------------------- validaciones
if (-not (Get-ChildItem -Path $Carpeta -Filter "Ccc.Descargas.Api*.dll" | Select-Object -First 1)) {
    throw "No se encontró Ccc.Descargas.Api*.dll en '$Carpeta'. Usa -Carpeta con la carpeta publicada."
}
# El módulo de IIS puede existir de una versión anterior: también se revisa que esté el runtime .NET 10
if (-not (Test-Path "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll") -or
    -not (Get-ChildItem "$env:ProgramFiles\dotnet\shared\Microsoft.AspNetCore.App" -Directory -Filter "10.*" -ErrorAction SilentlyContinue)) {
    throw "Falta el ASP.NET Core Hosting Bundle .NET 10. Instálalo, ejecuta 'iisreset' y vuelve a correr este script."
}
if ((Get-Command Get-WindowsFeature -ErrorAction SilentlyContinue) -and -not (Get-WindowsFeature Web-AppInit).Installed) {
    Write-Warning "Falta la característica 'Inicialización de aplicaciones' (Web-AppInit): sin ella la app no arranca sola al reiniciar IIS."
    Write-Warning "Instálala con: Install-WindowsFeature Web-AppInit"
}

# ---------------------------------------------------------------- credenciales desde archivo (opcional)
$Credenciales = $null
if ($ArchivoCredenciales) {
    if (-not (Test-Path $ArchivoCredenciales)) { throw "No se encontró el archivo de credenciales '$ArchivoCredenciales'." }
    . (Resolve-Path $ArchivoCredenciales)   # define $Credenciales
    if (-not $Credenciales) { throw "El archivo '$ArchivoCredenciales' no define `$Credenciales." }
    foreach ($cuenta in $Cuentas) {
        if ([string]::IsNullOrWhiteSpace($Credenciales.SecretKeys[$cuenta])) { throw "Falta la SecretKey de la cuenta $cuenta en el archivo." }
    }
    if ([string]::IsNullOrWhiteSpace($Credenciales.CadenaConexion)) { throw "Falta CadenaConexion en el archivo." }
    Write-Host "Credenciales tomadas de '$ArchivoCredenciales'."
}

# ---------------------------------------------------------------- secretos (se cifran al momento)
Write-Host ""
$variables = [ordered]@{}
if (-not $Credenciales) { Write-Host "== SecretKey de CCC (no se muestran en pantalla) ==" }
foreach ($cuenta in $Cuentas) {
    $clave = if ($Credenciales) { $Credenciales.SecretKeys[$cuenta] } else { Leer-Secreto "SecretKey de la cuenta $cuenta" }
    $variables["Ccc__Cuentas__${cuenta}__SecretKey"] = Cifrar $clave
}

if ($Credenciales) {
    $variables["BaseDatos__CadenaConexion"] = Cifrar $Credenciales.CadenaConexion
}
else {
    Write-Host ""
    Write-Host "== Base de datos =="
    Write-Host "Ejemplo: Server=10.80.5.205;Database=DESCARGAS;User Id=Desarrollo;Password=...;TrustServerCertificate=True"
    $variables["BaseDatos__CadenaConexion"] = Cifrar (Leer-Secreto "Cadena de conexión")
}

# ApiKey aleatoria de 64 caracteres; se muestra UNA vez para guardarla
$bytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$apiKey = [Convert]::ToBase64String($bytes)
$variables["Seguridad__ApiKey"] = Cifrar $apiKey

# Reportes .txt (una carpeta por cuenta, un archivo por día): FUERA del sitio para que no se borren al publicar
$variables["Reportes__Carpeta"] = $CarpetaReportes
New-Item -ItemType Directory -Force -Path $CarpetaReportes | Out-Null

# Correo de avisos (opcional): cuando una descarga falla, la app se detiene o se recuperan horas
Write-Host ""
if ($Credenciales -and $Credenciales.Correo) {
    $c = $Credenciales.Correo
    $variables["Correo__Habilitado"] = "true"
    $variables["Correo__Servidor"]   = $c.Servidor
    $variables["Correo__Puerto"]     = [string]$c.Puerto
    $variables["Correo__Seguridad"]  = $c.Seguridad
    $variables["Correo__Usuario"]    = $c.Usuario
    $variables["Correo__Contrasena"] = Cifrar $c.Contrasena
    $variables["Correo__De"]         = $c.De
    for ($i = 0; $i -lt @($c.Para).Count; $i++) { $variables["Correo__Para__$i"] = @($c.Para)[$i] }
    Write-Host "Correo de avisos: $($c.Servidor):$($c.Puerto) -> $(@($c.Para) -join ', ')"
}
elseif (-not $Credenciales -and (Read-Host "¿Configurar correo de avisos? (S/N)") -match '^[sS]') {
    Write-Host "Enter = usar el valor sugerido entre [ ]. El correo de 1csb.com.mx está en uservers.net."
    $variables["Correo__Habilitado"] = "true"
    $variables["Correo__Servidor"]   = Leer-ConDefecto "Servidor SMTP" "smtp.uservers.net"
    $variables["Correo__Puerto"]     = Leer-ConDefecto "Puerto (587, 465 o 25)" "587"
    $variables["Correo__Seguridad"]  = Leer-ConDefecto "Seguridad (StartTls, SslOnConnect o Ninguna)" "StartTls"
    $variables["Correo__Usuario"]    = Leer-ConDefecto "Usuario SMTP (cuenta que envía)" "desarrollouno@1csb.com.mx"
    if ($variables["Correo__Usuario"]) {
        $variables["Correo__Contrasena"] = Cifrar (Leer-Secreto "Contraseña SMTP")
    }
    $variables["Correo__De"] = Leer-ConDefecto "Correo remitente (De)" "desarrollouno@1csb.com.mx"
    $destinos = (Leer-ConDefecto "Correo(s) destino, separados por coma" "desarrollouno@1csb.com.mx") -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    for ($i = 0; $i -lt $destinos.Count; $i++) { $variables["Correo__Para__$i"] = $destinos[$i] }
}

# Ya todo está cifrado: el archivo de credenciales en texto plano se borra
if ($ArchivoCredenciales) {
    Remove-Item -Path $ArchivoCredenciales -Force
    $Credenciales = $null
    Write-Host "Archivo de credenciales borrado: en el servidor solo quedan los valores cifrados."
}

# ---------------------------------------------------------------- App Pool
$rutaPool = "IIS:\AppPools\$Nombre"
if (-not (Test-Path $rutaPool)) { New-WebAppPool -Name $Nombre | Out-Null }
Set-ItemProperty $rutaPool -Name managedRuntimeVersion -Value ""                         # Sin código administrado
Set-ItemProperty $rutaPool -Name startMode -Value "AlwaysRunning"                        # arranca con IIS
Set-ItemProperty $rutaPool -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)      # nunca se duerme
Set-ItemProperty $rutaPool -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero) # sin reciclaje cada 29 h
Set-ItemProperty $rutaPool -Name recycling.disallowOverlappingRotation -Value $true     # nunca 2 copias descargando a la vez

# Variables del App Pool (se reemplazan si ya existían)
$filtro = "system.applicationHost/applicationPools/add[@name='$Nombre']/environmentVariables"
foreach ($nombreVariable in $variables.Keys) {
    Remove-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $filtro -Name "." `
        -AtElement @{ name = $nombreVariable } -ErrorAction SilentlyContinue
    Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $filtro -Name "." `
        -Value @{ name = $nombreVariable; value = $variables[$nombreVariable] }
}

# ---------------------------------------------------------------- sitio
$rutaSitio = "IIS:\Sites\$Nombre"
if (-not (Test-Path $rutaSitio)) {
    New-Website -Name $Nombre -PhysicalPath $Carpeta -ApplicationPool $Nombre -Port $Puerto -IPAddress $Ip | Out-Null
}
Set-ItemProperty $rutaSitio -Name applicationDefaults.preloadEnabled -Value $true   # arranca sin esperar una petición

# La identidad del App Pool solo necesita leer y ejecutar la carpeta del sitio, y escribir en la de reportes
icacls $Carpeta /grant "IIS AppPool\${Nombre}:(OI)(CI)RX" /T /Q | Out-Null
icacls $CarpetaReportes /grant "IIS AppPool\${Nombre}:(OI)(CI)M" /T /Q | Out-Null

Restart-WebAppPool -Name $Nombre
Start-Website -Name $Nombre -ErrorAction SilentlyContinue
Write-Host ""
Write-Host "Sitio '$Nombre' publicado. Probando..."
Start-Sleep -Seconds 10

# ---------------------------------------------------------------- prueba
$host_ = if ($Ip -eq "*") { "localhost" } else { $Ip }
try {
    $ping = Invoke-RestMethod -Uri "http://${host_}:$Puerto/api/cuentas/ping" -Headers @{ "X-Api-Key" = $apiKey } -TimeoutSec 600
    $ping | Format-Table clientId, nombre, exitoso, statusCode -AutoSize
}
catch {
    Write-Warning "La prueba de ping falló: $($_.Exception.Message)"
    Write-Warning "Revisa el Visor de eventos -> Registros de Windows -> Aplicación (origen 'IIS AspNetCore Module V2' o '.NET Runtime')."
}

Write-Host ""
Write-Host "=================================================================="
Write-Host " GUARDA ESTA ApiKey en un lugar seguro (no se vuelve a mostrar):"
Write-Host " $apiKey"
Write-Host " Se usa en el encabezado X-Api-Key para pedir horas a mano."
Write-Host " (También quedó copiada en el portapapeles: Ctrl+V para pegarla.)"
Write-Host "=================================================================="
Set-Clipboard -Value $apiKey -ErrorAction SilentlyContinue
$apiKey = $null

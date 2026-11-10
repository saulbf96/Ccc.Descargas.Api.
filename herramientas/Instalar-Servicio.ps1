<#
  Instala Ccc.Descargas.Api como Servicio de Windows (sin IIS). Hace todo en un paso:
    1. Pide las 6 SecretKey de CCC y la cadena de conexión (ocultas) y genera la ApiKey.
    2. Cifra los 3 tipos de secreto con DPAPI de ESTE servidor (en otra máquina no se pueden descifrar).
    3. Crea el servicio "CccDescargas": inicio automático, cuenta NetworkService, se reinicia solo si falla.
    4. Lo arranca y prueba el ping a las 6 cuentas.

  Uso, en el servidor, en PowerShell COMO ADMINISTRADOR, desde la carpeta publicada:
    .\Instalar-Servicio.ps1
  o indicando la carpeta:
    .\Instalar-Servicio.ps1 -Carpeta "C:\Servicios\CccDescargas"

  Solo para la PRIMERA instalación (o para reinstalar desde cero). Para publicar una versión nueva:
    Stop-Service CccDescargas  ->  reemplazar los archivos  ->  Start-Service CccDescargas
#>
#Requires -RunAsAdministrator
param(
    [string]$Carpeta = $PSScriptRoot,
    [string]$NombreServicio = "CccDescargas",
    # Solo escucha DENTRO del servidor: nadie desde la red puede llamar a la API
    [string]$Url = "http://localhost:5239",
    [string]$CarpetaReportes = "C:\CccDescargas\reportes",
    [string[]]$Cuentas = @("200929", "200928", "2812", "3622", "2586", "2520")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Security

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
$exe = Get-ChildItem -Path $Carpeta -Filter "Ccc.Descargas.Api*.exe" | Select-Object -First 1
if (-not $exe) {
    throw "No se encontró Ccc.Descargas.Api*.exe en '$Carpeta'. Usa -Carpeta con la carpeta publicada."
}

if (Get-Service -Name $NombreServicio -ErrorAction SilentlyContinue) {
    $respuesta = Read-Host "El servicio '$NombreServicio' ya existe. ¿Borrarlo e instalarlo de nuevo? (S/N)"
    if ($respuesta -notmatch '^[sS]') { Write-Host "Cancelado."; return }
    Stop-Service -Name $NombreServicio -ErrorAction SilentlyContinue
    sc.exe delete $NombreServicio | Out-Null
    Start-Sleep -Seconds 2
}

# ---------------------------------------------------------------- secretos (se cifran al momento)
Write-Host ""
Write-Host "== SecretKey de CCC (no se muestran en pantalla) =="
$variables = @(
    "ASPNETCORE_URLS=$Url",
    "Logging__EventLog__SourceName=$NombreServicio"
)
foreach ($cuenta in $Cuentas) {
    $variables += "Ccc__Cuentas__${cuenta}__SecretKey=" + (Cifrar (Leer-Secreto "SecretKey de la cuenta $cuenta"))
}

Write-Host ""
Write-Host "== Base de datos =="
Write-Host "Ejemplo: Server=10.80.5.205;Database=DESCARGAS;User Id=Desarrollo;Password=...;TrustServerCertificate=True"
$variables += "BaseDatos__CadenaConexion=" + (Cifrar (Leer-Secreto "Cadena de conexión"))

# ApiKey aleatoria de 64 caracteres; se muestra UNA vez para guardarla
$bytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$apiKey = [Convert]::ToBase64String($bytes)
$variables += "Seguridad__ApiKey=" + (Cifrar $apiKey)

# Reportes .txt (una carpeta por cuenta, un archivo por día): fuera de la carpeta de la app
$variables += "Reportes__Carpeta=$CarpetaReportes"
New-Item -ItemType Directory -Force -Path $CarpetaReportes | Out-Null
icacls $CarpetaReportes /grant "NT AUTHORITY\NetworkService:(OI)(CI)M" /T /Q | Out-Null

# Correo de avisos (opcional): cuando una descarga falla, la app se detiene o se recuperan horas
Write-Host ""
$respuesta = Read-Host "¿Configurar correo de avisos? (S/N)"
if ($respuesta -match '^[sS]') {
    Write-Host "Enter = usar el valor sugerido entre [ ]. El correo de 1csb.com.mx está en uservers.net."
    $variables += "Correo__Habilitado=true"
    $variables += "Correo__Servidor=" + (Leer-ConDefecto "Servidor SMTP" "smtp.uservers.net")
    $variables += "Correo__Puerto=" + (Leer-ConDefecto "Puerto (587, 465 o 25)" "587")
    $variables += "Correo__Seguridad=" + (Leer-ConDefecto "Seguridad (StartTls, SslOnConnect o Ninguna)" "StartTls")
    $usuario = Leer-ConDefecto "Usuario SMTP (cuenta que envía)" "desarrollouno@1csb.com.mx"
    if ($usuario) {
        $variables += "Correo__Usuario=$usuario"
        $variables += "Correo__Contrasena=" + (Cifrar (Leer-Secreto "Contraseña SMTP"))
    }
    $variables += "Correo__De=" + (Leer-ConDefecto "Correo remitente (De)" "desarrollouno@1csb.com.mx")
    $destinos = (Leer-ConDefecto "Correo(s) destino, separados por coma" "desarrollouno@1csb.com.mx") -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    for ($i = 0; $i -lt $destinos.Count; $i++) { $variables += "Correo__Para__$i=$($destinos[$i])" }
}

# ---------------------------------------------------------------- servicio
# Origen del Visor de eventos: el servicio no tiene permiso de crearlo, se crea aquí
if (-not [Diagnostics.EventLog]::SourceExists($NombreServicio)) {
    New-EventLog -LogName Application -Source $NombreServicio
}

# NetworkService: permisos mínimos (no es administrador); DPAPI de máquina sí lo puede descifrar
$credencial = New-Object Management.Automation.PSCredential("NT AUTHORITY\NetworkService", (New-Object Security.SecureString))
New-Service -Name $NombreServicio `
    -BinaryPathName "`"$($exe.FullName)`"" `
    -DisplayName "CCC - Descarga de llamadas" `
    -Description "Descarga cada hora las llamadas de CCC y las guarda en dbo.TBL_LLAMADAS_CCC_*." `
    -StartupType Automatic `
    -Credential $credencial | Out-Null

# Variables de entorno SOLO de este servicio (no del servidor completo)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$NombreServicio" -Name "Environment" -Type MultiString -Value $variables

# Arranque retrasado (espera a que haya red) y reinicio automático si el proceso truena
sc.exe config $NombreServicio start= delayed-auto | Out-Null
sc.exe failure $NombreServicio reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

Start-Service -Name $NombreServicio
Write-Host ""
Write-Host "Servicio '$NombreServicio' instalado y arrancado. Probando..."
Start-Sleep -Seconds 10

# ---------------------------------------------------------------- prueba
try {
    $ping = Invoke-RestMethod -Uri "$Url/api/cuentas/ping" -Headers @{ "X-Api-Key" = $apiKey } -TimeoutSec 600
    $ping | Format-Table clientId, nombre, exitoso, statusCode -AutoSize
}
catch {
    Write-Warning "La prueba de ping falló: $($_.Exception.Message)"
    Write-Warning "Revisa el Visor de eventos -> Registros de Windows -> Aplicación -> origen '$NombreServicio'."
}

Write-Host ""
Write-Host "=================================================================="
Write-Host " GUARDA ESTA ApiKey en un lugar seguro (no se vuelve a mostrar):"
Write-Host " $apiKey"
Write-Host " Se usa en el encabezado X-Api-Key para pedir horas a mano."
Write-Host "=================================================================="
$apiKey = $null

<#
  Cifra una SecretKey de CCC con DPAPI para ESTE servidor.
  Ejecutar EN el servidor IIS donde corre la API (en otra máquina no se podrá descifrar).
  La clave se pide oculta, así no queda en el historial de PowerShell.
#>
Add-Type -AssemblyName System.Security

$clientId = Read-Host "ClientId de la cuenta (ej. 2812)"
$seguro   = Read-Host "SecretKey" -AsSecureString
$plano    = [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
                [Runtime.InteropServices.Marshal]::SecureStringToBSTR($seguro))

$entropia = [Text.Encoding]::UTF8.GetBytes("Ccc.Descargas")   # debe ser igual a la del código
$cifrado  = [Security.Cryptography.ProtectedData]::Protect(
                [Text.Encoding]::UTF8.GetBytes($plano), $entropia,
                [Security.Cryptography.DataProtectionScope]::LocalMachine)
$plano = $null

Write-Host ""
Write-Host "Variable: Ccc__Cuentas__${clientId}__SecretKey"
Write-Host ("Valor:    dpapi:" + [Convert]::ToBase64String($cifrado))

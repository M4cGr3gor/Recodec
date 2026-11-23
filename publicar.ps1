# Compila Recodec y lo instala en C:\Program Files\Recodec.
# Solo sustituye Recodec.exe: la carpeta ffmpeg y Recodec.settings.json (perfiles, teles, velocidades aprendidas) no se tocan.
# Uso:  powershell -ExecutionPolicy Bypass -File publicar.ps1   [-Destino "otra carpeta"] [-SoloCompilar]
param(
    [string]$Destino = (Join-Path $env:ProgramFiles 'Recodec'),
    [switch]$SoloCompilar
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$pub = Join-Path $PSScriptRoot 'publicado'
dotnet publish src\Recodec -c Release -o $pub -nologo
if ($LASTEXITCODE -ne 0) { throw 'La compilación ha fallado.' }
Write-Host "Compilado: $pub\Recodec.exe"
if ($SoloCompilar) { return }

if (Get-Process Recodec -ErrorAction SilentlyContinue) { throw 'Recodec está abierto: ciérralo y vuelve a ejecutar este script.' }
New-Item -ItemType Directory -Force $Destino | Out-Null
Copy-Item (Join-Path $pub 'Recodec.exe') $Destino -Force
if (-not (Test-Path (Join-Path $Destino 'ffmpeg\ffmpeg.exe'))) {
    Write-Warning "En $Destino no hay carpeta ffmpeg. Recodec la necesita: ábrelo y usa Configuración > FFmpeg > Buscar ahora, o copia ffmpeg.exe y ffprobe.exe en $Destino\ffmpeg."
}
Write-Host "Instalado en $Destino"

# Recodec

Conversor de vídeo para Windows basado en [FFmpeg](https://ffmpeg.org), con interfaz en español.
Pensado para preparar películas y series para verlas en la tele: elige la configuración adecuada
para cada archivo y explica cada opción con una ayuda integrada.

## Funciones

- **Recodificación con la gráfica** (NVIDIA NVENC, Intel Quick Sync, AMD AMF) o con el procesador, a HEVC, H.264, AV1 y otros. Detecta qué codificadores funcionan de verdad en el equipo.
- **Copia sin recodificar** cuando basta con cambiar de contenedor (p. ej. MKV → MP4).
- **Vídeo**: recorte automático de bandas negras, cambio de resolución (también con resoluciones poco habituales y vídeos girados), reducción de ruido, conservación de HDR.
- **Audio**: selección y orden de pistas, conversión de formato, **modo noche** (diálogos más claros y explosiones menos fuertes), normalización de volumen, añadir un audio externo y **sincronizarlo** con una prueba de 30 segundos.
- **Subtítulos**: distingue los subtítulos **forzados** (solo algunas frases) de los completos, los marca correctamente o los incrusta en la imagen.
- **Teles por DLNA**: busca la tele en la red, consulta los formatos que admite, avisa si un vídeo no se va a reproducir y propone los cambios, que solo se aplican si el usuario los acepta.
- **Perfiles** integrados y propios, **cola** de conversiones, ventana de progreso con estimación de tiempo que aprende de las conversiones anteriores, y opción de suspender o apagar el equipo al terminar.
- **Actualizador de FFmpeg** integrado, con comprobación SHA-256.

## Requisitos

- Windows 10 u 11 (64 bits).
- Para compilar: [SDK de .NET 10](https://dotnet.microsoft.com/download).
- FFmpeg **no se incluye** en este repositorio. La primera vez que se abre, Recodec ofrece descargarlo
  (compilación *release essentials* de [gyan.dev](https://www.gyan.dev/ffmpeg/builds/)) en la subcarpeta `ffmpeg`
  junto al programa. También se puede copiar a mano `ffmpeg.exe` y `ffprobe.exe` en esa carpeta.

## Compilar

```powershell
dotnet build Recodec.slnx -c Release
```

Para generar el programa portable (un único `Recodec.exe` que no necesita .NET instalado):

```powershell
dotnet publish src\Recodec -c Release -o publicado
```

`publicar.ps1` hace lo mismo y además copia el `.exe` a `C:\Program Files\Recodec`
(o a la carpeta indicada con `-Destino`), sin tocar la carpeta `ffmpeg` ni los ajustes.
Con `-SoloCompilar` solo compila.

## Pruebas

`Recodec.Check` es un banco de pruebas que analiza los vídeos de muestra, genera comandos y hace
conversiones reales con FFmpeg:

```powershell
dotnet run --project src\Recodec.Check -c Release -- pruebas\muestras $env:TEMP\recodec-pruebas
```

Debe terminar con `TODO CORRECTO`. Necesita FFmpeg en `C:\Program Files\Recodec\ffmpeg`
o en una carpeta `Recodec\ffmpeg` en algún directorio superior al de la compilación.
Las pruebas de NVENC solo se ejecutan si hay una gráfica NVIDIA compatible.

En `pruebas\ui` hay scripts de PowerShell para manejar la interfaz mediante UI Automation y hacer capturas.

## Estructura

| Carpeta | Contenido |
|---|---|
| `src\Recodec.Core` | Lógica sin interfaz: análisis de vídeos (ffprobe), reglas de compatibilidad, generación de comandos de FFmpeg, teles DLNA, perfiles, ajustes y actualizador. |
| `src\Recodec` | Interfaz WPF (tema Fluent) con patrón MVVM y la ayuda integrada. |
| `src\Recodec.Check` | Banco de pruebas de consola. |
| `pruebas\muestras` | Vídeos y subtítulos sintéticos generados con FFmpeg para las pruebas. |
| `pruebas\ui` | Scripts de automatización de la interfaz. |

Los ajustes del programa (perfiles propios, teles, velocidades aprendidas) se guardan en
`Recodec.settings.json`, junto a `Recodec.exe`.

## Licencia

Recodec se distribuye con licencia [MIT](LICENSE).

Recodec usa FFmpeg como programa externo; no lo enlaza ni lo distribuye. FFmpeg es un proyecto de
[ffmpeg.org](https://ffmpeg.org) con licencia LGPL/GPL, y la compilación que descarga Recodec es GPL.

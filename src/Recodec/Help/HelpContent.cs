namespace Recodec.Help;

public sealed record HelpTopic(string Id, string Title, string Group, string Body)
{
    public override string ToString() => Title;
}

/// <summary>
/// Textos de ayuda. Formato sencillo: "# " título, "## " subtítulo, "- " viñeta, "| a | b |" tabla
/// (la primera fila es la cabecera), "> " consejo destacado y **negrita**.
/// </summary>
public static class HelpContent
{
    public static IReadOnlyList<HelpTopic> Topics { get; } = new List<HelpTopic>
    {
        new("inicio", "Cómo usar Recodec", "Primeros pasos", """
# Cómo usar Recodec
Recodec convierte vídeos con FFmpeg sin que tengas que escribir comandos. El flujo normal es:
- **Abre o arrastra** un vídeo a la ventana. Recodec lo analiza y elige por ti unos ajustes razonables.
- Si quieres, elige un **Perfil** encima de las pestañas (por ejemplo, "Película para la tele"): ajusta todo de una vez.
- Revisa las pestañas **Vídeo**, **Audio** (incluida la corrección del volumen), **Subtítulos** y **Salida**. Cada opción tiene un icono **?** azul que explica qué hace.
- Elige dónde guardar el resultado en **Guardar en**.
- En **Salida → Compatibilidad con la tele**, comprueba que tu tele aceptará el vídeo.
- Pulsa **Convertir**, o **Añadir a la cola** si quieres preparar varios vídeos y convertirlos seguidos.

## En la barra superior
- **Cola**: abre la ventana con los vídeos preparados para convertir uno tras otro. Entre paréntesis, los que faltan.
- **Configuración**: tu idioma favorito, tu tele y las preferencias del programa.
- El texto **FFmpeg …** muestra la versión instalada; púlsalo para buscar actualizaciones.

## Lo que hace Recodec automáticamente
- Copia el vídeo sin recodificar cuando el contenedor elegido lo admite: es lo más rápido y no pierde calidad.
- Elige la pista de audio en tu **idioma favorito** y la convierte a estéreo con la **corrección de volumen** activada.
- Detecta las **bandas negras** analizando 10 puntos de la película (solo se recortan al recodificar).
- Evita combinaciones imposibles (por ejemplo, subtítulos de imagen en MP4) y te avisa en la zona amarilla.

> Si tu tele no reproduce un vídeo, lo más habitual es que el problema sea el contenedor (MKV) o el audio (DTS), no el vídeo. Mira el tema "Tu tele: formatos y Reproducir en".
"""),

        new("samsung", "Configuración recomendada para la tele", "Primeros pasos", """
# Configuración recomendada para la tele
Para enviar películas a una tele (por ejemplo, una Samsung con "Reproducir en" de Windows) estos ajustes funcionan casi siempre:

| Opción | Valor recomendado | Por qué |
| Contenedor | MP4 | Windows solo envía a la tele formatos que esta anuncia; MP4 siempre está. |
| Vídeo | Copiar | Si el original es H.264 o HEVC, la tele lo reproduce tal cual y no pierdes calidad. |
| Si hay que recodificar | HEVC 10 bits por GPU | Conserva el HDR y es muy rápido con una tarjeta NVIDIA. |
| Audio | AAC estéreo, 192 kb/s | Lo reproduce cualquier tele. Las Samsung desde 2018 no admiten DTS. |
| Volumen | Modo noche | Diálogos más claros y explosiones más contenidas. |
| Subtítulos forzados | Incluir como pista o quemar | Si tu tele no muestra la pista, quémalos en la imagen. |

> Si tienes una barra de sonido o un equipo 5.1, añade también la pista **original (sin cambios)** como segunda pista.

Mejor aún: en **Salida → Compatibilidad con la tele** pulsa **Buscar mi tele en la red…**. Recodec le pregunta a tu tele qué formatos admite y ajusta todo esto solo.
"""),

        new("video-modo", "Copiar o recodificar el vídeo", "Vídeo", """
# Copiar o recodificar el vídeo
## Copiar sin recodificar
El vídeo se pasa al nuevo archivo **tal cual**, bit a bit. Ventajas:
- **Sin pérdida de calidad**: la imagen es idéntica al original.
- **Muy rápido**: una película tarda unos pocos minutos (lo que se tarde en leer y escribir el archivo).
- Se conserva el HDR y el Dolby Vision tal cual.

Inconveniente: no se puede recortar, cambiar la resolución ni quemar subtítulos, y el códec tiene que ser compatible con el contenedor elegido.

## Recodificar
El vídeo se descomprime y se vuelve a comprimir con otro códec. Úsalo cuando:
- El códec original no es compatible con tu dispositivo (por ejemplo, XviD, VC-1, MPEG-2 o AV1 en una tele antigua).
- Quieres **reducir el tamaño** del archivo.
- Quieres **quemar subtítulos**, **recortar bandas negras** o **bajar la resolución**.

> Recodificar siempre pierde algo de calidad, aunque con los valores recomendados no se nota a simple vista.
"""),

        new("codecs-video", "Códecs de vídeo: cuál elegir", "Vídeo", """
# Códecs de vídeo
El códec es el "idioma" con el que se comprime la imagen. Un vídeo sin comprimir ocuparía cientos de gigas; el códec aprovecha que de un fotograma al siguiente casi todo se repite y solo guarda lo que cambia. Cuanto más moderno es el códec, **mejor adivina** esos cambios y menos ocupa a igual calidad, pero también **más trabajo** le cuesta al aparato que lo reproduce (y al ordenador que lo convierte).

## Resumen rápido
| Códec | Tamaño a igual calidad | Compatibilidad | Cuándo usarlo |
| XviD / DivX (2001) | 200-250 % · sin HDR | Reproductores DVD/DivX antiguos, todas las teles | Solo para aparatos muy antiguos que no leen nada más moderno. |
| H.264 (2003) | 100 % (referencia) · sin HDR | Total: cualquier tele, móvil o navegador desde 2010 | Aparatos de hace 10-15 años o cuando necesitas que funcione en todas partes. |
| HEVC / H.265 (2013) | 50-60 % · con HDR | Teles 4K desde 2016, móviles y ordenadores modernos | **Recomendado** para películas: buen tamaño y conserva el HDR. |
| VP9 (2013) | 55-65 % · con HDR | Navegadores, Android, YouTube, muchas teles desde 2017 | Vídeo para la web (WebM). En la tele, HEVC suele ir mejor. |
| AV1 (2018) | 40-50 % · con HDR | Teles desde 2020-2021 (no todas), navegadores, móviles recientes | Archivos lo más pequeños posible. Comprueba antes que tu aparato lo admite. |

El tamaño es orientativo: una película de 2 horas en 1080p con la calidad recomendada ocupa más o menos **4-6 GB en H.264**, **2-3 GB en HEVC** y **1,5-2,5 GB en AV1**. En 4K, aproximadamente el triple.

## HEVC (H.265)
- El sucesor de H.264: la misma calidad en **la mitad de espacio**.
- Recodec lo codifica siempre en **10 bits**, aunque el original sea de 8: los degradados (cielos, sombras, humo) se ven sin escalones y el archivo apenas crece.
- Conserva el **HDR10 y el HLG**.
- Lo hace la tarjeta gráfica NVIDIA en pocos minutos.
- Inconveniente: algunos navegadores y ordenadores antiguos no lo reproducen sin extensiones.

## H.264 (AVC)
- El códec más extendido del mundo: lo lee **cualquier cosa** con pantalla fabricada en los últimos 15 años.
- No admite HDR: si el original es HDR, Recodec lo convierte a SDR ajustando los tonos.
- Ocupa casi el doble que HEVC a igual calidad.

## AV1
- El más moderno y eficiente, sin patentes (lo impulsan Google, Netflix, Amazon…).
- Con NVIDIA solo lo codifican las tarjetas RTX 4000 o posteriores; con el procesador es **muy lento** (horas por película).
- Cada vez más teles y móviles lo reproducen, pero no todos: convierte primero un vídeo corto y pruébalo antes de convertir una colección entera.

## VP9
- El códec de Google que usa YouTube. Eficiencia parecida a HEVC.
- Solo se codifica con el **procesador** y es lento (varias veces la duración de la película).
- Tiene sentido para publicar vídeo en la web (WebM) o para aparatos que admiten VP9 pero no HEVC. Para la tele, mejor HEVC.

## XviD / DivX (MPEG-4 Part 2, "ASP")
- El formato de las películas de principios de los 2000, normalmente en archivos **.avi**. XviD y DivX son dos programas que producen el mismo tipo de vídeo, así que un reproductor "DivX" lee XviD y al revés.
- Ocupa más del doble que H.264 a igual calidad y se ve peor en pantallas grandes (bloques en escenas oscuras o con movimiento).
- Solo 8 bits y sin HDR. Los reproductores DivX suelen aceptar como máximo 1080p (los más antiguos, solo 720×576).
- Úsalo solo para un reproductor de DVD/DivX antiguo, el coche, o una tele que no lea nada más moderno. Si tienes un AVI con XviD y tu tele lo reproduce, **cópialo** sin recodificar: recodificarlo a XviD otra vez solo pierde calidad.

## ¿Qué son los "bits" (8 y 10)?
Indican cuántos tonos distintos puede tener cada color: 8 bits son 256 tonos por color y 10 bits, 1.024. Con 8 bits pueden verse "escalones" en los degradados suaves. El HDR **necesita** 10 bits.

> Si no sabes cuál elegir: **HEVC**. Si tu aparato es antiguo: **H.264**. Si es muy antiguo (un DVD con DivX): **XviD** en AVI.
"""),

        new("gpu-cpu", "Codificar con la tarjeta gráfica o con el procesador", "Vídeo", """
# Tarjeta gráfica o procesador
Las tarjetas gráficas modernas tienen un chip dedicado a comprimir vídeo. Recodec sabe usar los tres tipos:

| Gráfica | Nombre del chip | Códecs en Recodec |
| NVIDIA (GTX 1000 o posterior) | NVENC | HEVC, H.264 y AV1 (AV1 solo en RTX 4000 o posteriores) |
| Intel (gráficos integrados desde 2016, Arc) | Quick Sync | HEVC y H.264 |
| AMD (Radeon RX 400 o posterior, gráficos Ryzen) | AMF | HEVC y H.264 |

Al arrancar, Recodec prueba cada combinación codificando 5 fotogramas con las mismas opciones que usará de verdad, y **solo ofrece las que funcionan**. En la barra superior verás qué gráfica ha encontrado y en la pestaña **Registro**, el detalle.

## Con la tarjeta gráfica
- **Muy rápido**: una película 4K puede convertirse más rápido que su propia duración (NVIDIA es la más rápida; Intel y AMD algo menos).
- El ordenador sigue usable mientras tanto.
- A igual calidad, el archivo suele ser un **10-20 % más grande** que con el procesador.

## Con el procesador (x264, x265, AOM, libvpx, Xvid)
- **Más eficiente**: archivos algo más pequeños a igual calidad.
- **Mucho más lento**, sobre todo en 4K y con AV1 o VP9. El procesador trabaja al 100 % mientras dura.
- **VP9 y XviD/DivX solo se pueden hacer con el procesador.**

## Si hay varias gráficas
Por ejemplo, un portátil con gráficos Intel y una NVIDIA. Aparecen todas en **Codificador**. Si un perfil o tus últimos ajustes piden una gráfica que no admite el códec elegido, se usa otra que sí; si ninguna lo admite, el procesador.

## Si no hay ninguna
Verás "sin gráfica compatible (se usará el procesador)":
- En **Codificador** solo aparece el procesador, con su calidad recomendada.
- **Copiar el vídeo no cambia**: no usa ni gráfica ni procesador y sigue tardando minutos. Es lo habitual para la tele.
- Recodificar sí tarda mucho más: con un procesador actual, una película 1080p a HEVC lleva aproximadamente lo que dura; una 4K, varias horas. H.264 es unas 3 veces más rápido que HEVC.

> Si tienes una gráfica Intel o AMD y no aparece, actualiza sus controladores desde la web del fabricante y vuelve a abrir Recodec.

> Cómo se mide la calidad: NVIDIA usa "calidad constante" (CQ), Intel "calidad constante inteligente" (ICQ) y AMD "cuantización constante" (CQP). El número significa más o menos lo mismo en las tres: más bajo, más calidad.
"""),

        new("calidad", "Calidad (CRF / CQ)", "Vídeo", """
# Calidad
Recodec usa **calidad constante**: le dices qué calidad quieres y el codificador usa los datos que haga falta en cada escena. Una escena oscura y quieta ocupa poco; una explosión con mucho movimiento, mucho más.

- El número funciona **al revés**: **más bajo = más calidad** y archivos más grandes.
- En H.264, HEVC, AV1 y VP9, cada **6 puntos** menos el archivo ocupa aproximadamente el **doble**.
- El valor recomendado depende del códec y del codificador, porque las escalas no son iguales:

| Códec y codificador | Recomendado | Rango útil |
| H.264 por CPU (x264) | 20 | 18-23 |
| H.264 por GPU | 23 | 20-26 |
| HEVC por CPU (x265) | 22 | 20-26 |
| HEVC por GPU | 25 | 22-28 |
| AV1 por CPU (AOM) | 30 | 26-34 |
| AV1 por GPU | 32 | 28-36 |
| VP9 (libvpx) | 31 | 27-36 |
| XviD / DivX | 4 | 3-6 (escala de 2 a 31) |

> En el control deslizante, **hacia la derecha** es más calidad. El botón "Valor recomendado" vuelve al punto equilibrado.
"""),

        new("velocidad", "Velocidad de codificación", "Vídeo", """
# Velocidad
Controla cuánto "se esfuerza" el codificador en buscar la mejor forma de comprimir cada fotograma.
- **Más lento** = archivos algo más pequeños a la misma calidad, pero tarda más.
- **Más rápido** = termina antes, archivos algo más grandes.
- La calidad visual la marca sobre todo el control de **Calidad**; la velocidad afecta principalmente al tamaño.

Con la GPU la diferencia de tiempo entre extremos es pequeña. Con la CPU puede ser de 3 a 5 veces.

> "Equilibrado" es un buen punto de partida.
"""),

        new("resolucion", "Resolución máxima", "Vídeo", """
# Resolución máxima
Reduce el tamaño de la imagen si es mayor que el límite elegido. **Nunca se amplía** ni se deforma.

Cada resolución es una **caja**: "1080p" significa que el vídeo tiene que caber en 1920×1080. Por eso funciona con cualquier tamaño, por raro que sea, y en la lista solo aparecen las opciones que de verdad reducen el vídeo, con el **tamaño exacto** que saldrá (ya sin bandas negras, si se recortan).

| Original | Elegido | Resultado |
| 3840×1600 (cine panorámico) | 1080p | 1920×800 |
| 1440×1080 (4:3) | 720p | 960×720 |
| 2560×1080 (ultrapanorámico) | 1080p | 1920×810 |
| 1080×1920 (móvil, vertical) | 720p | 720×1280 |
| 720×576 (DVD) | 480p | 600×480 (se sigue viendo 16:9) |

- **Vídeos verticales**: la caja se gira (720p = 720×1280), así que un vídeo de móvil no se queda diminuto.
- **Vídeos girados**: muchos móviles graban en horizontal y guardan un "giro" en el archivo. Recodec lo tiene en cuenta (verás "girado 90°" en los detalles y la etiqueta "Vertical").
- **DVD y vídeos anamórficos**: los píxeles no son cuadrados; al reducir se conserva la proporción con la que se ven.
- El tamaño final siempre es par (lo exigen los códecs).

Recomendaciones:
- **Igual que el original**: para ver en la tele.
- **1080p**: para pasar películas 4K a un portátil, una tablet o una tele Full HD. Reduce mucho el tamaño.
- **720p y menos**: para móviles o para ahorrar espacio.
"""),

        new("recorte", "Recorte de bandas negras", "Vídeo", """
# Recorte de bandas negras
Muchas películas panorámicas vienen con **bandas negras** arriba y abajo para rellenar una imagen 16:9. Recortarlas:
- Reduce un poco el tamaño del archivo.
- No cambia lo que ves en la tele (la tele vuelve a poner las bandas).

Recodec analiza **10 puntos repartidos** por la película y solo recorta lo que es negro **en todos ellos**. Así no recorta por error una escena oscura.

> El recorte solo se aplica cuando el vídeo se **recodifica**. Si lo copias, se queda tal cual.
"""),

        new("desentrelazado", "Desentrelazado", "Vídeo", """
# Desentrelazado
El vídeo **entrelazado** es propio de la televisión antigua y los DVD: cada fotograma se compone de dos medias imágenes. En pantallas modernas se ven "peines" en los bordes cuando hay movimiento.

- **Automático**: solo se desentrelaza si el archivo indica que es entrelazado. Recomendado.
- **Siempre**: útil si ves peines aunque el archivo diga que es progresivo (pasa en algunas grabaciones de TV).
- **Nunca**: las películas modernas son progresivas y no lo necesitan.

Recodec usa el filtro **bwdif**, de buena calidad y rápido.
"""),

        new("ruido", "Reducir ruido o grano", "Vídeo", """
# Reducir ruido o grano
El **ruido** (puntitos que bailan en las zonas oscuras) y el **grano** de las películas antiguas son lo que más le cuesta comprimir a un códec: ocupan muchos bits sin aportar nada. Quitarlo antes de comprimir hace que se vea más limpio **y** que el archivo ocupe menos. Solo se aplica al recodificar.

| Nivel | Para qué | Velocidad |
| Ninguna | Vídeos limpios (la mayoría de películas modernas). | — |
| Ligera | Ruido suave de cámaras digitales o series recientes. Casi no se nota en el detalle. | No ralentiza |
| Media | Películas antiguas, TV grabada, DVD. | No ralentiza |
| Fuerte | Mucho ruido o grano. Puede dejar caras y texturas algo planas. | No ralentiza |
| Máxima | El mejor filtro (NLMeans): quita el grano conservando más detalle. | **Muy lenta**: unos 7 fotogramas/s en 1080p |

Los niveles ligera, media y fuerte usan el filtro **hqdn3d** con valores habituales para cada nivel. NLMeans solo trabaja en 8 bits: en vídeos de 10 bits (HDR) el nivel máximo usa hqdn3d con la intensidad más alta para no perder los 10 bits.

> El grano a veces es intencionado (el "aspecto de cine"). Si quieres conservarlo, déjalo en Ninguna o Ligera y sube un poco la calidad.
"""),

        new("hdr", "HDR, HDR10 y Dolby Vision", "Vídeo", """
# HDR
El **HDR** (alto rango dinámico) permite blancos más brillantes y colores más intensos en teles compatibles. Necesita **10 bits** y unos metadatos que indican a la tele cómo mostrarlo.

| Tipo | Qué hace Recodec |
| HDR10 | Al copiar se conserva intacto. Al recodificar con HEVC o AV1 se conservan los 10 bits, los colores BT.2020 y los metadatos de brillo. |
| HLG | Igual que HDR10 (es el HDR de la televisión en directo). |
| Dolby Vision | Al copiar se mantiene. Al recodificar se pierde la capa Dolby Vision y queda como HDR10 (el perfil 5 no tiene HDR10 y puede verse con colores raros). |

## ¿Y con H.264?
H.264 no admite HDR. Si recodificas un vídeo HDR a H.264, Recodec lo **convierte a SDR ajustando los tonos** (tone mapping). Si no se hiciera, la imagen se vería **apagada y grisácea**, que es justo lo que pasa con un HDR mal convertido.
"""),

        new("idioma", "Idioma favorito", "Audio y volumen", """
# Idioma favorito
Se elige en **Configuración** (barra superior). Al abrir un vídeo, Recodec elige automáticamente:
- La **pista de audio** en tu idioma favorito (si hay varias, la marcada como principal).
- Los **subtítulos** en tu idioma, como pista activable.
- Si no hay audio en tu idioma pero sí subtítulos, **quema los subtítulos** en la imagen para que puedas seguir la película.

La primera vez se usa el idioma de Windows. Si lo cambias, se guarda para las próximas veces y se vuelve a hacer la selección del vídeo abierto.

> La pista original **no se incluye** salvo que la añadas tú con "Añadir original (sin cambios)".
"""),

        new("codecs-audio", "Formatos de audio y canales", "Audio y volumen", """
# Formatos de audio
| Formato | Canales | Compatibilidad | Cuándo usarlo |
| AAC estéreo | 2 | Total | **Recomendado** para la tele, el móvil o el portátil. Es el que mejor funciona con el modo noche. |
| AAC 5.1 | 6 | Muy buena | Equipos 5.1 que no admiten Dolby. |
| AC3 (Dolby Digital) 5.1 | 6 | Muy buena en teles y barras de sonido | Cine en casa con Dolby. También cabe en AVI. |
| E-AC3 (Dolby Digital+) 5.1 | 6 | Buena en equipos modernos | Mejor calidad que AC3 al mismo tamaño. |
| Opus estéreo | 2 | Navegadores y móviles | Obligatorio en WebM. |
| MP3 estéreo | 2 | Total, incluidos aparatos muy antiguos | El audio clásico de los AVI con DivX/XviD. |
| FLAC (sin pérdida) | Los del original | Ordenadores, Kodi/Plex; pocas teles | Conservar el sonido exacto (ocupa mucho más). Solo en MKV y MP4. |
| Copiar sin cambios | Los del original | Depende del original | Para conservar DTS, TrueHD, Atmos… en un equipo de cine en casa. |

## Calidad (kb/s)
Para AAC estéreo, **192 kb/s** es transparente para casi todo el mundo; 128 kb/s es suficiente para diálogos. Para MP3, 192-256 kb/s. Para 5.1 usa 384-640 kb/s. FLAC no tiene este ajuste porque guarda el sonido sin perder nada.

## ¿Por qué estéreo?
Los altavoces de la tele son estéreo. Si le mandas un 5.1, la tele lo mezcla por su cuenta y los diálogos (canal central) suelen quedar bajos. Recodec hace esa mezcla **dando prioridad a los diálogos**.

> La corrección de volumen **solo** se aplica a las pistas convertidas. Una pista "copiada" queda tal cual.
"""),

        new("pistas-audio", "Añadir y quitar pistas de audio", "Audio y volumen", """
# Añadir y quitar pistas
- **Añadir convertida**: añade una pista del vídeo convertida al formato elegido, con la corrección de volumen.
- **Añadir original (sin cambios)**: copia la pista tal cual (útil para conservar el DTS o el Atmos para una barra de sonido).
- **Desde un archivo…**: añade audio de un archivo aparte (por ejemplo, un doblaje en .ac3 o .m4a).
- El botón **✕** quita la pista del vídeo convertido (el original no se toca).

## Orden de las pistas
Cuando hay más de una pista aparecen las flechas **↑** y **↓** y una etiqueta con su número. **La pista 1 es la que suena al empezar la película**; las demás se eligen desde el mando de la tele (botón de audio o de idioma). Por ejemplo, deja primero la pista convertida con el modo noche y después la original para la barra de sonido.

## Retraso de las pistas externas
Si el audio externo no va sincronizado, indica un retraso en milisegundos:
- **Positivo** (por ejemplo, 500): el audio suena medio segundo **más tarde**.
- **Negativo** (por ejemplo, -300): el audio suena **antes**.

> Pulsa **Comprobar sincronización** junto al retraso: verás un trozo de la película con esa pista y podrás ajustar el retraso sin convertir nada.
"""),

        new("volumen", "Corrección del volumen (modo noche)", "Audio y volumen", """
# Corrección del volumen
Las películas se mezclan para salas de cine, con una diferencia enorme entre los diálogos y la acción. En casa esto obliga a subir el volumen para entender a los actores y bajarlo en cada explosión.

Los ajustes están en la pestaña **Audio**, debajo de las pistas, y solo actúan sobre las pistas que tienen el volumen en **Modo noche** (si no hay ninguna, aparecen desactivados). Recodec aplica tres pasos, en este orden:
- **1. Realce de diálogos**: al pasar de 5.1/7.1 a estéreo, el canal central (donde van las voces) entra completo y el resto se baja. Se descarta el subwoofer, como en el "modo noche" de los reproductores.
- **2. Compresión**: baja los golpes de sonido (explosiones, disparos, música fuerte) para acercarlos al volumen de las voces.
- **3. Volumen final**: ajusta el volumen medio de toda la película a un nivel fijo, para que todas suenen igual de fuertes.

## Qué hace cada opción del desplegable "Volumen" de una pista
| Opción | Qué hace |
| Modo noche | Los tres pasos, con los valores de los tres controles. **Recomendado.** |
| Suave | Una compresión ligera y fija y el volumen final. Mantiene más la fuerza original. No usa los controles 1 y 2. |
| Sin cambios | Solo convierte el formato; el sonido queda como en el original. |

> En una prueba con audio 5.1, la diferencia entre diálogos y explosiones pasó de unos **30 dB** a unos **6 dB** con los valores por defecto.
"""),

        new("volumen-pista", "Volumen de una pista: modo noche, suave o sin cambios", "Audio y volumen", """
# Volumen de una pista
Cada pista convertida tiene un desplegable **Volumen** que decide cuánto se toca su sonido:

| Opción | Qué hace | Cuándo usarla |
| Modo noche | Realza las voces, suaviza las explosiones y fija el volumen medio, con los tres ajustes de "Corrección del volumen". | Para ver películas sin tener que subir y bajar la tele. **Recomendado.** |
| Suave | Solo una compresión ligera y fija y el volumen medio. Las explosiones conservan casi toda su fuerza. | Si te gusta el sonido de cine pero quieres que todas las películas suenen a un volumen parecido. |
| Sin cambios | Convierte el formato sin tocar el volumen. | Si el audio ya está bien, o para conservar la mezcla original. |

Las pistas **copiadas sin cambios** (formato "Copiar sin cambios") no tienen este desplegable: se guardan tal cual, por ejemplo para una barra de sonido.

> El realce de diálogos solo funciona si el original es 5.1 o 7.1: en estéreo la voz ya está mezclada con todo lo demás.
"""),

        new("dialogos", "Realce de diálogos", "Audio y volumen", """
# Realce de diálogos
En las películas con sonido 5.1 o 7.1 las voces van casi siempre en un canal propio, el **central**. Al mezclar a estéreo para la tele, Recodec mete ese canal entero y **baja los demás** (música, efectos, ambiente). El control decide cuánto se bajan:
- **0 (Nada)**: mezcla normal, todos los canales con su peso habitual.
- **4-7**: las voces se entienden claramente sin que la música desaparezca. **Recomendado.**
- **10 (Máximo)**: la música y los efectos quedan bastante en segundo plano.

Solo tiene efecto si el audio original es **5.1 o 7.1**. En un audio estéreo la voz ya viene mezclada con todo lo demás y no se puede separar; en ese caso Recodec te avisa y solo actúan la compresión y el volumen final.
"""),

        new("compresion", "Compresión del rango dinámico", "Audio y volumen", """
# Compresión
La "dinámica" es la diferencia entre lo más flojo (un susurro) y lo más fuerte (una explosión) de la película. En el cine puede ser enorme; en casa obliga a tocar el mando. La compresión **baja automáticamente lo que suena muy fuerte**, de forma que se acerca al volumen de las voces:
- **0 (Nada)**: sin compresión; la película conserva toda su fuerza y solo se ajusta el volumen medio.
- **4-6**: las explosiones siguen sonando con fuerza, pero ya no obligan a bajar la tele. **Recomendado.**
- **8-10**: casi todo suena a un volumen parecido. Ideal para ver películas de noche con el volumen bajo.

> Una compresión muy fuerte puede hacer que la música suene "plana". Si es tu caso, déjala en 4-6.
"""),

        new("lufs", "Volumen final (LUFS)", "Audio y volumen", """
# Volumen final
Ajusta el **volumen medio** de toda la película a un nivel fijo. Así no hay películas que suenen mucho más bajas que otras, y al pasar de una película a Netflix o a la tele no tienes que tocar el mando.

El nivel se mide en **LUFS**, una unidad que tiene en cuenta cómo de fuerte **nos parece** el sonido (no solo los picos). Cuanto más cerca de 0, más fuerte:

| Opción | Uso |
| Más fuerte (−14 LUFS) | Como la música en plataformas de streaming. Puede quedar fuerte en la tele. |
| Como Netflix o YouTube (−16 LUFS) | **Recomendado** para la tele. |
| Más bajo (−20 LUFS) | Algo más bajo de lo habitual. |
| Como la tele europea (−23 LUFS) | El estándar de las cadenas de televisión (EBU R128). Suena bastante bajo: tendrás que subir el volumen. |
"""),

        new("tele-dlna", "Tu tele: formatos y \"Reproducir en\"", "Salida", """
# Tu tele y "Reproducir en"
Al enviar un vídeo con **"Reproducir en"** de Windows (o desde cualquier programa por **DLNA**), el ordenador primero le pregunta a la tele qué formatos acepta. Si el archivo no está en esa lista, Windows dice que **el dispositivo no puede reproducir el vídeo** y ni siquiera lo intenta.

## El problema de los contenedores
La lista de la tele usa **nombres de tipo** (MIME). El fallo más habitual es que no coincidan con los de Windows aunque la tele sepa reproducir el archivo:

| Contenedor | Nombre que usa Windows | Lo que anuncian muchas Samsung | Resultado |
| MP4 | video/mp4 | video/mp4 | Funciona |
| MKV | video/x-matroska | video/x-mkv | Windows lo rechaza |
| AVI | video/avi | video/avi, video/x-msvideo | Funciona |

Por eso un MKV que la tele abre sin problema desde un USB falla con "Reproducir en", y el mismo vídeo pasado a **MP4** funciona. Pasar de MKV a MP4 copiando el vídeo tarda muy poco y no pierde calidad.

El otro motivo habitual es el **audio**: las Samsung desde 2018 (y otras marcas) no admiten **DTS** ni **TrueHD**. El vídeo se ve pero sin sonido, o no se reproduce.

## Qué hace Recodec
- En **Salida → Compatibilidad con la tele** eliges para qué tele preparas los vídeos. Por defecto, **Tele cualquiera**: MP4, H.264/HEVC y audio AAC, AC3 o MP3, que aceptan casi todas.
- **Buscar mi tele en la red…** busca las teles encendidas en tu red y les pide su lista de formatos. Verás qué contenedores, vídeos y audios admite cada una y una recomendación.
- Al elegir una tele, Recodec **solo comprueba** el vídeo: en la pestaña Salida verás una lista con ✓, ⚠ y ✕ y lo que cambiaría.
- Si quieres, pulsa **Aplicar ajustes para la tele**: cambia solo lo imprescindible (contenedor que Windows le enviará, vídeo recodificado a HEVC o H.264 si la tele no admite el original, audio convertido si no lo admite). Nada cambia sin pulsar ese botón.
- Si la tele no aceptaría el vídeo, también aparece un aviso amarillo encima de "Guardar en".

> La tele solo anuncia formatos, no cuenta todo (por ejemplo, no dice si admite HDR ni hasta qué resolución). Si un vídeo pasa la comprobación y aun así falla, mira el registro y prueba con H.264.

## Si la búsqueda no encuentra la tele
- Tiene que estar **encendida** y en la **misma red** que el ordenador (da igual wifi o cable, pero el mismo router).
- En las Samsung, la opción se llama **Compartir contenido** o **Dispositivos conectados** (Ajustes → General → Administrador de dispositivos externos).
- Algunos antivirus o el Firewall de Windows en red **pública** bloquean la búsqueda: marca tu red como **privada**.
"""),

        new("subtitulos", "Subtítulos como pista", "Subtítulos", """
# Subtítulos como pista
Los subtítulos se guardan dentro del archivo y se pueden activar o desactivar desde el reproductor.

## Texto o imagen
| Tipo | Ejemplos | Notas |
| Texto | SRT, ASS, WebVTT, texto MP4 | Se pueden convertir entre formatos. MP4 los guarda como "texto MP4". |
| Imagen | PGS (Blu-ray), VobSub (DVD) | Son dibujos, no texto. **MP4 no los admite**: solo se pueden incluir en MKV o quemarlos en la imagen. |

## Completos o forzados
Una película suele traer dos tipos de subtítulos en tu idioma:
- **Completos**: todos y cada uno de los diálogos. Para ver la película en versión original.
- **Forzados**: solo aparecen en momentos concretos, por ejemplo cuando un personaje habla en otro idioma que el resto, o para traducir un cartel o una carta. Son los que hay que ver aunque la película esté en tu idioma.

Muchos archivos **no los marcan bien** (ni con la marca "forzado" ni en el título), así que Recodec los distingue por el **número de líneas**:

| | Líneas en una película de 2 horas | Por minuto |
| Completos | 1.000 - 1.800 | 8 - 15 |
| Forzados | de 5 a 100 | menos de 1 |

Junto a cada subtítulo verás cuántas líneas tiene. Se consideran forzados si tienen **menos de 2 líneas por minuto** de película o **menos de la cuarta parte** que los subtítulos más largos del archivo (salvo que el título diga "completos", "full" o "SDH"). Los detectados así aparecen como "forzados (por el número de líneas)".

Si el archivo no dice cuántas líneas tiene cada subtítulo (pasa en muchos MKV), Recodec las cuenta al abrirlo: tiene que leer el archivo entero, así que en una película grande tarda desde unos segundos hasta un par de minutos. Mientras, puedes seguir usando el programa.

## Qué hace Recodec con cada tipo
- **Audio en tu idioma**: los forzados se guardan como pista marcada "forzada" y "por defecto", para que la tele los muestre sola en los momentos en que hacen falta.
- **Audio en otro idioma** (no hay doblaje en tu idioma): se queman en la imagen los **completos** (si hay varios, los que más líneas tienen). Si al contar resulta que los elegidos eran forzados, se cambian solos por los completos.
- Aun así, **no todas las teles respetan la marca "forzado"** al reproducir por DLNA.

> Si tu tele no muestra los subtítulos de la pista, quémalos en la imagen.
"""),

        new("quemar", "Quemar subtítulos en la imagen", "Subtítulos", """
# Quemar subtítulos
Los subtítulos se "pintan" sobre la imagen y pasan a formar parte del vídeo.
- **Ventaja**: se ven siempre, en cualquier tele o reproductor, sin configurar nada.
- **Inconveniente**: no se pueden quitar, y obliga a **recodificar** el vídeo (tarda más).

Puedes quemar un subtítulo del propio vídeo o un archivo externo (.srt, .ass, .ssa, .vtt o .sup de Blu-ray). Los subtítulos de imagen se escalan al tamaño del vídeo automáticamente.
"""),

        new("contenedores", "Contenedores: MP4, MKV, MOV, WebM y AVI", "Salida", """
# Contenedores
El contenedor es la "caja" que guarda juntos el vídeo, el audio, los subtítulos y los capítulos. **Cambiar de contenedor no cambia la calidad.**

| Contenedor | Admite | Compatibilidad | Cuándo usarlo |
| MP4 | H.264, HEVC, AV1, VP9, XviD · AAC, AC3, E-AC3, MP3, FLAC · subtítulos de texto | La mayor: teles, móviles, consolas, "Reproducir en" de Windows | **Recomendado** para la tele y casi todo. |
| MKV | Todo: cualquier códec, varias pistas, subtítulos PGS, fuentes | Buena en ordenadores y Plex/Kodi; algunas teles solo desde USB | Archivar películas con todas sus pistas. |
| MOV | H.264, HEVC, XviD · AAC, AC3, MP3 | Apple (iPhone, Mac, Final Cut) | Si vas a usarlo en productos Apple o para editar. |
| WebM | Solo AV1 o VP9 · Opus | Navegadores | Vídeo para páginas web. |
| AVI | Solo XviD/DivX · MP3 o AC3 · **sin subtítulos como pista** ni capítulos | Reproductores DVD/DivX y aparatos antiguos | Solo para aparatos que no leen MP4. |

> Tu tele puede reproducir MKV desde un USB y aun así rechazarlo por "Reproducir en" de Windows: Windows y la tele no se ponen de acuerdo con el nombre del formato. Con MP4 no pasa.
"""),

        new("faststart", "Optimizar para reproducción en red", "Salida", """
# Optimizar para reproducción en red
En los MP4 y MOV, el índice del archivo normalmente se escribe al final. Esta opción lo mueve al **principio**, de forma que:
- La tele o el navegador pueden **empezar a reproducir antes** de leer el archivo entero.
- Saltar a otro punto de la película es más rápido al reproducir por red (DLNA, "Reproducir en").

Al terminar la conversión se reescribe el archivo una vez, lo que añade unos segundos. Recomendado.
"""),

        new("comando", "Comando de FFmpeg", "Salida", """
# Comando de FFmpeg
Recodec es una interfaz para **FFmpeg**. Aquí puedes ver el comando exacto que se ejecutará con tus ajustes.
- Sirve para **aprender** qué hace cada opción.
- Puedes **copiarlo** y ejecutarlo en una terminal (desde la carpeta "ffmpeg" del programa) o ajustarlo a mano.
- Si algo falla, el comando y el mensaje de error aparecen en la pestaña **Registro**.
"""),

        new("perfiles", "Perfiles", "Primeros pasos", """
# Perfiles
Un perfil es un conjunto de ajustes con nombre que se aplica de una vez. Se elige en **Perfil**, encima de las pestañas, y se aplica al vídeo abierto **y a todos los que abras después**, hasta que elijas otro.

## Perfiles que vienen con Recodec
| Perfil | Qué hace |
| Película para la tele | MP4 copiando el vídeo si se puede (si no, HEVC 10 bits) y audio AAC estéreo con el modo noche. |
| Serie a 720p (ocupa poco) | HEVC a 720p con calidad algo más baja: un capítulo de una hora ocupa unos 300-500 MB. |
| Película antigua o con grano | HEVC quitando ruido y grano (nivel medio). |
| Máxima calidad (archivo grande) | HEVC por procesador con calidad muy alta y audio AAC 5.1 sin tocar el volumen. |
| Solo pasar a MP4 (rápido) | Copia vídeo y audio sin recodificar; solo convierte el audio si MP4 no lo admite. |
| DivX/XviD para reproductores antiguos | AVI con XviD a 720p y audio MP3. |

**Mis últimos ajustes** no aplica ningún perfil: cada vídeo se abre con las últimas opciones que elegiste.

## Crear tus propios perfiles
- Ajusta las opciones como te gusten (vídeo, formato y volumen del audio, contenedor…).
- Pulsa **Guardar como perfil…** y ponle un nombre. Aparecerá en la lista con "· mío".
- Si usas un nombre que ya tienes, Recodec te pregunta si quieres sustituirlo: así se **modifica** un perfil.
- La **papelera** borra el perfil elegido (solo los tuyos).

Se guardan en **Recodec.settings.json**, junto al programa.

## Qué guarda un perfil
- **Salida**: contenedor, capítulos y optimización para red.
- **Vídeo**: copiar o recodificar, códec, codificador, calidad, velocidad, resolución máxima, recorte, desentrelazado y ruido.
- **Audio**: formato, calidad y volumen de las pistas convertidas, y los tres ajustes de la corrección del volumen.

No guarda las pistas ni los subtítulos que se eligen (dependen de cada vídeo y de tu idioma favorito) ni el destino. Las pistas **copiadas sin cambios** se respetan.

> Si tienes una tele elegida en "Compatibilidad con la tele", después de aplicar el perfil Recodec comprueba si la tele lo acepta; para ajustarlo, pulsa "Aplicar ajustes para la tele" en la pestaña Salida.
"""),

        new("cola", "Cola de conversión", "Uso", """
# Cola
La cola permite preparar varios vídeos y convertirlos seguidos, por ejemplo durante la noche. Se abre con el botón **Cola** de la barra superior; entre paréntesis se ve cuántos vídeos faltan.
- **Añadir a la cola** guarda una copia de los ajustes actuales: puedes abrir otro vídeo y seguir trabajando.
- **Convertir** añade el vídeo a la cola y la inicia.
- Si **arrastras varios vídeos** (o una carpeta) a la ventana, se añaden todos con tus ajustes por defecto y se abre la ventana de la cola.
- Mientras convierte se abre una **ventana de progreso** con el porcentaje, el tiempo que falta, la velocidad, el tamaño final previsto y lo que está haciendo con el vídeo, el audio y los subtítulos. El resto del programa queda bloqueado hasta que termine; si cierras esa ventana, se vuelve a abrir con **Ver progreso**.
- **Cancelar** detiene la conversión en curso, borra el archivo a medias y para la cola. Los vídeos pendientes siguen en la lista: pulsa "Iniciar cola" para continuar.

## Al terminar
En **Al terminar** (en la ventana de progreso que aparece al convertir y en la ventana Cola) puedes elegir que el equipo se **suspenda** o se **apague** cuando acabe toda la cola. Antes aparece una cuenta atrás de 60 segundos para cancelarlo. Si cancelas la conversión, no se apaga. Esta opción no se recuerda: hay que elegirla cada vez.

Mientras convierte, Recodec impide que Windows suspenda el equipo por inactividad (la pantalla sí puede apagarse).
"""),

        new("sincronizacion", "Comprobar la sincronización del audio", "Audio y volumen", """
# Comprobar la sincronización
Cuando añades una pista de audio **desde un archivo** (por ejemplo, un doblaje descargado aparte), puede que no vaya a la par que la imagen: la voz llega antes o después de que se muevan los labios. Suele pasar si el audio viene de otra edición de la película.

El botón **Comprobar sincronización** de la pista abre una ventana que reproduce **20 segundos** de la película con esa pista:
- Elige en **Momento** una escena en la que alguien hable de cerca (se ven mejor los labios).
- Si la voz llega **tarde**, pulsa los botones con **−** (−20, −100 o −500 ms). Si llega **antes**, los de **+**.
- Cada cambio vuelve a crear el clip en unos segundos y lo reproduce en bucle.
- Cuando coincida, pulsa **Usar este retraso**: se guarda en la pista y se aplica al convertir.

| Botón | Para qué |
| ±500 ms | Desfases grandes (medio segundo): se nota en cualquier escena. |
| ±100 ms | Ajuste normal. |
| ±20 ms | Ajuste fino: por debajo de unos 40 ms casi nadie nota la diferencia. |

> Si el desfase va **creciendo** a lo largo de la película (al principio va bien y al final no), el audio es de una versión con otra velocidad de fotogramas (por ejemplo, 25 fps frente a 23,976). Eso no se arregla con un retraso.

El clip es de baja resolución (720p) para que se cree rápido y no se guarda: se borra al cerrar la ventana.
"""),

        new("registro", "Registro de actividad", "Uso", """
# Registro
Muestra todo lo que hace Recodec: los comandos de FFmpeg y los avisos o errores. Si una conversión falla, aquí está el motivo.

> Si necesitas ayuda con un error, usa "Copiar registro" y pégalo donde preguntes.
"""),

        new("actualizar", "Actualizar FFmpeg", "Uso", """
# Actualizar FFmpeg
Recodec lleva FFmpeg dentro de su propia carpeta (subcarpeta **ffmpeg**): no instala nada en Windows.

Una vez al día, al arrancar, comprueba si hay una versión nueva en **gyan.dev** (las compilaciones para Windows que enlaza la web oficial de FFmpeg). Si la hay, te lo ofrece. Al aceptar:
- Descarga la versión nueva (unos 100-120 MB).
- Comprueba su **firma SHA-256** para asegurarse de que el archivo está completo y no ha sido manipulado.
- Se queda solo con **ffmpeg.exe**, **ffprobe.exe** y la licencia, y borra todo lo demás.
- Comprueba que funciona; si algo falla, **vuelve a la versión anterior**.

También puedes comprobarlo a mano pulsando el texto "FFmpeg …" de la barra superior.
"""),

        new("configuracion", "Configuración del programa", "Uso", """
# Configuración
Se abre con el botón **Configuración** de la barra superior. Todo se guarda en el archivo **Recodec.settings.json**, junto a Recodec.exe, así que viaja contigo si copias la carpeta del programa.
- **Idioma favorito**: el idioma del audio y los subtítulos que se eligen al abrir un vídeo.
- **Tu tele**: la tele con la que se comprueban los vídeos (contenedor, vídeo y audio que acepta); los cambios se aplican con un botón. Se puede buscar en la red.
- **Abrir la carpeta al terminar la conversión**: muestra el archivo convertido en el Explorador al acabar la cola.
- **Recordar la carpeta de destino**: los próximos vídeos se guardarán en la misma carpeta que el último. Si no, se guardan junto al original.
- **Buscar actualizaciones de FFmpeg al arrancar**: como mucho una vez al día.

Además, Recodec recuerda solo tus últimas elecciones de contenedor, códec, codificador, velocidad y corrección del volumen.
"""),
    };

    public static HelpTopic Get(string id) => Topics.FirstOrDefault(t => t.Id == id) ?? Topics[0];
}

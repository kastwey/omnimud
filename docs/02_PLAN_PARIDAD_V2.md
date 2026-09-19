# OMnimud v2 — Análisis de divergencias y plan de paridad

Fecha: 2026-09-18. Complementa a [01_FUNCIONALIDAD_ORIGINAL.md](01_FUNCIONALIDAD_ORIGINAL.md) (qué hacía el original) y al anexo [A4](auditoria/A4_v2_estado.md) (estado real de la v2, con citas `fichero:línea`).

Sustituye a `trunk\PLAN_REESCRITURA_OMNIMUD.md`, que además está corrupto entre las líneas 474 y 581 (tiene pegado un diff).

## 0. Reglas del plan

1. **Añadir sí, recortar no.** Todo lo del original se porta, y todo lo nuevo de la v2 se conserva (§2).
2. **100 % accesible.** Ningún formulario se da por terminado sin pasar los criterios de §4.
3. **WinForms**, sobre **.NET 10**.
4. Los defectos del original marcados **[CORREGIR]** en el documento 01 se corrigen, no se replican.
5. Único recorte acordado: el soporte específico de **Window-Eyes** (descatalogado en 2017), cubierto por UI Automation.

Decisiones ya tomadas contigo:

| Tema | Decisión |
|---|---|
| Anuncios al lector | **UI Automation como vía principal**; DLL de JAWS/NVDA como respaldo |
| Evaluación de triggers | **Por línea**, con casilla **multilínea** por trigger para evaluar el bloque |
| Datos del Omnimud antiguo | **No se migran**: ni Registro ni `.oxf`. Formato de intercambio nuevo |
| Actualizaciones, informes, ayuda | **Última fase**, con las interfaces preparadas desde el principio |
| Window-Eyes | **Se retira** |
| Código C#/VB en triggers | Se sustituye por **Lua en sandbox** |

---

## 1. Diagnóstico de la v2 en una frase

El **Core y los datos están hechos y probados (249 tests en verde), pero casi nada llega a la ventana de juego**: `FrmClient` calcula las coincidencias de triggers y las descarta, nunca carga los alias, usa un diccionario de paths vacío, el único reproductor de sonido es nulo, las opciones se guardan y nadie las lee, y TLS y la codificación se configuran pero no se aplican. El trabajo principal no es escribir motores nuevos, sino **construir la sesión que los una** y **rehacer la interfaz con accesibilidad real**.

---

## 2. Novedades de la v2 que se conservan

TLS 1.2/1.3 · codificación por MUD · login script con `%character` y `%password` · conexión rápida · Lua en sandbox · ANSI de 256 colores, TrueColor y brillantes · GMCP con `Comm.Channel` al panel de mensajes · cifrado AES-256-GCM + DPAPI · descarga de sonidos con saneado de nombres y tope de tamaño · autodetección JAWS/NVDA y braille por NVDA · DLL nativas x64/arm64/x86 · prioridad de triggers y timeout de regex · alias activables · scroll inteligente · botón Reconectar · Ctrl+L, Ctrl+K, Ctrl+M, Alt+S · lanzador en árbol · varias sesiones a la vez · migraciones SQLite, inyección de dependencias y localización en/es.

---

## 3. Matriz de paridad

Estado v2: ✅ funciona · 🟡 parcial · 🔌 existe en Core pero sin conectar · ❌ ausente. La columna Fase remite a §6.

### 3.1 Ventana de juego

| Función del original | v2 | Fase |
|---|---|---|
| Etiquetas visibles con mnemónico en Mensajes, Recibido y Texto a enviar | ❌ | 2 |
| `AccessibleName`/`Description` en todos los controles | ❌ (las cadenas existen en los resx y no se usan) | 2 |
| Cursor de Recibido y de Mensajes según opción (final / mantener / según posición) | ❌ (siempre salta al final: impide repasar con lector) | 2 |
| Ctrl+1…0 con concatenación de dígitos, fila superior y numpad | ❌ | 2 |
| Ctrl+º repaso de mensajes hacia atrás | ❌ | 2 |
| Ctrl+F hora del mensaje bajo el cursor; hora en la barra de estado | ❌ | 2 |
| Mensajes con fecha y hora, límite 1000 | 🟡 (solo GMCP, sin límite) | 2 |
| Cuadro de Mensajes oculto si el MUD no tiene regla; reaparece al llegar un mensaje | ❌ | 2 |
| Menús Acciones, Edición, Herramientas, Ayuda con F1–F9, Ctrl+B/S/E | 🟡 (un menú sin atajos; F3/F4 sí) | 2 |
| Enter vacío repite el último; Shift/Ctrl+Enter línea en blanco o salto | ❌ | 2 |
| Cuadro de envío multilínea con lectura de línea anterior/siguiente | ❌ (una sola línea) | 2 |
| Historial de tamaño configurable, con sonido | 🟡 (500 fijas, sin sonido) | 2 |
| Modo contraseña sin historial ni log | 🟡 (**las contraseñas entran en el historial**) | 0 |
| Escritura directa desde los cuadros de lectura | ❌ | 2 |
| Enter sobre URL o e-mail lo abre | ❌ | 2 |
| Buscar y Buscar siguiente, por cuadro | ❌ | 2 |
| Botón Cancelar / Desconectar / Cerrar con nombre accesible actualizado | 🟡 (solo Reconnect) | 2 |
| Barra de estado: conexión, tiempo conectado, estado de mensajes | 🟡 | 2 |
| Parpadeo en la barra de tareas con la ventana inactiva | ❌ (solo la casilla) | 2 |
| Voz solo con ventana activa; texto entrante encolado; mensajes interrumpen | 🟡 (habla siempre y **con los códigos ANSI dentro**) | 1–2 |
| Modo silencioso F8 y marcador `all_speak:` | 🟡 (Ctrl+M silencia; sin marcador) | 2 |
| Modo de desconexión si falla la conexión | ❌ | 5 |
| Confirmar al cerrar; enviar comando de salida antes de cortar | ❌ | 5 |
| Login automático con nombre y contraseña | ✅ (login script, más flexible) | — |
| Conectar a un MUD usa su personaje predeterminado | ❌ | 6 |

### 3.2 Comandos internos

| Comando | v2 | Fase |
|---|---|---|
| `_path`, `_path -r` | 🔌 | 4 |
| `calias`, `calias N`, `calias N acción`, `uncalias N` | ❌ | 4 |
| `+triggers`, `-triggers`, `+trigger N`, `-trigger N`, `triggers` | ❌ | 3 |
| `paths`, `paths iniciar/ultima/ultima borrar/grabado/cancelar/detener` | ❌ | 4 |
| `cls`, `callate`, `hablar` | ❌ (Ctrl+L y Ctrl+M como atajos) | 4 |
| Concatenación `;` y repetición `N#` opcionales | 🔌 (sin opciones que las activen) | 4 |

### 3.3 Motores

| Función | v2 | Fase |
|---|---|---|
| Triggers literal, anclas, regex, comodines `%s %d %w %Ns %Nw` | 🔌 (43 tests; resultado descartado) | 3 |
| Acciones: comando, sonido, **ambas**, código | 🔌 (falta "ambas") | 3 |
| Triggers de comando `@palabra` | ❌ | 3 |
| API de código: enviar, **esperar respuesta**, mostrar, decir, mensaje, sonidos, variables, contador, última actividad, quitar colores, constantes ANSI | 🟡 (9 de ~20 funciones) | 3 |
| Activar/desactivar con Espacio y anuncio | ❌ | 3 |
| Alias por primera palabra | 🔌 (nunca se cargan) | 4 |
| Paths: expandir, colapsar, invertir, validar | 🔌 (diccionario vacío) | 4 |
| Diccionario de direcciones **por MUD**, con su ventana | ❌ (tabla global sin UI) | 4 |
| Teclas de movimiento numpad, F2, por MUD o personaje | ❌ (tabla sin registrar en DI) | 4 |
| MSP `!!SOUND` / `!!MUSIC` | 🔌 (**no hay reproductor real**) | 5 |
| Carpeta de sonidos por MUD; sonido y música fuera de ventana | ❌ | 5 |
| Reglas de mensajes por MUD (Balzhur, Callandor, Simauria, Cyberlife) | 🔌 (extractor regex sin registrar) | 5 |
| Logs por día / por partida | ❌ | 5 |
| Opciones: 23 del original, en tres ámbitos con herencia | 🟡 (10 claves, solo global, **nadie las lee**) | 5 |
| Proxy para descargas HTTP | ❌ | 5 |

### 3.4 Gestión y servicios

| Función | v2 | Fase |
|---|---|---|
| MUD, personajes, alias, triggers, paths: alta, edición, borrado | ✅ (sin accesibilidad completa) | 6 |
| Personaje predeterminado por MUD; "Conectar con…" | 🟡 (columna sin usar) | 6 |
| Exportar / importar todas las entidades | ❌ | 6 |
| Importar alias, triggers y paths **desde otro personaje** | ❌ | 6 |
| Confirmaciones, foco al campo erróneo, selección conservada | 🟡 (la lista pierde selección y foco; un duplicado **tumba la aplicación**) | 6 |
| Información personal, informes de error, manejador global de errores | ❌ | 7 |
| Comprobar actualizaciones | ❌ | 7 |
| Manual con F1, Acerca de, enlaces web | ❌ | 7 |
| Instalador | ❌ | 7 |

---

## 4. Especificación de accesibilidad

### 4.1 Reglas para **todos** los formularios

1. Cada control de entrada, lista o árbol lleva una **`Label` visible** con mnemónico, situada justo antes en el orden de tabulación.
2. Además se fija **`AccessibleName`** de forma explícita (texto de la etiqueta sin `&` ni `:`), y **`AccessibleDescription`** cuando haga falta explicar algo que una persona vidente deduce mirando (formato de un path, qué significa `%1`). Las propiedades disponibles son `AccessibleName`, `AccessibleDescription`, `AccessibleRole` y `AccessibleDefaultActionDescription` (esta última solo por código).
3. Si el texto de un control cambia en ejecución, **se actualizan también sus propiedades accesibles**.
4. `AccessibleRole` solo cuando el rol por defecto sea incorrecto (por ejemplo, el control de terminal).
5. Mnemónicos **sin duplicados** por formulario, en cada idioma. Lo comprueba un test.
6. `AcceptButton` y `CancelButton` siempre. Escape cierra todo diálogo.
7. Listas: Supr quita, **Insert agrega, Enter o F2 edita**, menú contextual accesible con la tecla Aplicaciones y Shift+F10, siempre hay un elemento seleccionado, y tras recargar se **conservan selección y foco**.
8. Ordenación de listas por teclado (entrada de menú "Ordenar por"), no solo con clic en la cabecera.
9. Validación: mensaje y **foco al campo erróneo**, cambiando antes a su pestaña si hace falta.
10. Todo cambio de estado sin reflejo en el foco se **anuncia** (§4.3).
11. Nada depende solo del color ni del ratón. Se respetan alto contraste y escalado.
12. Los textos salen de los `.resx`. La opción de idioma se aplica de verdad al arrancar.

### 4.2 Ventana de juego

Orden visual como el original: Mensajes arriba, Recibido en medio, Texto a enviar abajo. Foco inicial en Texto a enviar.

| Tab | Control | Etiqueta visible | `AccessibleName` | Notas |
|---|---|---|---|---|
| 0 | `LblInput` | `&Texto a enviar:` | — | Cursiva con modo movimiento activo |
| 1 | `TxtInput` | | Texto a enviar | Multilínea; en modo contraseña: "Contraseña" |
| 2 | `LblOutput` | `&Recibido:` | — | |
| 3 | `Terminal` | | Recibido | `AccessibleRole.Text`; solo lectura; navegable |
| 4 | `LblMessages` | `&Mensajes:` | — | Se oculta junto con el cuadro |
| 5 | `RtbMessages` | | Mensajes | Solo lectura |
| 6 | `BtnAction` | `&Cancelar` / `&Desconectar` / `&Cerrar` | igual al texto | `AccessibleDescription` explica qué hará |
| 7 | `BtnReconnect` | `Re&conectar` | Reconectar | Visible solo desconectado; con `TabStop` |
| — | Barra de estado | | Estado de la conexión · Tiempo conectado · Estado de mensajes | Cada panel con su nombre |

Mnemónicos de la ventana en español: T, R, M, C/D, y menús A (Acciones), E (Edición), H (Herramientas), U (Ayuda). En inglés se definen aparte y se comprueban con el mismo test.

El menú contextual para redimensionar paneles de la v2 pasa al menú **Ver**, con atajos, para que sea alcanzable por teclado.

**Requisito crítico del terminal**: al llegar texto, el cursor y la selección del usuario **no se mueven** salvo que la opción diga lo contrario (por defecto: ir al final solo si ya estaba al final). Hoy cada línea manda el cursor al final, lo que impide repasar con el lector. Además: recorte por líneas sin coste cuadrático, liberar fuentes, pintar inverso, tenue y tachado, y conservar el estado ANSI entre líneas.

### 4.3 Anuncios: UI Automation primero

```
IAnnouncer.Announce(texto, prioridad)
   1. UiaAnnouncer   → control.AccessibilityObject.RaiseAutomationNotification(...)
   2. NvdaAnnouncer / JawsAnnouncer  (DLL nativas que ya tiene la v2)   ← si UIA devuelve false o está desactivado
```

| Qué se anuncia | Procesamiento UIA | Equivalente original |
|---|---|---|
| Texto recibido del MUD | `All` (se encola) | No interrumpe |
| Lectura de mensaje (Ctrl+número, Ctrl+º), hora, línea del cuadro de envío | `ImportantMostRecent` | Interrumpe |
| Conexión, desconexión, errores | `ImportantAll` | — |
| Cambios de estado (silencio, movimiento, trigger activado, "OK") | `MostRecent` | Voz directa |

- `RaiseAutomationNotification` existe en .NET 10 y requiere Windows 10 1709 o posterior; devuelve `false` si no está disponible, y entonces se usa la DLL.
- Opción **Lector de pantalla**: Automático (UI Automation) — *por defecto* · JAWS · NVDA · Ninguno. El braille por NVDA de la v2 se mantiene.
- Se habla **solo con la ventana activa**, como el original. Se conservan las opciones de la v2 "anunciar texto del MUD" y "anunciar mensajes".
- Al lector se le envía siempre el texto **sin ANSI**.
- **Primera prueba técnica** (fase 0): comprobar con NVDA y con JAWS que las notificaciones UIA se leen con la ventana activa, que `All` encola y `ImportantMostRecent` interrumpe, que no hay doble lectura con el cuadro de Recibido, y cómo se comporta con mucho texto seguido. Si algún lector falla, para ese lector la DLL pasa a ser la vía por defecto. *No está verificado todavía; necesito tu oído para esto.*

### 4.4 Teclado final de la ventana de juego

Del original: F1 manual · F2 modo movimiento · F3 salvar · F4 abandonar · F5 alias · F6 triggers · F7 paths · F8 modo silencioso · F9 opciones · Ctrl+B buscar · Ctrl+S buscar siguiente · Ctrl+E seleccionar todo · Ctrl+C/X/V/Z · Ctrl+1…0 · Ctrl+º · Ctrl+F hora del mensaje · Escape · Alt+T/R/M · flechas de historial · Enter, Shift+Enter, Ctrl+Enter · NumPad 0-9.
De la v2, se conservan: Ctrl+L limpiar (equivale a `cls`) · Ctrl+K foco a la entrada · Ctrl+M silenciar (equivale a F8) · Alt+S callar la voz · Esc limpia la entrada.
Corrección: con Ctrl pulsado, el numpad **solo** lee el mensaje, no mueve.

Todos los atajos aparecen en los menús, de modo que se puedan descubrir con el lector.

---

## 5. Diseño técnico

### 5.1 `MudSession`: la pieza que falta

Hoy la lógica de sesión vive en `FrmClient` y los motores son singletons con estado compartidos entre ventanas (negociador telnet, triggers, alias, silencio del lector). Se crea en Core una clase **`MudSession`**, una por conexión, con su propio ámbito de inyección de dependencias, que posee sus motores y expone eventos a la interfaz. `FrmClient` queda como vista. Así la sesión se prueba sin ventana, y se arreglan de paso las fugas de memoria y el cruce de estado entre sesiones.

**Recepción**
```
bytes → Decoder con estado (codificación del MUD; no rompe caracteres partidos)
      → Telnet con estado (tolera secuencias partidas; responde WONT/DONT a lo no soportado; ECHO, GMCP)
      → retrocesos y \r
      → búfer de líneas  (línea completa, o vaciado a los ~150 ms si no llega \n: prompts)
      → MSP              (extrae y reproduce; elimina la línea)
      → si hay un om.get esperando: entrega y fin
      → ANSI             (estado persistente entre líneas)
      → triggers por línea  → [ocultar línea]      → triggers multilínea sobre el bloque
      → pintar en Recibido (cursor según opción)
      → regla de mensajes del MUD + GMCP → Mensajes
      → parpadeo si inactiva → log → anuncio (texto sin ANSI, all_speak:, modo silencioso)
```
A diferencia del original, los triggers se evalúan **antes** de pintar: es lo que permite la novedad de ocultar una línea.

**Envío** — mismo orden que el original:
`_path` → alias → triggers `@` → `±triggers` → `±trigger` → `uncalias` → `calias` → captura de grabación → comandos exactos → concatenación → repetición → envío con la codificación del MUD → log.
Mejora: alias y comandos se aplican a **cada** subcomando tras concatenar, con protección contra recursión.

### 5.2 Triggers y Lua

Modelo: nombre · desencadenante · tipo de patrón (literal, regex, comodines) · distinguir mayúsculas · **multilínea** · qué hará (**comando, sonido, ambas, script Lua**) · acción · sonido · activado · prioridad (v2) · **ocultar línea** (nuevo). Los de comando se reconocen por el `@` inicial, como en el original.

Correspondencia de la API. Se añade a las 9 funciones que ya existen; no se quita ninguna.

| Original | Lua | Notas |
|---|---|---|
| `OmSend(cmd)` | `om.send(cmd)` ✔ existe | Por el procesador de entrada completo |
| — | `om.sendraw(cmd)` | Directo al MUD, sin alias |
| `OmGet(cmd [, patrón] [, colores])` | `om.get(cmd, {pattern=, timeout=3, colors=false})` | Cola por sesión: varios scripts ya no se pisan. Devuelve `nil` si vence |
| `OmDisplay(texto)` | `om.display(texto)` ✔ | Reinyecta por el flujo completo |
| — | `om.echo(texto)` | Solo pinta, sin disparar triggers |
| `SayText(texto)` | `om.say(texto [, interrumpir])` y `om.notify` ✔ | |
| `AddMessage(texto)` | `om.message(texto)` | Muestra el cuadro si estaba oculto |
| `PlaySound(n [, rep])` | `om.playsound(n, {loop=, volume=, priority=})` | Búsqueda en 3 rutas: tal cual → carpeta del MUD → `sounds\` |
| `StopSound(n)` | `om.stopsound(n)` → booleano | |
| `OmSetVar` / `OmQueryVar` / `OmRemoveVar` / `OmVarIsSet` | `om.setvar` ✔ `om.getvar` ✔ `om.removevar` ✔ `om.isset` | |
| `StartCount(…)` | `om.countdown(ticks, {unit="s", during=, finish=})` | No bloqueante; cancelable |
| `GetLastActivity()` | `om.lastactivity()` | Segundos desde el último envío |
| `RemoveColours(t)` | `om.removecolors(t)` | |
| Constantes `Apearance*` | `om.ANSI_*` | También las de fondo, que el original no tenía |
| `Thread.Sleep` | `om.sleep(s)` | Cooperativo, máximo 10 s |
| — | `om.timer(nombre, s, fn)` · `om.canceltimer` | Nuevo |
| `args`, `FullCommand` | `om.args`, `om.command`, `om.captures` ✔ `om.line` ✔ `om.block` | El original **no** daba la línea ni los grupos de la regex |
| `Cln.*` (acceso total) | `om.mud.name`, `om.character.name` (solo lectura) | Sin acceso a la ventana |
| — | `om.gag()` · `om.log` ✔ · `om.status(t)` · `om.match(t, patrón)` | Nuevo |

Sandbox: sustituir el truco actual de inyectar `__guard()` por línea de texto — que deja `while true do end` en bucle infinito real y es redefinible desde el script — por ejecución en **corrutina de MoonSharp con `AutoYieldCounter`**, que cede cada N instrucciones y permite aplicar límite de instrucciones, tiempo y cancelación de forma fiable. Test obligatorio: bucle infinito en una sola línea. Borrar el volcado de 162 MB que dejó el test colgado.
Una excepción en un script se muestra y se registra; **nunca** cierra la aplicación.

Editor de trigger: cuadro de código **multilínea** que acepta Tab con salida por Ctrl+Tab (anunciada en su `AccessibleDescription`), botón **Probar** con una línea de ejemplo (nuevo), y etiqueta y `AccessibleName` que cambian a "Script Lua:" según el tipo, como hacía el original.

### 5.3 Sonido

`NAudioSoundPlayer` (NAudio + NAudio.Vorbis para ogg) en lugar del reproductor nulo. MSP conectado a la sesión. Carpeta de sonidos **por MUD**, con `%AppData%\Omnimud\sounds\<mud>` por defecto (el original exigía configurarla). Tablas de prioridad separadas para sonido, música y triggers. `C=1` del estándar aceptado. Opciones de sonido y música fuera de ventana. **Volumen general** (v2) conservado.
Descargas: HTTPS por defecto, intentando HTTPS cuando la URL llegue en HTTP, más una opción "Permitir descargas HTTP" porque los MUD antiguos publican sonidos sin TLS y el original lo permitía. Proxy aplicado a las descargas.
Corregir el fallo de `SoundManager` que bloquea para siempre los sonidos de menor prioridad. (Hecho: las prioridades las lleva `SessionSound`; `SoundManager` se retiró en la limpieza de 2026-09-19.)

### 5.4 Reglas de mensajes

En lugar de DLL, **conjuntos de reglas regex guardados como datos** (patrón → plantilla de mensaje), con su editor accesible. Se siembran las cuatro del original con sus patrones exactos (A2 §9). El MUD elige un conjunto o ninguno. Conviven con GMCP y con `om.message()`.

### 5.5 Opciones

`IOptionsService` tipado, con resolución personaje → MUD → global → valor por defecto, y notificación de cambios para que la ventana abierta se actualice (fuente incluida, que en el original no se aplicaba).
Contenido: las 23 del original (A2 §5.1) más las 10 de la v2. Seis pestañas como el original, más Idioma en General.
Ámbito: global desde el lanzador; personaje o MUD con F9 desde el juego. Mejora: el título indica el ámbito ("Opciones del personaje X") y hay una casilla **"Usar las opciones de <nivel superior>"** que permite volver a heredar, cosa imposible en el original.
Un solo juego de botones Aceptar, Cancelar, Exportar e Importar fuera de las pestañas.

### 5.6 Intercambio de datos

Formato nuevo: JSON con campo de versión, extensión `.omnimud`. Exporta MUD (con direcciones, reglas y, opcionalmente, personajes), personaje completo, alias, triggers, paths, movimientos y opciones. Importar pregunta por cada duplicado y muestra un resumen final. Importar **desde otro personaje**. Los scripts Lua importados no requieren firma porque corren en sandbox.

### 5.7 Plataforma

`net10.0` y `net10.0-windows` en los 5 proyectos · `Directory.Build.props`, `Directory.Packages.props` y `global.json` · `Microsoft.Data.Sqlite` y `Microsoft.Extensions.DependencyInjection` 10.x · registrar `CodePagesEncodingProvider` (sin él `windows-1252` falla) · **FluentAssertions 8 es de licencia comercial**: pasar a AwesomeAssertions, que es un fork compatible · MoonSharp se mantiene, con el sandbox rehecho · nuevo proyecto `Omnimud.UI.Tests`.

---

## 6. Fases

Cada fase termina con la compilación sin avisos, los tests en verde y una lista de comprobación manual con lector de pantalla.

### Fase 0 — Saneamiento · *pequeña*
- .NET 10 y ficheros de plataforma (§5.7).
- **`Main` síncrono con `[STAThread]`**: hoy es `async Task Main` y el hilo de interfaz queda en MTA, lo que rompe portapapeles, diálogos y la base de accesibilidad.
- Arreglos directos: TLS que no se aplica · columna `Encoding` que no se guarda y envío fijo en UTF-8 · contraseñas en el historial · el botón Aceptar de Opciones que no cierra · `try/catch` en todos los `async void` · excepción al sondear JAWS (`JFWGetVersion` no existe en la DLL) · fichero de clave maestra corrupto que impide arrancar · transacciones en el guardado de opciones y en "predeterminado".
- Retirar código muerto sin función: `FrmTEst`, `test_dbg.cs`, `ITextPipeline`. (Hecho.) `FrmMuds` y `FrmCharacters` se revisan en la fase 6: el lanzador en árbol ya cubre su función.
- `Omnimud.UI.Tests` con los tests genéricos de accesibilidad de §7. Empezarán en rojo, y eso es lo esperado.
- **Prueba técnica de UI Automation** (§4.3).

### Fase 1 — `MudSession` · *grande*
Sesión por conexión con su ámbito de DI; decodificador y telnet con estado; búfer de líneas con vaciado de prompts; ANSI persistente; filtrado de secuencias CSI que no son de color; líneas vacías conservadas; `FrmClient` reducido a vista; cierre ordenado. Tests nuevos: paquetes partidos (telnet, UTF-8, líneas), prompts, reconexión, dos sesiones simultáneas.

### Fase 2 — Ventana de juego accesible · *grande*
Todo §4.2 a §4.4 y §3.1: etiquetas y nombres accesibles · terminal con cursor según opción · `IAnnouncer` · lectura de mensajes (Ctrl+número con concatenación, Ctrl+º, Ctrl+F) con marca de tiempo y límite de 1000 · menús completos con atajos · cuadro de envío multilínea · Enter vacío · historial configurable con sonido · modo contraseña · escritura directa (sin espera activa) · abrir URL · Buscar · botón de acción · barra de estado con tiempo real · parpadeo · modo silencioso y `all_speak:` · cuadro de Mensajes que se oculta.
**Al acabar esta fase el cliente ya es usable para jugar con lector.**

### Fase 3 — Triggers y Lua · *grande*
Conectar `TriggerEngine` a la sesión · modelo ampliado (migración 004) · "ambas" · multilínea · triggers `@` · ocultar línea · sandbox rehecho · API completa de §5.2 · `om.get` con cola · temporizadores · comandos `±triggers` y `±trigger` · lista con Espacio y anuncio · editor con Probar · ejemplos de la ayuda original reescritos en Lua como tests.

### Fase 4 — Alias, paths, movimiento y comandos · *mediana*
Cargar alias por personaje · comandos `calias`/`uncalias` · diccionario de direcciones **por MUD** (migración 005) con su ventana y un botón "Cargar direcciones habituales" (n, s, e, o, ne, no, se, so, arriba, abajo; nuevo, porque el original partía de cero) · paths validados · `_nombre` y `-r` · grabación completa · teclas de movimiento con su ventana, F2 y herencia MUD → personaje · concatenación y repetición con sus opciones · `cls`, `callate`, `hablar` sincronizados con los menús.

### Fase 5 — Sonido, mensajes, opciones, logs y ciclo de vida · *grande*
§5.3, §5.4 y §5.5 · logs por día y por partida, sin ANSI, sin contraseñas, con nombres `yyyy-MM-dd` · proxy para descargas · modo de desconexión · confirmar al cerrar · "intentar guardar antes de salir": enviar `SaveCommand`, después `QuitCommand`, y esperar un instante antes de cortar · reconexión (v2) conservada.

### Fase 6 — Diálogos de gestión · *mediana*
Todos los formularios según §4.1: localizados, con mnemónicos y nombres accesibles, con selección y foco conservados · personaje predeterminado y "Conectar con…" · exportar, importar e importar desde otro personaje (§5.6) · avisos al guardar un path no invertible o duplicado · conexión rápida con TLS y codificación.

### Fase 7 — Servicios y distribución · *mediana; mecanismo por decidir*
Interfaces `IUpdateChecker` e `IReportSender` · información personal · manejador global de errores con informe · manual accesible en HTML local con F1, partiendo del contenido del CHM y actualizado a Lua · Acerca de y enlaces · instalador · referencia de la API Lua para usuarios.

### Fase 8 — Verificación de paridad
Repasar el documento 01 sección por sección contra la aplicación · sesión real en un MUD con NVDA y con JAWS · alto contraste y 200 % de escala · perfil de rendimiento con 10 000 líneas.

---

## 7. Cómo se verifica la accesibilidad

**Automático** (`Omnimud.UI.Tests`, recorriendo por reflexión todos los formularios):
- Todo control con `TabStop` tiene `AccessibleName` no vacío.
- Todo `TextBox`, `RichTextBox`, `ComboBox`, `ListView`, `TreeView`, `NumericUpDown` y terminal tiene una `Label` con `TabIndex` inmediatamente anterior en su mismo contenedor.
- No hay mnemónicos duplicados en un formulario, en español ni en inglés.
- Todo diálogo tiene `AcceptButton` y `CancelButton`.
- Todo elemento de menú con acción tiene texto localizado; los atajos no se repiten ni usan Insert ni Bloq Mayús, que son las teclas modificadoras de JAWS y NVDA.
- Ninguna cadena visible está escrita directamente en un `Designer.cs`.
- Terminal: añadir texto no mueve `SelectionStart` cuando el cursor no está al final.
- `IAnnouncer`: nunca recibe secuencias ANSI; respeta ventana inactiva, modo silencioso y `all_speak:`.

**Manual**: una lista de comprobación por fase, para NVDA y JAWS — recorrer con Tab, leer cada control, usar cada atajo, repasar texto mientras llega más, leer mensajes con Ctrl+número. Esta parte la tienes que validar tú; yo puedo dejar preparado el guion.

---

## 8. Riesgos

| Riesgo | Mitigación |
|---|---|
| Un lector no lee bien las notificaciones UIA, o lo hace con retraso cuando llega mucho texto | Prueba técnica en la fase 0; respaldo por DLL ya implementado; vía por defecto configurable por lector |
| MoonSharp sin mantenimiento | Queda aislado tras `IScriptEngine`; el sandbox se rehace con corrutinas; se puede cambiar de motor sin tocar la API `om.*` |
| El vaciado de prompts por tiempo añade latencia o parte líneas | Umbral configurable; los triggers multilínea cubren el caso límite |
| `RichTextBox` lento con mucho texto | Recorte por bloques, `BeginUpdate`, medición en la fase 8 |
| Evaluar triggers antes de pintar cambia el orden respecto al original | Solo afecta a la novedad de ocultar línea; `om.display` sigue reinyectando igual |

---

## 9. Pendiente de tu decisión (no bloquea las fases 0 a 2)

1. **"Intentar guardar antes de salir"**: el original enviaba el comando de *abandonar*. Propongo guardar y después abandonar.
2. **Orden de tabulación** de la ventana de juego: propongo Enviar → Recibido → Mensajes → botón. El original empezaba por el botón.
3. **Nombres de log** `yyyy-MM-dd` en lugar de `d-M-yyyy`.
4. **Proxy también para la conexión al MUD** (SOCKS5 o HTTP CONNECT): el original no lo tenía; sería un añadido.
5. **Huevo de pascua** de `dedicatoria.mp3`: no lo porto salvo que lo quieras.
6. Mecanismo de **actualizaciones e informes** (fase 7).

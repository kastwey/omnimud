# Auditoría de OMnimud v2 (solo lectura) — estado real a 2026-09-18

Ámbito: `C:\projects\OMnimud\v2` (todas las rutas de este informe son relativas a esa carpeta salvo que se indique). Se han leído completos todos los `.cs`, `.csproj`, `.sln`, `.resx` de `src\`, los `.csproj` y fixtures de `tests\`, `test_dbg.cs`, y se ha listado `dlls\`. De los ficheros de test se han leído completos los de sandbox Lua y el fixture SQLite; del resto se han inventariado todos los métodos de test (nombres) y se han contado. Documentos de contexto leídos: `trunk\PLAN_REESCRITURA_OMNIMUD.md` y `trunk\DOCUMENTACION_OMNIMUD.md`.

Nota sobre el plan: `PLAN_REESCRITURA_OMNIMUD.md` está corrupto entre las líneas 474 y 581: contiene dos pegados accidentales de un diff de VS Code de `SqliteOptionRepository.cs` (`<vscode_codeblock_uri>…`) que se comen el encabezado "2.4 Capa UI".

Únicas escrituras realizadas: `bin/` y `obj/` por `dotnet build`, y este informe. (Hay además `build.log` y `test.log` en el scratchpad.)

---

## 0. Veredicto en una frase

El **Core y la capa de datos están razonablemente hechos y testeados (249 tests verdes), pero casi nada está cableado a la UI**. `FrmClient` hoy es un cliente telnet con colores ANSI, eco, historial, GMCP de canales y lectura por JAWS/NVDA; **triggers, Lua, alias, paths, comandos, sonido/MSP, extractores de mensajes, opciones y TLS no funcionan de extremo a extremo** aunque existan sus clases. Además hay un bug de arranque grave (hilo principal MTA) y la accesibilidad de los formularios retrocedió al pasar al Designer (se perdieron `AccessibleName`, mnemónicos y localización que sí están en el `.resx`).

---

## 1. Proyectos, frameworks, paquetes y paso a .NET 10

### 1.1 Inventario

| Proyecto | TFM | Opciones | Paquetes | Evidencia |
|---|---|---|---|---|
| `src\Omnimud.Core` | `net9.0` | `ImplicitUsings`, `Nullable=enable`, `NeutralLanguage=en`, resx con `ResXFileCodeGenerator` | `MoonSharp 2.0.0.0` | `Omnimud.Core.csproj:4-11` |
| `src\Omnimud.Data` | `net9.0` | `ImplicitUsings`, `Nullable` | `Dapper 2.1.35`, `Microsoft.Data.Sqlite 9.0.1`; referencia a Core | `Omnimud.Data.csproj:4-15` |
| `src\Omnimud.UI` | `net9.0-windows` | `OutputType=WinExe`, `UseWindowsForms=true`, `Nullable`, `ImplicitUsings`, `NeutralLanguage=en`, `ApplicationHighDpiMode=PerMonitorV2`, `AssemblyName=Omnimud`, `RootNamespace=Omnimud.UI`; copia `..\..\dlls\{x64,arm64,x86}\*.dll` a `dlls\<arch>\` | `Microsoft.Extensions.DependencyInjection 9.0.1` | `Omnimud.UI.csproj:4-45` |
| `tests\Omnimud.Core.Tests` | `net9.0` | `IsPackable=false`, `Using Xunit` | `coverlet.collector 6.0.2`, `FluentAssertions 8.10.0`, `Microsoft.NET.Test.Sdk 17.12.0`, `NSubstitute 5.3.0`, `xunit 2.9.2`, `xunit.runner.visualstudio 2.8.2` | `Omnimud.Core.Tests.csproj:4-16` |
| `tests\Omnimud.Data.Tests` | `net9.0` | ídem + `Using FluentAssertions` | igual sin NSubstitute | `Omnimud.Data.Tests.csproj:4-15` |

Otros: `Omnimud.sln` (VS17) con 5 proyectos; `Omnimud.Data.Tests` está anidado por error en la carpeta de solución `src` (`Omnimud.sln:98`). No hay `global.json`, `Directory.Build.props`, `.editorconfig`, `.gitignore` ni repo git en `v2`. No existen los proyectos del plan `Omnimud.Updater`, `Omnimud.UI.Tests`, `Omnimud.Integration.Tests` ni `Omnimud.Plugin.SDK`. No hay NAudio, Serilog, M.E.Logging ni System.IO.Pipelines (todos previstos en el plan §7).

`dlls\` (nativas de lector de pantalla):

| Fichero | Tamaño | Versión |
|---|---|---|
| `dlls\x64\FSAPI.dll` | 65 656 | 27.4.21.0 |
| `dlls\x64\nvdaControllerClient.dll` | 262 808 | – |
| `dlls\arm64\FSAPI.dll` | 148 088 | 27.4.21.0 |
| `dlls\arm64\nvdaControllerClient.dll` | 239 256 | – |
| `dlls\x86\jfwapi.dll` | 50 456 | – |
| `dlls\x86\nvdaControllerClient.dll` | 210 584 | – |

### 1.2 Qué cambiar para .NET 10 (`net10.0` / `net10.0-windows`)

SDK disponible en la máquina: **9.0.314 y 10.0.303**; runtimes WindowsDesktop 8.0.x, 9.0.16, 10.0.11 y 10.0.12. Se puede migrar sin instalar nada.

1. `TargetFramework`: `net9.0` → `net10.0` en Core, Data y los dos proyectos de test; `net9.0-windows` → `net10.0-windows` en UI (5 ficheros, una línea cada uno).
2. Paquetes a alinear con la 10: `Microsoft.Data.Sqlite 9.0.1 → 10.0.x`, `Microsoft.Extensions.DependencyInjection 9.0.1 → 10.0.x`. Opcionales: `Dapper → 2.1.66`, `Microsoft.NET.Test.Sdk → 17.14+/18.x`, `xunit 2.9.3` (o migrar a xunit.v3), `xunit.runner.visualstudio 3.x`, `coverlet 6.0.4`.
3. **`FluentAssertions 8.x` cambió a licencia comercial (Xceed)**: gratis solo para uso no comercial. Decidir: quedarse en 7.x, aceptar la licencia o pasar a AwesomeAssertions/Shouldly.
4. `MoonSharp 2.0.0` es un paquete antiguo y sin mantenimiento (netstandard/net4x). Funciona en net9 y no hay motivo para que falle en net10, pero es deuda; además su límite de instrucciones no existe (de ahí el hack de `__guard`, ver §8).
5. Corregir **`Program.Main`** (ver §8.1): es independiente de la versión pero conviene hacerlo en la migración: `Main` síncrono con `[STAThread]`.
6. Registrar `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` si se mantiene `windows-1252` en el combo (`FrmAddEditMud.Designer.cs:165`); sin ello `Encoding.GetEncoding("windows-1252")` lanza en .NET moderno (`FrmClient.cs:112`).
7. C# 14 (`field` contextual): no hay identificadores `field` en el código (comprobado con búsqueda), sin riesgo.
8. WinForms 10: el analizador WFO1000 ya está satisfecho (`AnsiTerminalControl.cs:51,58` usan `DesignerSerializationVisibility`). No se usa BinaryFormatter ni Clipboard API propia.
9. Recomendable añadir `Directory.Build.props` (TFM, `Nullable`, `TreatWarningsAsErrors`, `LangVersion`) y `Directory.Packages.props` (gestión central de versiones), y un `global.json` fijando SDK 10.

---

## 2. Inventario funcional: implementado y cableado vs. solo clases

Leyenda: **Completo** = funciona de extremo a extremo; **Parcial** = funciona algo pero con huecos; **Sin cablear** = existe lógica (y a menudo tests) en Core/Data pero la UI no la usa; **Stub**; **Ausente**.

| Subsistema | Estado | Evidencia |
|---|---|---|
| **Conexión TCP** | **Completo** (básico) | `Core\Connection\TelnetConnection.cs:23-83` conexión async con timeout 15 s, bucle de recepción 4096 B (`:123-159`), eventos `DataReceived/Disconnected`. Usado en `FrmClient.cs:137-138`. Sin tests. `DisconnectReason.Timeout` nunca se emite. |
| **TLS** | **Sin cablear (roto en UI)** | Core lo implementa: `TelnetConnection.cs:44-61` (`SslStream`, TLS 1.2/1.3, opción de no validar certificado). BD: `Muds.UseTls` (`Migration001:11`). UI: checkbox `_chkTls` (`FrmAddEditMud.Designer.cs:91-99`), llega a la sesión (`FrmLauncher.cs:146`, `FrmClient.cs:104`) **pero `ConnectAsync` crea `new ConnectionConfig(_host, _port)` sin `UseTls`** (`FrmClient.cs:137`). `_useTls` no se lee nunca. `ClientCertificatePath` (`ConnectionConfig.cs:14`) no se usa. Sin tests. |
| **Telnet** | **Parcial** | `Core\Telnet\TelnetNegotiator.cs`: elimina IAC, extrae WILL/WONT/DO/DONT, subnegociación, IAC IAC. UI responde solo a `WILL GMCP` y `WILL/WONT ECHO` (`FrmClient.cs:183-209`); **no contesta WONT/DONT al resto** (TTYPE, NAWS, MSP, MCCP…). **No tolera secuencias partidas entre paquetes**: si `IAC WILL` cae al final del buffer se pierde la opción (`:59-69`), igual con `SB` (`:71-80`) e `IAC SE` (`:34`). El negociador es **singleton con estado** (`ServiceConfigurator.cs:41`): compartido entre sesiones simultáneas y no se reinicia al reconectar (si se corta en mitad de un SB, la sesión siguiente se traga todo el texto). 9 tests, ninguno de paquetes partidos ni GMCP. |
| **GMCP** (nuevo) | **Parcial, cableado** | Parseo `TelnetNegotiator.cs:40-45,128-138`; `BuildGmcpPacket` (`:114-126`). UI: acepta GMCP, envía `Core.Supports.Set ["Comm.Channel 1"]` (`FrmClient.cs:187-196`) y vuelca `Comm.Channel.Text` al panel de mensajes (`:211-265`). No envía `Core.Hello`. Resto de paquetes ignorados. |
| **ANSI** | **Completo en parser / Parcial en render** | `Core\Text\AnsiParser.cs`: SGR 0-9, 22-29, 30-37, 40-47, 90-97, 100-107, 38/48;5;n (256) y 38/48;2;r;g;b (TrueColor). 15 tests. Render (`AnsiTerminalControl.cs:102-120`): color de texto/fondo, bold, italic, underline. **No pinta** dim, blink, **inverse**, hidden ni strikethrough. El estado ANSI **se reinicia en cada línea** (`Parse` arranca con `AnsiStyle.Default`, `AnsiParser.cs:18`), así que un color abierto en una línea no continúa en la siguiente. Secuencias CSI que no sean `m` (cursor, borrado) no se filtran y aparecen como basura. |
| **MSP / sonido** | **Sin cablear + Stub** | `MspExtractor` (`Core\Text\MspExtractor.cs`, 12 tests) **no se usa en ningún sitio de `src`**. `ISoundManager` se inyecta en `FrmClient` (`FrmClient.cs:26,58`) y **nunca se invoca**. **Solo existe `NullSoundPlayer`** (`Core\Sound\NullSoundPlayer.cs:8-17`), registrado en `ServiceConfigurator.cs:56`; no hay NAudio ni ningún reproductor real. `SoundManager` resuelve rutas saneadas, prioridades y descarga (`SoundManager.cs:40-107`); `HttpSoundDownloader` solo HTTPS, lista blanca de extensiones, tope 10 MB (`ISoundDownloader.cs:33-75`). Directorio de sonidos fijo `%AppData%\Omnimud\sounds` (`ServiceConfigurator.cs:59-62`); `Muds.SoundDirectory` se ignora. No se negocia la opción telnet MSP. Los `!!SOUND(...)` aparecen como texto en pantalla. |
| **Triggers** | **Sin cablear** | Core: `TriggerMatcher` (literal con anclas `^ $`, regex con timeout 100 ms, sscanf), `TriggerEngine` (prioridad, enable/disable, global on/off), `TriggerActionExecutor` (comando con `%1..%n`, sonido, script). 43 tests. Datos+UI CRUD: `FrmTriggers`, `FrmAddEditTrigger`, `SqliteTriggerRepository`. **Pero**: `LoadTriggers` no se llama nunca desde la UI; `FrmClient.cs:284` hace `var matches = _triggerEngine.Process(rawLine);` y **descarta el resultado**; `ITriggerActionExecutor` no se inyecta en `FrmClient`. No existe mapeo `TriggerEntity → TriggerDefinition`. Falta el tipo "regex con reemplazo" del original. |
| **Lua** | **Core completo (API reducida) / Sin cablear** | `Core\Scripting\LuaScriptEngine.cs`. API **exacta** expuesta (`:88-153`): `om.send(cmd)`, `om.display(text)`, `om.notify(text)`, `om.setvar(name, value)`, `om.getvar(name)`, `om.removevar(name)`, `om.log(msg)` (va a display con prefijo `[LOG] `), `om.line` (string), `om.captures` (tabla base 1). Global interno `__guard`. Se eliminan `io, os, debug, load, loadfile, dofile, require, loadstring` (`:76-83`) sobre `Preset_SoftSandbox`. **Faltan del plan §3.2.4**: `om.send_noecho`, `om.get`, `om.message`, `om.status`, `om.playsound`, `om.stopsound`, `om.removecolors`, `om.match`, `om.timer`, `om.canceltimer`, `om.sleep`, constantes `om.ANSI_*`, y el límite de memoria. `IScriptEngine` se inyecta en `FrmClient` (`:29,61`) y no se usa. 23 tests. Ver fallos del sandbox en §8.3. |
| **Alias** | **Sin cablear (CRUD sí)** | `AliasResolver` (12 tests) se llama en `FrmClient.cs:461`, **pero `Load()` nunca se invoca**: el diccionario está siempre vacío. CRUD por personaje completo (`FrmAliases`, `FrmAddEditAlias`, `SqliteAliasRepository`). Sin `calias`/`uncalias`. No hay mapeo `AliasEntity → AliasDefinition`. |
| **Paths** | **Sin cablear (CRUD sí)** | `PathEngine` expand/collapse/reverse/validate (11 tests). Ejecución `_nombre` / `_nombre -r` en `CommandProcessor.cs:55-82`, pero el diccionario de paths inyectado es **un `Dictionary` vacío singleton** (`ServiceConfigurator.cs:46`) y `CommandProcessor` solo se invoca si la entrada empieza por `#` (`FrmClient.cs:449`). CRUD: `FrmPaths`, `FrmAddEditPath` (no valida con `IsValid`). |
| **Grabación de paths** | **Ausente** | No hay ningún código de `paths iniciar/detener/…`. Solo existe `PathEngine.Collapse` como pieza. |
| **Diccionario de direcciones** | **Sin cablear, sin UI, sin datos** | `DirectionDictionary` (14 tests), `SqliteDirectionRepository` (4 tests), tabla `Directions`. No hay formulario, nadie llama a `Load`, y **ninguna migración siembra direcciones** (n/s/e/o…), así que `Reverse` devolvería siempre `null`. `DirectionEntity.Abbreviation` es `string` y `DirectionEntry.Abbreviation` es `char`, sin conversor. |
| **Movimientos numpad** | **Stub de datos** | Tabla `Movements`, `MovementEntity`, `SqliteMovementRepository` (3 tests). **`IMovementRepository` ni siquiera está registrado en DI** (`ServiceConfigurator.cs:31-37`). Sin formulario, sin F2, sin manejo de NumPad. |
| **Comandos internos** | **Stub** | `FrmClient.cs:449-458` solo consulta `CommandProcessor` si empieza por `#` y solo actúa si el resultado es `Handled`, cosa que `CommandProcessor` **no devuelve nunca** (`CommandProcessor.cs:28-50`). No existen `cls`, `callate`, `hablar`, `calias`, `uncalias`, `paths …`, `+/-triggers`, `+/-trigger`. Concatenación (`;`) y repetición (`3#cmd`) están implementadas y testeadas en Core (`:34-46`) pero DI crea el procesador con ambos caracteres a `null` y sin opciones que los configuren. |
| **Historial** | **Completo (básico)** | `FrmClient.cs:44-45,443-446,482-495`: Flechas arriba/abajo, 500 entradas fijas (no configurable), Esc limpia. Entrada vacía **no** reenvía el último comando (`:440`). No se anuncia al lector el comando recuperado (lo leerá el lector por el cambio del TextBox). **Las contraseñas tecleadas con eco desactivado también entran al historial** (`:443` antes de mirar `_echoOff`). |
| **Mensajes / extractores** | **Parcial** | Panel `_rtbMessages` alimentado **solo por GMCP** (`FrmClient.cs:233-265`). `MessageProcessor` + `RegexMessageExtractor` (12 tests) existen, se llama a `Process` (`:287`), pero **nadie registra extractores** (`RegisterExtractor` no está en la interfaz ni se invoca), y si extrajera algo tampoco lo añadiría al panel (solo lo verbaliza, `:288-291`). Sin `Ctrl+1..0`, sin `Ctrl+\`, sin tope de 1000 (el RichTextBox de mensajes crece sin límite). `Muds.ProcessRule` sin uso; no hay plugins ni equivalentes a CBalzhur/CCallandor/CCyberlife/CSimauria. |
| **Lectores de pantalla** | **Completo en Core / Parcial en uso** | `ScreenReaderApi` (autodetección JAWS→NVDA, re-sondeo cada 2 s, `Muted`, `Speak/StopSpeech/Braille`), `JawsScreenReader`, `NvdaScreenReader`, P/Invoke con resolver por arquitectura (`NativeLibraryResolver.cs`). 11 tests con mocks. Cableado: anuncia conexión (`FrmClient.cs:140`), desconexión (`:324`), mensajes GMCP (`:264`) y **todas las líneas del MUD** (`:295`). Problemas: se verbaliza `rawLine` **con los códigos ANSI dentro**; se habla aunque la ventana no tenga el foco; la opción "ScreenReader" y "Announce…" de `FrmOptions` no se consultan; no hay Window-Eyes/SAPI; no hay marcador `all_speak:`. Ver §8.4 sobre `JFWGetVersion`. |
| **Opciones y herencia** | **Stub funcional** | `SqliteOptionRepository` soporta ámbito (0 global / 1 MUD / 2 personaje) con `ScopeId` (7 tests). `FrmOptions` guarda 10 claves **solo en global** (`FrmOptions.cs:202-211`): Language, ScreenReader, MaxLines, FontFamily, FontSize, SoundEnabled, Volume, AnnounceMessages, AnnounceMudText, FlashOnMessage. **Ninguna clase lee esas opciones** (ni `FrmClient`, ni `Program`). No hay servicio de resolución Personaje→MUD→Global→defecto. Faltan casi todas las opciones del original (posición del cursor, nº de comandos, confirmar/guardar al salir, negociación telnet, logs, proxy, caracteres especiales, reproducir fuera de ventana, descarga de sonidos). Además **el botón OK no cierra el diálogo** (§8.5). |
| **Logs** | **Ausente** | Ninguna clase de log de sesión ni de diagnóstico. |
| **Proxy** | **Stub** | Solo el record `ProxyConfig` (`ConnectionConfig.cs:17-20`); `TelnetConnection` lo ignora. |
| **Import/Export XML** | **Ausente** | Ningún código. Tampoco migración desde el Registro de la v1 ni `ITriggerMigrator`. |
| **Búsqueda de texto** | **Ausente** | No hay FrmFind ni Ctrl+B / Ctrl+S. |
| **Flash de ventana** | **Stub** | Solo la casilla y la clave `FlashOnMessage` (`FrmOptions.cs:78,211`). Sin `FlashWindowEx`. |
| **Login automático** | **Parcial, cableado** | Vía *login script* (`FrmClient.cs:358-382`): tras conectar envía cada línea con `%character` y `%password` sustituidos, 250 ms entre líneas, sin esperar al prompt. La contraseña se descifra en `FrmLauncher.cs:141-145`. Conectar sobre un nodo MUD no usa el personaje predeterminado (`FrmLauncher.cs:118-119` pasa `character: null`); `Muds.DefaultCharacterId` no se usa. |
| **Encoding** | **Roto de extremo a extremo** | UI: combo utf-8 / iso-8859-1 / windows-1252 / ascii. Entidad `MudEntity.Encoding`, migración 002. **`SqliteMudRepository.AddAsync` y `UpdateAsync` no incluyen la columna `Encoding`** (`SqliteMudRepository.cs:41-42` y `:52-55`): siempre queda `'utf-8'`. Recepción sí usa `_encoding` (`FrmClient.cs:170`), pero **el envío está fijado a UTF-8** (`TelnetConnection.cs:103`). Se decodifica por trozos sin `Decoder`, así que un carácter multibyte partido entre lecturas se corrompe. Sin test que cubra `Encoding` ni `LoginScript` en el repositorio. |
| **Login script** | **Completo (persistencia + ejecución)** | Migración 003, `SqliteMudRepository.cs:41,53`, `FrmAddEditMud` (multilínea), ejecución descrita arriba. La ayuda `Muds_LoginScriptHint` existe en resx pero no se muestra. |
| **Quick connect** | **Completo** | `FrmQuickConnect` (host + puerto) → `FrmLauncher.cs:126-136`. Sin TLS ni encoding. Es el único formulario con `AccessibleName` y localización aplicados. |
| **Cifrado de contraseñas** (nuevo) | **Completo** | `AesPasswordProtector` AES-256-GCM + HKDF por sal (9 tests); clave maestra de 32 B protegida con DPAPI en `%AppData%\Omnimud\master.key` (`MasterKeyProvider.cs`). Si ese fichero se corrompe, la app no arranca (excepción al resolver `FrmLauncher`). No se puede borrar una contraseña guardada desde la UI (`FrmAddEditCharacter.cs:18`). |
| **Multisesión** | **Parcial** | Cada conexión abre un `FrmClient` no modal (`FrmLauncher.cs:160`), pero comparten singletons con estado (negociador telnet, motor de triggers, alias, mensajes, mute del lector). No hay pestañas. |
| Otros del original | **Ausentes** | Mapa, información personal, informes de error, auto-actualización, confirmar al salir / guardar al salir, modo offline, línea múltiple (Shift+Ctrl+Enter), temporizador de la barra de estado. |

---

## 3. `FrmClient` a fondo

### 3.1 Controles (todo en `Forms\FrmClient.Designer.cs`)

| Control | Tipo | TabIndex / TabStop | Texto | Notas |
|---|---|---|---|---|
| `_menuStrip` | MenuStrip | 0 | – | `:38-42` |
| `_menuMud` | menú | – | `&MUD` | `:46-49` |
| `_miAliases` / `_miTriggers` / `_miPaths` / `_miOptions` | ítems | – | `&Aliases`, `&Triggers`, `&Paths`, `&Options` | `:53-77`. **Sin `ShortcutKeys`**. Textos en inglés fijo. Los tres primeros no hacen nada si no hay personaje (`FrmClient.cs:516,524,532`), sin avisar. |
| `_splitContainer` | SplitContainer horizontal | 1, `TabStop=false`, `IsSplitterFixed=true` | – | `:81-98` |
| `_terminal` | `AnsiTerminalControl` (Panel1) | 0 | – | `:102-106` |
| `_rtbMessages` | RichTextBox `ReadOnly` (Panel2) | 0 | – | `:110-119`, Consolas 9.75, fondo oscuro |
| `_inputPanel` | Panel inferior | 2 | – | `:123-129` |
| `_txtInput` | TextBox **una sola línea** | 0 | – | `:133-138`, `KeyDown` |
| `_btnReconnect` | Button | 1, `TabStop=false`, oculto | `Reconnect` | `:142-151` |
| `_statusStrip` | StatusStrip | 3 | – | con `_lblMudName`, `_lblConnectionStatus` ("Disconnected"), `_lblLineCount` ("Lines: 0") `:155-176` |

Orden de tabulación efectivo: salida → mensajes → entrada (→ Reconnect cuando aparece). El foco inicial va a la entrada tras conectar (`FrmClient.cs:127`). Visualmente la salida va arriba y los mensajes debajo (al revés que el original).

### 3.2 Etiquetas y accesibilidad

- **No hay ningún `Label`** en `FrmClient`: ni para la salida, ni para mensajes, ni para la entrada.
- **No hay ningún `AccessibleName`, `AccessibleDescription` ni `AccessibleRole`** en el formulario ni en `AnsiTerminalControl` (búsqueda global: solo aparecen en `FrmOptions.cs` y `FrmQuickConnect.cs`). Con NVDA/JAWS los tres cuadros se anuncian como "edición" / "edición solo lectura" sin nombre.
- Las cadenas pensadas para ello **existen pero no se usan**: `Client_OutputAccessible` ("Recibido"), `Client_InputAccessible` ("Enviar"), `Client_MessagesAccessible` ("Mensajes"), `Client_StatusAccessible`, `Client_Reconnect`.
- El menú contextual de tamaño de paneles (`FrmClient.cs:88-98`) cuelga del `SplitContainer`, que no recibe foco y está tapado por controles `Dock=Fill`: **inalcanzable por teclado** (solo clic derecho sobre el divisor de 4 px).

### 3.3 Teclado

`TxtInput_KeyDown` (`FrmClient.cs:409-433`): Enter envía; Arriba/Abajo historial; Esc limpia.

`ProcessCmdKey` (`:545-578`), global al formulario:

| Atajo | Acción |
|---|---|
| Ctrl+L | limpia la salida |
| Ctrl+K | foco a la entrada |
| Ctrl+M | silencia/activa el lector (sustituye al F8 del original) |
| Alt+S | detiene la voz |
| F3 | envía el comando de guardar del MUD |
| F4 | envía el comando de salir del MUD |

`KeyPreview=true` está activado pero no hay manejador `KeyDown` de formulario. No existen: F2, F8, NumPad, Ctrl+1..0, Ctrl+\, Ctrl+B, Ctrl+S, Shift+Ctrl+Enter, ni atajo para saltar a salida/mensajes. El comentario "User can silence with Ctrl" (`:294`) no corresponde a ningún código.

### 3.4 Cómo se pinta el texto: `Controls\AnsiTerminalControl.cs`

- **No hereda de `RichTextBox`**: es un `UserControl` (`:12`) que contiene un `RichTextBox` privado `Dock=Fill`, `ReadOnly=true`, `WordWrap=false`, barra vertical forzada, `HideSelection=false`, `ShortcutsEnabled=true`, Consolas 10, negro/gris (`:31-45`).
- **Legible con lector de pantalla: sí en principio.** Es un RichEdit estándar de solo lectura: recibe foco, admite cursores, selección, Ctrl+C, revisión por líneas/palabras. No es un control pintado a mano.
- **Navegable con cursor: sí, pero se rompe al llegar texto.** Cada `AppendLineInternal` hace `SelectionStart = TextLength` (`:92-93`, `:106-107`, `:130-131`): **el cursor y la selección del usuario se van al final con cada línea recibida**. Si no estaba al fondo se restaura el *scroll* (`:136`) pero no el cursor. Para un usuario ciego que repasa el historial con flechas mientras el MUD sigue enviando texto, esto es inutilizable. El original tenía la opción "posición del cursor" (ir al final / mantener / según posición) que aquí no existe.
- `WordWrap=false` sin barra horizontal: las líneas largas se cortan visualmente.
- Negrita/cursiva/subrayado: se crea un `new Font` por segmento y no se libera (`:116-117`), y no se restablece la fuente para el segmento siguiente, por lo que el estilo tiende a "sangrar" al texto posterior.
- `TrimLines` (`:182-194`) evalúa `_rtb.Lines` hasta tres veces **por cada línea recibida**; esa propiedad reconstruye el array completo. Con 10 000 líneas el coste es cuadrático.
- `GetAccessibleText` (`:172-180`) no lo usa nadie. `MaxLines` y `TerminalFont` existen pero nada los conecta con las opciones.
- Las líneas vacías del MUD se descartan (`FrmClient.cs:177`) y **no hay buffer de línea entre paquetes**: una línea partida entre dos lecturas TCP se pinta (y se evalúa en triggers, y se verbaliza) como dos líneas.

### 3.5 Cómo se anuncia el texto al lector

- Vía API directa, no vía eventos UIA/MSAA ni *live regions*: `ScreenReaderApi.Speak` → `JFWSayString` o `nvdaController_speakText` (con `cancelSpeech` previo si `interrupt`).
- Cada línea recibida se encola con `interrupt:false` (`FrmClient.cs:295`); conexión y desconexión con `interrupt:true`; mensajes GMCP como "{0} dice: {1}" (`:264`).
- Se envía la línea **cruda con secuencias ANSI**. No se comprueba foco de ventana ni las opciones. Braille (`ScreenReaderApi.Braille`) no se usa. El eco local del comando no se verbaliza (correcto).
- La barra de estado no se anuncia salvo por los `Speak` anteriores.

---

## 4. Resto de formularios

Rasgos comunes: todos tienen `Font Segoe UI 9.75`, `AutoScaleMode.Font`. Los diálogos de edición son `FixedDialog`, con `AcceptButton`/`CancelButton` y `DialogResult` correctos, y **cada campo va precedido de su `Label` con TabIndex inmediatamente anterior** (así el lector sí nombra los campos). Pero: **textos en inglés fijos en el Designer**, **sin mnemónicos (`&`)**, **sin `AccessibleName`**, y las validaciones solo muestran un `MessageBox` sin llevar el foco al campo. Los manejadores `async void` de alta/edición no capturan excepciones: un duplicado (restricción `UNIQUE`) tumba la aplicación. Tras cada operación las listas se recargan y **se pierde la selección y el foco** (salvo en el árbol del lanzador).

| Formulario | Controles y orden | Accesibilidad | Qué funciona |
|---|---|---|---|
| **FrmLauncher** (`.cs`, `.Designer.cs`) | Label "MUDs && Characters:" (0), TreeView MUD→personajes (1), botones Connect (2), Quick Connect (3), Add MUD (4), Edit (5), Delete (6), Add Char (7), Set Default (8), Label de estado (9) | El árbol queda nombrado por la etiqueta. Teclas en árbol: Enter conecta, Supr borra, F2 edita (`FrmLauncher.cs:91-109`). Sin menú, sin mnemónicos, sin `AcceptButton`. 23 cadenas `Launcher_*` sin usar (incl. `Launcher_TreeAccessible`). | CRUD completo de MUDs y personajes con confirmación, predeterminado, conectar, quick connect. Conserva la selección al recargar. Es el formulario principal: al cerrarlo termina la aplicación. |
| **FrmQuickConnect** | Label Server (0), TextBox (1), Label Port (2), NumericUpDown 1-65535 def. 23 (3), Connect (4), Cancel (5) | **El único bien hecho**: `ApplyLocalization()` pone textos con mnemónico y `AccessibleName` desde resx (`FrmQuickConnect.cs:17-26`). Si el host está vacío devuelve el foco al campo. | Completo. |
| **FrmAddEditMud** | Name, Host, Port (def. 23), CheckBox "Use TLS", Save command, Quit command, Login script (multilínea, `AcceptsReturn`), Encoding (combo), OK, Cancel; TabIndex 0-16 correlativos | Etiquetas asociadas por orden. Sin accesibles ni mnemónicos (existen `Muds_*Label/Accessible` sin usar). | Alta/edición. **TLS y Encoding no tienen efecto** (§2). Sin campos para directorio de sonidos ni regla de proceso (columnas existentes). |
| **FrmAddEditCharacter** | Name, Password (`UseSystemPasswordChar`), OK, Cancel | Igual. La pista `Characters_PasswordHint` no se muestra. | Alta/edición; contraseña en blanco = conservar; no se puede borrar. No permite cambiar de MUD. |
| **FrmAliases** | ListView Details (Command, Action, On) (0), Add, Edit, Delete, Close | **La lista no tiene etiqueta ni `AccessibleName`**. Columna "On" con "✓". Sin casillas, sin ordenar, sin doble clic/Enter/Supr. `Esc` cierra. Título fijo "Aliases" (el nombre que recibe es el del MUD, y no se usa). | CRUD completo en BD. **No afecta a la sesión en curso.** |
| **FrmAddEditAlias** | Command, Action, CheckBox Enabled, OK, Cancel | Correcto por orden de etiquetas. | Funciona. |
| **FrmTriggers** | ListView (Name, Pattern, Type, Action, On, Pri) (0), Add, Edit, Delete, Toggle, Close | Igual que alias; tipos mostrados en inglés fijo (`FrmTriggers.cs:30-44`). | CRUD + activar/desactivar en BD. Sin efecto en el juego. |
| **FrmAddEditTrigger** | Name, Pattern, Pattern type (Literal/Regex/Sscanf), Action type (Command/Sound/Script), Action, Sound (deshabilitado salvo tipo Sound), Priority, Case sensitive, Enabled, OK, Cancel; TabIndex 0-17 | Etiquetas por orden. | Funciona como editor. **El cuadro Action es de una sola línea** (no hay `Multiline`): no se puede escribir un script Lua de varias líneas, y eso además anula el guardián de instrucciones (§8.3). Sin validación de regex ni prueba de patrón. Sin examinar para el sonido. |
| **FrmPaths / FrmAddEditPath** | ListView (Name, Path) + Add/Edit/Delete/Close; Name, Path, OK, Cancel | Igual que alias. | CRUD en BD. No valida contra diccionario. |
| **FrmOptions** | TabControl creado por código con 3 pestañas: General (Language, Screen reader, 3 casillas), Display (Max lines, Font, Font size), Sound (Enable, Volume); OK, Cancel | Aquí sí hay `Label` + `AccessibleName` localizados en los 6 campos (`FrmOptions.cs:50,63,102,113,128,154`). Los controles creados por código no fijan `TabIndex` (dependen del orden de inserción, que es correcto). Título "Options" fijo. | Carga y guarda en ámbito global. **OK no cierra** y **nada consume las opciones**. |
| **FrmMuds**, **FrmCharacters** | Listas con CRUD equivalentes | – | **Código muerto**: nunca se instancian (`new FrmMuds`/`new FrmCharacters` no aparecen). Los sustituyó el árbol del lanzador. |
| **FrmTEst** | Formulario vacío 800×450 | – | **Código muerto** (plantilla de VS creada el 21-05-2026). |

Los `.resx` por formulario (`FrmCharacters`, `FrmLauncher`, `FrmMuds`, `FrmQuickConnect`, `FrmTEst`) están vacíos (0 entradas `data`).

---

## 5. Funcionalidades nuevas de la v2 que hay que preservar

1. **TLS 1.2/1.3** con `SslStream` y opción de no validar certificado (`TelnetConnection.cs:44-61`) + columna/casilla `UseTls`. (Arreglar el cableado.)
2. **Encoding por MUD** (utf-8, iso-8859-1, windows-1252, ascii): migración 002, entidad y combo. (Arreglar persistencia, envío y proveedor de code pages.)
3. **Login script por MUD** con `%character` y `%password` (migración 003; `FrmClient.cs:358-382`).
4. **Quick connect** (`FrmQuickConnect`).
5. **Lua en sandbox** (MoonSharp) con API `om.*`, variables de sesión y límites de tiempo/instrucciones; sustituye a C#/VB compilado.
6. **ANSI 256 colores, TrueColor y colores brillantes 90-97/100-107** (parser + conversión en `AnsiTerminalControl.cs:241-256`).
7. **GMCP**: negociación, `Core.Supports.Set`, y canal `Comm.Channel.Text` → panel de mensajes con color por canal y anuncio al lector.
8. **Cifrado AES-256-GCM + HKDF + DPAPI** de contraseñas (sustituye al cifrado por sustitución).
9. **Descarga de sonidos segura**: solo HTTPS, extensiones permitidas, tope de tamaño, nombre de fichero saneado contra *path traversal*.
10. **Lectores**: autodetección en caliente JAWS/NVDA, **salida braille por NVDA**, DLL nativas **x64/arm64/x86** con resolver propio, silenciar (Ctrl+M) y parar voz (Alt+S).
11. **Triggers**: prioridad numérica, timeout de regex 100 ms (anti-ReDoS), sscanf con anchos (`%3d`), activar/desactivar individual y global; **alias con flag Enabled**.
12. **Terminal**: *scroll* inteligente (no salta si el usuario está leyendo arriba), tope de líneas, panel de mensajes redimensionable/ocultable.
13. **Sesión**: botón Reconnect con foco automático al caer la conexión, F3 guardar / F4 salir, Ctrl+L, Ctrl+K, Esc limpia entrada, eco local suprimido con `WILL ECHO`.
14. **Lanzador unificado** en árbol MUD→personajes con Enter/Supr/F2; **varias sesiones simultáneas** en ventanas.
15. **Infraestructura**: SQLite con migraciones versionadas, WAL y claves foráneas con borrado en cascada; DI; localización en/es por resx; DPI PerMonitorV2; concatenación y repetición con tope de 100 en `CommandProcessor`.

---

## 6. Esquema SQLite real y desajustes

Ubicación: `%AppData%\Omnimud\omnimud.db` (`ServiceConfigurator.cs:25-28`). Migraciones en `Data\Migrations\`, ejecutadas al arrancar (`Program.cs:22-26`), versión final **3**.

- **001** (`Migration001_InitialSchema.cs`): `Muds` (Id, Name UNIQUE, Host, Port, UseTls, SaveCommand, QuitCommand, ProcessRule, SoundDirectory, DefaultCharacterId, CreatedAt, UpdatedAt); `Characters` (Id, MudId FK cascade, Name, EncryptedPassword BLOB, IsDefault, fechas, UNIQUE(MudId,Name)); `Aliases` (…, Enabled, UNIQUE(CharacterId,Command)); `Triggers` (Id TEXT PK, CharacterId FK, Name, Pattern, PatternType, Action, ActionType, Sound, Enabled, CaseSensitive, Priority, fechas); `Paths` (UNIQUE(CharacterId,Name)); `Directions` (Direction UNIQUE, Abbreviation, OppositeDirection); `Movements` (UNIQUE(CharacterId,KeyCode)); `Options` (Scope, ScopeId, Key, Value, UNIQUE(Scope,ScopeId,Key)); 6 índices.
- **002**: `ALTER TABLE Muds ADD COLUMN Encoding TEXT NOT NULL DEFAULT 'utf-8'`.
- **003**: `ALTER TABLE Muds ADD COLUMN LoginScript TEXT`.
- `SchemaVersion(Version, AppliedAt)` creada por `MigrationRunner.cs:29-38`; cada migración en su transacción.

Desajustes y observaciones:

1. **`Muds.Encoding` no se escribe nunca** (`SqliteMudRepository.cs:41-42,52-55`). Bug funcional, sin test.
2. `Muds.ProcessRule`, `SoundDirectory`, `DefaultCharacterId`: se persisten pero no hay UI ni lógica que los use. `DefaultCharacterId` no tiene FK y duplica `Characters.IsDefault`.
3. `Options`: `UNIQUE(Scope,ScopeId,Key)` no protege las filas globales (`ScopeId NULL`); el repositorio lo suple con DELETE+INSERT **sin transacción** (`SqliteOptionRepository.cs:32-41`).
4. `SetDefaultAsync` hace dos UPDATE sin transacción (`SqliteCharacterRepository.cs:58-68`).
5. Las entidades rellenan `CreatedAt/UpdatedAt` en la UI, pero el SQL usa siempre `datetime('now')`.
6. `Directions` sin datos semilla; `Abbreviation` TEXT/`string` frente a `char` en Core.
7. **No existe ninguna capa de mapeo Entidad → tipo de Core** (Trigger, Alias, Path, Direction): es la pieza que falta para cablear.
8. Frente al plan §2.2.3: `PathDictionary` se llama `Directions`; no hay `Characters.PasswordSalt` (la sal va dentro del blob: correcto); no hay `Triggers.ScriptLanguage` ni `SessionVariables`; se añadieron `Encoding` y `LoginScript`.
9. `SqliteConnectionFactory.Create()` abre una conexión nueva y lanza `PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON` en **cada** operación (`IDbConnectionFactory.cs:19-30`). El fixture de tests no usa WAL.
10. `IMovementRepository` sin registrar en DI.

---

## 7. Compilación y tests (ejecutado hoy)

- `dotnet --list-sdks` → `9.0.314`, `10.0.303`.
- `dotnet build v2\Omnimud.sln` → **compila, 0 errores, 1 warning**: `FrmClient.Designer.cs(5,50): CS0414 The field 'FrmClient.components' is assigned but its value is never used` (el Designer de `FrmClient` no tiene `Dispose`; está en `FrmClient.cs:590`). Tiempo 8,3 s. El ejecutable sale como `src\Omnimud.UI\bin\Debug\net9.0-windows\Omnimud.dll`.
- `dotnet test v2\Omnimud.sln --no-build` → **249 tests, 249 pasan, 0 fallan, 0 omitidos**: `Omnimud.Core.Tests` 205 (0,6 s) y `Omnimud.Data.Tests` 44 (0,4 s).

Cobertura por áreas (métodos `[Fact]/[Theory]`): alias 12, comandos 10, mensajes 12, paths 25, Lua 23, seguridad 9, sonido 11, telnet 9, ANSI 15, MSP 12, triggers 43, lector 11, migraciones 3, repositorios 41.

Huecos: **ningún test de UI/accesibilidad**, ninguno de `TelnetConnection`/TLS, ninguno de paquetes telnet partidos ni GMCP, ninguno de `Encoding`/`LoginScript` en el repositorio, ninguno de integración del flujo recibir→trigger→acción. Un test de sandbox es tautológico (`LuaScriptEngineSandboxTests.cs:98-113` comprueba que no aparezca una cadena que el script nunca envía).

Resto de una ejecución anterior colgada: `tests\Omnimud.Core.Tests\TestResults\7d1ce992-…\testhost_15776_20260519T145004_hangdump.dmp` (**162 MB**) con `Sequence_….xml` que señala `Sandbox_InfiniteLoop_ExceedsInstructionLimit` como test no completado. Hoy pasa, pero ver §8.3.

---

## 8. Bugs, código muerto, hilos, fugas y deuda

### 8.1 Críticos

1. **El hilo principal no es STA.** `Program.cs:11-12` declara `[STAThread] static async Task Main()`. El compilador genera un punto de entrada sintético que no hereda el atributo. Comprobado por reflexión sobre el binario compilado: el `EntryPoint` real es `Program::<Main>` con atributos `[DebuggerStepThrough]` solamente; `STAThreadAttribute` solo está en `Main`. WinForms sobre MTA rompe OLE: portapapeles (copiar desde la salida), arrastrar y soltar, diálogos de carpeta/fichero, y es terreno inestable para MSAA/UIA. Solución: `Main` síncrono y ejecutar las migraciones con `GetAwaiter().GetResult()` o dentro del `Load` del lanzador.
2. **TLS no se aplica** (`FrmClient.cs:137`). El usuario marca "Use TLS" y conecta en claro sin aviso.
3. **Encoding no se guarda** (`SqliteMudRepository.cs:41-55`) y **el envío siempre es UTF-8** (`TelnetConnection.cs:103`).
4. **Triggers/alias/paths/sonido/Lua no hacen nada en el juego** (§2) aunque la UI permite crearlos: el usuario cree que funcionan.
5. **`FrmOptions`: OK no cierra** — `_btnOk` no tiene `DialogResult` (`FrmOptions.Designer.cs:36-43`) y `BtnOk_Click` no llama a `Close()` (`FrmOptions.cs:200-212`).
6. **El cursor de la salida salta al final con cada línea** (§3.4): bloquea la revisión con lector de pantalla.
7. **Se verbalizan los códigos ANSI** (`FrmClient.cs:295`); triggers y extractores también reciben la línea con ANSI (`:284,287`).

### 8.2 Recepción de datos

- Sin buffer de línea entre lecturas TCP; líneas vacías descartadas; *prompts* sin salto de línea tratados como líneas (`FrmClient.cs:170-180`).
- Decodificación por bloque sin `Decoder` con estado (`:170`).
- Estado telnet perdido en los límites de paquete y negociador singleton sin *reset* (§2).
- Estado ANSI reiniciado por línea; CSI no-SGR sin filtrar.

### 8.3 Sandbox Lua (`LuaScriptEngine.cs`)

- El límite se implementa **inyectando texto `__guard() ` al principio de cada línea** (`:165-189`). Consecuencias:
  - Un bucle en **una sola línea** (`while true do end`) solo ejecuta el guardián una vez: bucle infinito real. `Task.Run(..., token)` no aborta código ya en marcha, así que `ExecuteAsync` no retorna nunca y un hilo del pool queda al 100 %. Es justo el caso de prueba que pedía el plan (§5.2) y **el editor de triggers es de una línea**. Los tests solo prueban bucles multilínea.
  - Rompe scripts válidos con construcciones multilínea (tablas, llamadas o cadenas `[[ ]]` que ocupan varias líneas): inserta `__guard()` dentro de la expresión → error de sintaxis.
  - `__guard` es un global normal: el script puede hacer `__guard = function() end` y anular el límite. El comentario "scripts cannot bypass" (`:63`) es falso.
  - `trimmed.StartsWith("end")` etc. también coincide con identificadores como `endurance`.
- Sin límite de memoria. `context.Variables` es un `Dictionary` no sincronizado compartido entre scripts que corren en hilos del pool.
- `test_dbg.cs` (raíz de `v2`) es el experimento con `IDebugger` de MoonSharp para contar instrucciones de verdad: esa es la vía correcta, pero quedó como fichero suelto sin proyecto.

### 8.4 Accesibilidad / lectores

- **`JFWGetVersion` no aparece entre los símbolos de `FSAPI.dll` ni de `jfwapi.dll`** (búsqueda de cadenas en los binarios: solo `JFWRunFunction, JFWRunScript, JFWSayString, JFWSayStringEx, JFWStopSpeech`). `JawsScreenReader.IsRunning` (`JawsScreenReader.cs:14-32`) lanza por tanto `EntryPointNotFoundException` en cada sondeo y cae al plan B: `JFWSayString("", false)`. Funciona, pero cuesta una excepción cada 2 s mientras haya texto, y JAWS se sondea antes que NVDA.
- `NvdaNative.GetProcessId()` está declarado sin el parámetro de salida que exige la API real (`NvdaNative.cs:30-31`); no se usa, pero es una trampa.
- `JawsNative` declara `Cdecl` y `bool` sin `MarshalAs` (`JawsNative.cs:17-18`); en x64 da igual, en x86 conviene verificar la convención.
- Regresión de accesibilidad: 118 de las 186 cadenas de `Omnimud.UI\Resources\Strings.resx` están **sin usar**, entre ellas todos los `*Accessible`, `*Label` con mnemónico y títulos. Todo indica que la UI se construía por código con esas cadenas y al pasar a ficheros `.Designer.cs` se perdieron.

### 8.5 Hilos de UI y ciclo de vida

- `OnFormClosing` es `async void` y hace `await` antes de `base.OnFormClosing` (`FrmClient.cs:580-588`): el cierre continúa y el formulario se destruye mientras `DisconnectAsync` sigue; después se dispara `Disconnected` → `HandleDisconnect` → `BeginInvoke`/`Focus` sobre un formulario eliminado. Riesgo de `ObjectDisposedException`/`InvalidOperationException`.
- `BeginInvoke` desde el hilo de recepción sin comprobar `IsHandleCreated`/`IsDisposed` (`:237,273,303`); una excepción ahí la captura `ReceiveLoopAsync` y la convierte en "conexión perdida" (`TelnetConnection.cs:150-157`).
- `_echoOff` se escribe en el hilo de red y se lee en el de UI sin sincronizar (`:200,206,464`).
- `async void SendCommand()` (`:435`) y todos los `async void` de los diálogos sin `try/catch` global.
- F3/F4 lanzan `SendAsync` sin observar la tarea (`:568,573`).
- `TriggerEngine` y `AliasResolver` no son seguros para hilos y son singletons compartidos entre ventanas.
- No hay `Application.ThreadException` ni `AppDomain.UnhandledException`.

### 8.6 Fugas y rendimiento

- `FrmClient` y `TelnetConnection` son *transient* `IDisposable` resueltos del **contenedor raíz** (`FrmLauncher.cs:155`, `ServiceConfigurator.cs:76,80`): el contenedor los retiene hasta salir → cada sesión abierta queda en memoria. `_connection` nunca se libera explícitamente.
- `Font` sin liberar por segmento; `ContextMenuStrip` de `SetupResizeContextMenu` sin liberar; `JsonDocument` sin `using` (`FrmClient.cs:218`).
- `_rtb.Lines` repetido por línea (§3.4); `new Regex` por trigger y por línea sin caché (`TriggerMatcher.cs:98`, `SscanfMatcher.cs:36`); `_rtbMessages` sin tope.
- `SoundManager`: `_currentSound` nunca se borra al acabar el sonido (`SoundManager.cs:60-64,92`): tras un sonido de prioridad alta, los de prioridad menor quedan bloqueados para siempre. Una excepción de descarga (URL no HTTPS) se propaga sin capturar (`:70-73`).
- `SubstituteCaptures` reemplaza `%1` antes que `%10` (`TriggerActionExecutor.cs:63-71`).
- El hangdump de 162 MB dentro del árbol de fuentes; sin `.gitignore`.

### 8.7 Seguridad menor

- Contraseñas tecleadas al historial (§2); el campo de entrada no pasa a modo contraseña con `WILL ECHO`.
- `%password` viaja en claro si no hay TLS (y hoy nunca lo hay).
- La contraseña descifrada vive como `string` en `FrmClient._characterPassword` toda la sesión.
- La clave maestra queda en memoria en `AesPasswordProtector._masterKey`; DPAPI sin entropía adicional.

### 8.8 Código muerto

`Forms\FrmTEst.*`, `Forms\FrmMuds.*`, `Forms\FrmCharacters.*`, `v2\test_dbg.cs`, `ITextPipeline`/`TextPipelineResult` (sin implementación), `AnsiTerminalControl.GetAccessibleText`, `TelnetCommand.TerminalType/WindowSize/Msp` (sin uso), `CommandResult.Handled/NotConnected`, `JawsNative.RunScript/RunFunction`, `ProxyConfig`, `TlsConfig.ClientCertificatePath`, `ScriptLimits.Relaxed`, campos `_characterName` de `FrmAliases/FrmTriggers/FrmPaths`, campos `_useTls`, `_scriptEngine`, `_soundManager`, `_savedSplitterDistance` de `FrmClient`, y 118 claves de resx.

### 8.9 Deuda frente al plan

Sin ViewModels/MVVM (toda la lógica de sesión está en `FrmClient.cs`), sin `ITextPipeline` real, sin `IProtocolHandler`, sin logging, sin plugins, sin CI, sin analizadores ni *warnings as errors*. `Core\Resources\Strings.Designer.cs` está escrito a mano aunque el csproj declara `ResXFileCodeGenerator`: si Visual Studio lo regenera cambiará de forma (seguirá compilando).

---

## 9. Localización

- Mecanismo: **resx neutro en inglés + satélite `es`**, con clases `Strings` de acceso fuerte (`internal static`), en dos ensamblados:
  - `Omnimud.Core\Resources\Strings.resx` / `Strings.es.resx`: 14 claves de error (conexión, script, sonido, seguridad, triggers). Completas en ambos idiomas.
  - `Omnimud.UI\Resources\Strings.resx` / `Strings.es.resx`: **186 claves, paridad exacta en/es** y coincidencia exacta con `Strings.Designer.cs` (verificado por script).
- `NeutralLanguage=en` en Core y UI. Se genera `es\Omnimud.resources.dll`.
- **El idioma lo decide la cultura de Windows**: nadie asigna `Strings.Culture` ni `CurrentUICulture`. La opción "Language" de `FrmOptions` se guarda y no se aplica.
- **Solo 68 de 186 claves de UI se usan.** Se localizan: mensajes de estado/errores, confirmaciones, títulos "Edit…", todo `FrmQuickConnect`, las pestañas y campos de `FrmOptions`, el menú contextual de paneles y los nodos del árbol. **No se localizan** (inglés fijo en los `.Designer.cs`): menú de `FrmClient`, botón Reconnect, barra de estado inicial, todos los botones y etiquetas de `FrmLauncher`, `FrmAddEdit*`, listas y sus columnas, títulos "Add …", y los nombres de tipo de trigger. Resultado: en un Windows en español la interfaz sale **mezclada**.
- Los formularios no usan `Localizable=true`; sus `.resx` están vacíos.
- Traducciones mejorables ya presentes: `Disconnect_Timeout` = "Timeout de conexión", "Script de login". La documentación original usa "Alias/Paths/Triggers" sin traducir; el resx español usa "Recorridos" para paths.

---

## 10. Orden de trabajo sugerido (derivado de la auditoría)

1. `Program.Main` síncrono STA + manejadores globales de excepción.
2. Migrar a `net10.0(-windows)` y actualizar paquetes; decidir FluentAssertions.
3. Arreglar TLS, persistencia de `Encoding`, envío con el encoding del MUD, `CodePagesEncodingProvider`.
4. Capa de sesión fuera de `FrmClient` (servicio por sesión, con *scope* de DI): buffer de línea, `Decoder`, negociador por sesión y tolerante a cortes, texto sin ANSI para triggers/voz/mensajes.
5. Cargar alias/triggers/paths/direcciones del personaje al conectar (mapeadores Entidad→Core), ejecutar acciones de trigger, cablear MSP y un `ISoundPlayer` real.
6. Terminal: no mover el cursor del usuario, opción de posición de cursor, quitar `Lines` del camino caliente, restablecer fuente.
7. Recuperar accesibilidad y localización desde el resx existente (nombres accesibles, mnemónicos, etiquetas en `FrmClient`, atajos de menú, acceso por teclado al tamaño de paneles).
8. Sandbox Lua con contador real de instrucciones (vía `IDebugger`, como en `test_dbg.cs`) y editor multilínea.
9. Funciones del original pendientes: comandos internos, grabación de paths, diccionario y numpad con UI, opciones con herencia, logs, búsqueda, flash, Ctrl+1..0, import/export XML, proxy.
10. Limpieza: borrar `FrmTEst`, `FrmMuds`, `FrmCharacters`, `test_dbg.cs`, el hangdump; añadir `.gitignore`.

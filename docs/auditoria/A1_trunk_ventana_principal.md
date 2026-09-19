# Auditoría del cliente legacy OmniMud (trunk/cliente): ventana principal de juego y flujo de E/S

Auditoría en modo solo lectura. Todas las rutas son relativas a `C:\projects\OMnimud\trunk\cliente\` salvo que se indique. Las citas son `fichero:línea`. Los ficheros fuente están en ISO-8859-1 (los acentos se ven como `?` en algunas herramientas) y contienen el byte ESC (0x1B) literal dentro de cadenas (verificado con `cat -A`).

---

## 0. Qué se compila de verdad

| Hecho | Evidencia |
|---|---|
| La solución `trunk\omnimud.sln` solo referencia `cliente\omnimud.csproj` (más AutoUpdater, CloseOmnimuds, OmnimudCommonRules, ProcessRules, SignCode y dos proyectos de `..\v2`). `tiflomud.csproj` NO está en la solución. | `omnimud.sln:6-20` |
| `omnimud.csproj`: WinExe, .NET Framework **v2.0**, Debug = **x86**, namespace/assembly `omnimud`. Referencias: `GWSPEAKLib` (Window-Eyes), `irrKlang.NET2.0`, `System.Data.SQLite`, proyecto `OmnimudCommonRules`. | `omnimud.csproj:8-11,27,52,63-76,444-447` |
| `tiflomud.csproj` es un proyecto antiguo/abandonado (nombre anterior del programa). Referencia `CSonidos.cs`, que **ya no existe** en disco, y le faltan decenas de ficheros actuales (CHistory, CSounds, CMovements, FrmFindTextBox...). No compilaría. | `tiflomud.csproj:75`, `ls` |
| **`FrmCliente` es LA ventana de juego**. Se instancia desde `FrmPrincipal.cs:109`, `FrmMuds.cs:274` y `FrmCharacters.cs:275` (siempre `new FrmCliente(...)`, `Owner = this`, `Show()`, `Connect()`, `Hide()` del padre). | ficheros citados |
| **`UcClient` es código muerto**: `UcClient.cs` y `UcClient.Designer.cs` están incluidos en el csproj (`omnimud.csproj:368-373`) pero TODO el cuerpo de ambas clases está dentro de un comentario `/* ... */` (`UcClient.cs:22` a `UcClient.cs:2127`; `UcClient.Designer.cs:5` a `:259`). Compila como un `UserControl` vacío. Nadie lo instancia. | grep de `/*` y `*/` |
| `TextBoxBraille.cs` (namespace `tiflomud`) y `Form1.cs` **no están en ningún csproj**: no se compilan. | `omnimud.csproj`, `tiflomud.csproj` |
| `CNegotiation.cs.txt` es una copia byte a byte de `CNegotiation.cs` (`cmp` = idénticos). | — |

### Diferencias UcClient (muerto, snapshot antiguo) vs FrmCliente (vivo)
UcClient era un intento de convertir la ventana en UserControl (¿pestañas/multi-sesión?) abandonado. Respecto a FrmCliente:
- Ctrl+número solo cubría **Ctrl+1..7** sin concatenación de dígitos ni teclado numérico (`UcClient.cs:1091-1105`).
- Troceaba el texto a hablar en bloques de **500 caracteres** "para que no pete la api de JAWS" (`UcClient.cs:1050-1060`); FrmCliente ya no lo hace.
- Reglas de mensajes cableadas a `"simauria"`/`"callandor"` (`UcClient.cs:1021-1031`) en vez de plugins `IRule`.
- Sin modo offline, sin `-triggers/+triggers/-trigger/+trigger`, sin triggers `@comando`, sin `recolecta`/`pruebaom`, sin `OmGet`, sin variables de sesión, sin apertura de URL con Enter.
- Historial como `string[] ultimos` (`UcClient.cs:56,1205-1212`) en vez de `CHistory`.
- `TxtEnviar.WordWrap = false` (`UcClient.Designer.cs:100`); en FrmCliente WordWrap queda en su valor por defecto (true).
- Los `Close()` están comentados (`UcClient.cs:651,1304,1307`).

En adelante todo se refiere a **FrmCliente**.

---

## 1. Controles de la ventana principal

Formulario `FrmCliente` (`FrmCliente.Designer.cs:218-245`): `Text = "OmniMud"`, `ClientSize 607x644`, `StartPosition = CenterScreen`, **`KeyPreview = true`**, imagen de fondo e icono desde resx, `AutoScaleMode.Font`. Eventos: `Load`→`Form1_Load`, `FormClosed`→`Form1_FormClosed`, `FormClosing`→`FrmCliente_FormClosing`, `KeyDown`→`FrmCliente_KeyDown`, `KeyUp`→`FrmCliente_KeyUp`. `Activated/Deactivate` se enganchan en Load (`FrmCliente.cs:461-462`). No define AcceptButton ni CancelButton.

| Control | Tipo | Texto / Label | TabIndex | Accesibilidad | Otras propiedades | Eventos |
|---|---|---|---|---|---|---|
| `BtnAccion` | Button | `&Cancelar` inicialmente (Alt+C). Cambia a `&Desconectar` (Alt+D) al conectar o `&Cerrar` en modo offline (`FrmCliente.cs:293-301`) | **4** | `AccessibleDescription="Cancelar"` (se actualiza junto con `Tag` y `Text`). Sin AccessibleName | AutoSize, anclado abajo-izda, (13,591) | `Click`→`BtnAccion_Click` |
| `LblEnviar` | Label | `&Texto a enviar:` (Alt+T) | 6 | Name y Description = `"Texto a enviar:"` | `Enabled=false` hasta conectar; negrita 9pt, blanco, fondo transparente. Se pone en **cursiva** cuando se activa el modo movimiento desde el menú (`FrmCliente.cs:807,812`) | — |
| `TxtEnviar` | **TextBox** | (label anterior) | **7** | Name `"Texto a enviar:"`, Description `"Texto a enviar"` | **`Multiline=true`**, `ScrollBars=Horizontal`, 412x80, `Enabled=false` hasta conectar, WordWrap por defecto (true). Fuente = `_options.TextFont` (`FrmCliente.cs:466`). Con NO_ECHO pasa a `Multiline=false` + `PasswordChar='*'` | `KeyDown`, `KeyUp`. **`TxtEnviar_TextChanged` existe (`FrmCliente.cs:1380`) pero NO está enganchado en el Designer** (solo KeyDown/KeyUp en `Designer.cs:99-100`) → es código muerto |
| `LblRecibidos` | Label | `&Recibido:` (Alt+R) | 8 | Name/Description `"Recibidos:"` (nota: plural, distinto del texto) | `Enabled=false` hasta conectar | — |
| `TxtRecibido` | **RichTextBox** | (label anterior) | **9** | Name y Description = `"Recibido:"` | **`ReadOnly=true`**, `Cursor=Arrow`, 412x342 en (147,190), anclado a los 4 lados, `Enabled=false` hasta conectar. `BackColor=White` forzado en Load (`FrmCliente.cs:463`). `DetectUrls` por defecto (true). Sin MaxLength explícito | `KeyDown`→`TxtReadOnly_KeyDown`, `KeyPress`→`TxtReadOnly_KeyPress`, `LinkClicked`→`TxtReadOnly_LinkClicked`; `ContextMenu` compartido |
| `LblMensajes` | Label | `&Mensajes:` (Alt+M) | 10 | `AccessibleDescription="Mensaes:"` (**errata**), sin AccessibleName | `Enabled=false` hasta conectar | — |
| `TxtMensajes` | **RichTextBox** | (label anterior) | **11** | `AccessibleDescription="Mensajes"`, **sin AccessibleName** | `ReadOnly=true`, `Cursor=Arrow`, 412x172 en (147,12) | `KeyDown`→`TxtReadOnly_KeyDown`, `KeyPress`→`TxtReadOnly_KeyPress`, `SelectionChanged`→`TxtMensajes_SelectionChanged`. **No tiene `LinkClicked`** (a diferencia de TxtRecibido), pero Enter sobre URL sí funciona vía KeyDown |
| `StatusBar` | StatusStrip | `Text="statusStrip1"` | 12 | — | `SizingGrip=false`, 3 paneles `StlInfo` (Spring), `StlSecs` (Spring), `StlMsg`. Los tres se ocultan en Load (`FrmCliente.cs:477`) | — |
| `TmrSecs` | Timer 1000 ms | — | — | — | Arranca al conectar (`FrmCliente.cs:327`) | `TmrSecs_Tick` |
| `TmrMsgStatus` | Timer 2000 ms | — | — | — | — | `TmrMsgStatus_Tick` |
| `timer2`, `TmrPackets` | Timer | — | — | — | `timer2` se instancia sin uso; `TmrPackets` se declara y nunca se instancia (`Designer.cs:41,259-260`). Restos | — |

Orden de tabulación resultante: BtnAccion(4) → TxtEnviar(7) → TxtRecibido(9) → TxtMensajes(11). Orden visual de arriba abajo: Mensajes, Recibido, Texto a enviar, botón.

**Ventana de mensajes opcional**: si el MUD no tiene regla de procesamiento (`ProcessRule` vacío, `"ninguna"` o no encontrada) se llama a `OcultaVentanaMensajes()` (`FrmCliente.cs:333-345,1993-1999`): oculta `LblMensajes`, `TxtMensajes` y `StlMsg`, y `TxtRecibido` ocupa su sitio. Si más tarde un trigger llama a `AddMessage`, `MuestraVentanaMensajes()` la vuelve a mostrar (`FrmCliente.cs:2001-2013,2054`), pero fija la altura de TxtRecibido a 190 px de forma fija.

Campos declarados y nunca usados: `time_receive`, `packets_addeds`, `tmp_path` (solo se pone a null), `MnuEdicionRehacer_Click` (sin ítem de menú), `ChangeTextFont` (nunca se llama: **cambiar la fuente en Opciones no se aplica a la ventana abierta**; `FrmOptions.cs:286` solo reasigna `Options`).

---

## 2. Menús

No hay MenuStrip en el Designer: se usa **`MainMenu`/`MenuItem` clásico construido por código** en `Form1_Load` (`FrmCliente.cs:354-458`). Todos los menús de primer nivel se ponen `Visible=false` mientras se conecta y `true` al conectar (`ChangeMenusVisibility`, `FrmCliente.cs:555-562`, llamadas en `:303` y `:536`).

### Menú principal

| Menú | Ítem (texto exacto) | Atajo | Handler / efecto |
|---|---|---|---|
| **&Acciones** | `&Salvar partida` | **F3** | `SendCommand(mud.SaveCommand)`; no hace nada si está vacío (`:905-912`). Deshabilitado en Popup si SaveCommand vacío (`:865-872`) |
| | `&Abandonar la partida` | **F4** | `SendCommand(mud.QuitCommand)` (`:896-903`). Deshabilitado si QuitCommand vacío |
| **&Edición** | `&Copiar` | Ctrl+C | `Copy()` del TextBox/RichTextBox activo (`:707-712`) |
| | `C&ortar` | Ctrl+X | `Cut()` (`:714-720`) |
| | `&Pegar` | Ctrl+V | `Paste()` (`:694-700`) |
| | `Seleccionar todo` (sin mnemónico) | **Ctrl+E** | `SelectAll()` (`:571-577`) |
| | `&Deshacer` | Ctrl+Z | `Undo()` (`:682-687`) |
| | `&Buscar` | **Ctrl+B** | Abre `FrmFindTextBox` solo si el foco está en Recibido o Mensajes (`:723-770`) |
| | `Buscar &siguiente` | **Ctrl+S** | Repite la última búsqueda del cuadro enfocado (`:623-663`) |
| **&Herramientas** | `Modo Mo&vimiento con teclado numérico` (checkable) | **F2** | Alterna el check, pone/quita cursiva en `LblEnviar`, persiste en `CRegistro.SetMovementsEnabled` a nivel mud o personaje (`:802-822`). Estado inicial leído del registro en Load (`:411-418`) **pero la cursiva de la etiqueta NO se aplica al cargar** |
| | `&Alias` | **F5** | Abre `FrmAliases` (requiere personaje), oculta la ventana de juego (`:884-893`) |
| | `&Triggers` | **F6** | Abre `FrmTriggers` (requiere personaje) (`:874-882`) |
| | `&Paths` | **F7** | Abre `FrmPaths` (requiere personaje) (`:849-857`) |
| | `Modo silencioso` (checkable, sin mnemónico) | **F8** | Alterna check y ejecuta el comando interno `callate` / `hablar` (`:825-837`) |
| | `&Opciones` | **F9** | Abre `FrmOptions` (de personaje o de mud), oculta la ventana (`:839-847`) |
| | `Configurar &teclas de movimiento` | — | Abre `FrmMovements` (`:780-799`) |
| | `&Direcciones para los paths` | — | Abre `FrmPathsDictionary` (`:772-778`) |
| **Ay&uda** (de `CHelpManager.CreateHelpMenu`, `CHelpManager.cs:21-49`) | `&Acerca de...` | — | MessageBox con versión, autor, email y web (`:125-128`) |
| | `&Manual de usuario` | **F1** | Abre `omnimud.chm` del directorio de la app; error si no existe (`:110-118`) |
| | `Omnimud en la web` ▸ `Página principal de OMnimud` / `Lista de distribución` / `Enviar e-mail al autor` | — | `Process.Start` de `http://www.omnimud.org`, `http://groups.google.com/group/omnimud`, `mailto:info@omnimud.org` |
| | `&Enviar errores o sugerencias` ▸ `Enviar &sugerencia` / `Enviar &error` | — | Abre `FrmReports` |

Notas:
- `MnuHerramientasMovimiento` se crea dos veces (`:375` y `:379`); gana el segundo texto.
- `MnuHerramientasOpciones` se declara como variable **local** en `:360`, ocultando el campo de clase (que queda null). Inofensivo.
- En Popup de Herramientas, Alias/Triggers/Paths se deshabilitan si no hay personaje (`:859-863`).
- En Popup de Edición (`:584-615`): sin cuadro de texto activo, todo deshabilitado. En RichTextBox: Buscar habilitado, Deshacer/Cortar/Pegar deshabilitados, Buscar siguiente solo si ya hay texto de búsqueda para ese cuadro. En TxtEnviar: Buscar/Buscar siguiente deshabilitados, Cortar según selección, Pegar según portapapeles, Deshacer según `CanUndo`.
- Tras abrir Alias/Triggers/Paths/Opciones/Movimientos/Direcciones la ventana de juego se **oculta** (`Hide()`) — la conexión sigue viva y recibiendo, pero `activo=false`, así que no se habla nada y parpadea la ventana.

### Menú contextual (compartido por TxtRecibido y TxtMensajes) (`:452-458`)
`&Copiar`, `&Seleccionar todo`, `&Buscar`, `Buscar &siguiente` (mismos handlers; mismo Popup). TxtEnviar usa el menú contextual estándar de Windows.

### Diálogo Buscar (`FrmFindTextBox`)
Título y AccessibleDescription = `"Buscar en " + AccessibleDescription del cuadro` → "Buscar en Recibido:" / "Buscar en Mensajes" (`FrmCliente.cs:746`).
Controles (`FrmFindTextBox.Designer.cs`): `LblBuscar` `&Buscar:` (TabIndex 0) / `TxtBuscar` (1, MaxLength 255) / `ChkCaseSensitive` `&Distinguir mayúsculas de minúsculas` (2) / GroupBox `Dirección:` (3) con `RbAbajo` `A&bajo` (marcado por defecto) y `RbArriba` `A&rriba` / `BtnBuscar` `&Buscar` (4, AcceptButton) / `BtnCancelar` `&Cancelar` (5, CancelButton). Hay tres mnemónicos **&B** en conflicto (label, RbAbajo, botón).
Comportamiento (`FrmFindTextBox.cs:29-53`): busca desde `SelectionStart` hasta el final (o de 0 a `SelectionStart` si Arriba) con `NoHighlight`; si encuentra, selecciona el texto y cierra con OK (entonces FrmCliente memoriza texto y opciones **por cuadro**); si no, MessageBox `"No se encontró el texto buscado"` y cierra con Cancel (no memoriza). Reabrir precarga el último texto seleccionado.
**Bug**: con texto vacío hace beep y pone `CancelClose = true`, que **nunca se vuelve a poner a false** → a partir de ahí `FormClosing` cancela siempre y el diálogo no se puede cerrar (`FrmFindTextBox.cs:34-38,55-59`).
Buscar siguiente (`FrmCliente.cs:623-663`): hacia abajo busca desde `SelectionStart+1`; hacia arriba de 0 a `SelectionStart-1`; si no hay resultado, `SystemSounds.Exclamation`.

---

## 3. Atajos de teclado (inventario completo)

No hay `ProcessCmdKey` ni `ProcessDialogKey` sobrescritos en FrmCliente. Fuentes de atajos: `Shortcut` de MenuItem, `FrmCliente_KeyDown/KeyUp` (KeyPreview), `TxtEnviar_KeyDown/KeyUp`, `TxtReadOnly_KeyDown/KeyPress`, mnemónicos Alt+letra.

### 3.1 A nivel de formulario (cualquier control con foco)

| Tecla | Condición | Comportamiento exacto | Cita |
|---|---|---|---|
| **F1** | — | Manual CHM | `CHelpManager.cs:40` |
| **F2** | — | Alterna modo movimiento con teclado numérico | `FrmCliente.cs:382` |
| **F3** / **F4** | — | Enviar SaveCommand / QuitCommand del MUD | `:383-384` |
| **F5 / F6 / F7** | — | Alias / Triggers / Paths | `:392-394` |
| **F8** | — | Modo silencioso (ejecuta `callate`/`hablar`) | `:396` |
| **F9** | — | Opciones | `:395` |
| **Ctrl+C/X/V/Z** | cuadro de texto activo | Copiar/Cortar/Pegar/Deshacer vía menú | `:387-390` |
| **Ctrl+E** | cuadro de texto activo | Seleccionar todo | `:391` |
| **Ctrl+B** | foco en Recibido o Mensajes | Buscar | `:385` |
| **Ctrl+S** | foco en Recibido o Mensajes | Buscar siguiente | `:386` |
| **Ctrl+1 … Ctrl+9, Ctrl+0** (fila superior **y también Ctrl+NumPad0-9**) | `e.Control && !e.Alt && !KeyMenu` (evita AltGr) | Lee el mensaje N-ésimo más reciente: dice **`"N: <mensaje>"`** con `interrupt=true`. `0` = 10. **Concatenación**: si entre pulsaciones pasan < **400 ms**, los dígitos se concatenan (Ctrl+1,Ctrl+5 → mensaje 15); máximo 3 dígitos (al cuarto: `SystemSounds.Beep` y se descarta). Sin mensajes: `"No hay mensajes."`; si N > total: `"Solo hay X mensajes."`. **Pulsar dos veces NO hace nada especial** (ni copia ni deletrea): o repite, o concatena si es < 400 ms (Ctrl+1,Ctrl+1 rápido = mensaje 11) | `:990-1029` |
| **Ctrl+Oem5** (tecla `º`/`\` a la izquierda del 1 en teclado español; en el enunciado "Ctrl+\\") | igual que arriba | Lectura **secuencial hacia atrás**: primera pulsación lee el 1 (último); cada pulsación posterior con < **800 ms** de separación avanza al 2, 3…; si pasa más tiempo, vuelve al 1. Dice `"N: <mensaje>"`. Sin mensajes `"No hay mensajes."`; si se pasa: `"Sólo ay X mensajes."` (**errata "ay"** en el literal) | `:1030-1050` |
| **Escape** (sin modificadores) | — | Pone `NumMensajeTNumerico = 0` (cancela la concatenación de dígitos). No hace nada más (no cierra, no limpia) | `:986-989` |
| **Alt** (Keys.Menu) | — | Activa flag `KeyMenu` (se limpia en KeyUp de Alt o cuando `!e.Alt`); sirve para que AltGr+número no dispare la lectura | `:980-984,1127-1131` |
| **Alt+T / Alt+R / Alt+M** | — | Mnemónicos de las etiquetas → foco a TxtEnviar / TxtRecibido / TxtMensajes (mecanismo estándar Label→siguiente control por TabIndex) | Designer |
| **Alt+C / Alt+D** | según estado | Botón Cancelar / Desconectar (`&Cerrar` en offline también es Alt+C) | Designer, `:293-301` |
| **Alt+A / E / H / U** | — | Menús Acciones / Edición / Herramientas / Ayuda | `:355-357`, `CHelpManager.cs:23` |
| Tab / Shift+Tab | — | Navegación estándar (RichTextBox sin AcceptsTab; TxtEnviar multilinea sin AcceptsTab) | — |

**No existen** atajos tipo Alt+número, ni teclas dedicadas para saltar entre cuadros aparte de los mnemónicos y Tab, ni atajo para copiar un mensaje.

### 3.2 Con foco en `TxtEnviar` (`TxtEnviar_KeyDown`, `:1190-1304`)

| Tecla | Comportamiento exacto |
|---|---|
| **Shift+Enter** o **Ctrl+Enter** | Si el cuadro está vacío → envía una línea en blanco (`SendCommand("\n")`). Si tiene texto → inserta salto de línea (`AppendText("\r\n")`, siempre **al final**, no en el cursor) |
| **Enter** con cuadro **vacío** | **Repite el último comando** (`ultimo`) a través de `ProcessInputCommand`; solo si no hay `ultimo` envía `"\n"` (`:1208-1212`) |
| **Enter** con texto | (a) si el texto coincide con la entrada de historial seleccionada con flechas → se reenvía, `ultimo` = esa entrada, **no se vuelve a añadir** al historial; (b) si `PasswordChar=='*'` → se envía y **no se guarda** ni en historial ni en `ultimo`; (c) caso normal → se añade al historial, `ultimo = texto`, `ProcessInputCommand`, `Clear()`, `posCursorUltimos=-1` |
| **NumPad0-9** | Solo si F2 (modo movimiento) está marcado y hay `CMovement` para esa tecla: `ProcessInputCommand(m.Direction)` y se suprime la tecla (`:1242-1252`; KeyUp también marca Handled `:1389-1392`). **No comprueba modificadores** → con modo movimiento activo, Ctrl+NumPadN lee el mensaje N **y además** envía el movimiento |
| **Flecha arriba** (sin Shift) | Si el cuadro tiene "más de una línea" y el cursor no está en la primera: se **verbaliza la línea anterior** (`GetLineFromEnviar(-1)`) y se deja el movimiento normal del cursor. Si no: historial hacia atrás (desde el más reciente; se queda en el más antiguo), pone el texto, `SelectAll()`, y suena `sounds\click.wav` si `EnableSounds` |
| **Flecha abajo** (sin Shift) | Análogo: verbaliza línea siguiente si la hay; si no, historial hacia delante; al pasar del último, limpia el cuadro y `posCursorUltimos=-1` |

Casos límite del historial multilinea: la condición es `GetLineFromCharIndex(TextLength-1) > 1` (`:1255,1280`), es decir, **solo se considera multilinea con 3 o más líneas visuales** (con 2 líneas la flecha navega historial). Como WordWrap está activo, cuentan las líneas envueltas. Ctrl+flechas o Alt+flechas también entran (solo se excluye Shift).

### 3.3 Con foco en `TxtRecibido` o `TxtMensajes`

| Tecla | Comportamiento | Cita |
|---|---|---|
| **Enter** sobre una URL/e-mail | `GetUrl` extrae la "palabra" bajo el cursor (delimitada por espacio, `'`, CR, LF); si contiene `http`, `ftp` o `www.` y casa con `CConsts.RegUrl`, o contiene `@` y casa con `RegMail` → se abre con `Process.Start` (anteponiendo `mailto:` si es correo); suena `url.mp3` si existe; si falla, `SystemSounds.Exclamation` | `:2814-2828,1875-1917,1349-1364` |
| Clic en enlace (solo TxtRecibido) | Igual que arriba vía `LinkClicked` | Designer `:136` |
| **Ctrl+F** (KeyChar 6) | Verbaliza la **hora del mensaje bajo el cursor de TxtMensajes**: `"HH:mm:ss, el dd/MM/yyyy"` o `"Hora no encontrada."`. Ojo: usa siempre `TxtMensajes.SelectionStart` aunque el foco esté en TxtRecibido | `:1318-1328` |
| **Cualquier carácter imprimible** (incluye Backspace y Enter sin URL) | "Type-through": `SystemSounds.Beep`, foco a `TxtEnviar`, espera activa `while(!TxtEnviar.ContainsFocus);`, `Application.DoEvents()` y `SendKeys.Send(tecla)` (escapando `{}[]+%()^~` con llaves). Se ignora si Ctrl está pulsado o es Escape | `:1311-1342` |
| Flechas, Inicio/Fin, Ctrl+Inicio/Fin, selección con Shift | Navegación estándar del RichTextBox de solo lectura | — |
| Cambio de selección en TxtMensajes | Actualiza `StlMsg` a `"Mensaje seleccionado: HH:mm:ss, el dd/MM/yyyy"` (o `"...Hora desconocida"`) cuando cambia de línea; a los 2 s `TmrMsgStatus` lo devuelve a `"Último mensaje recibido a las HH:mm:ss, del dd/MM/yyyy"` / `"Sin mensajes"` | `:1366-1378,1409-1422` |

Riesgos: la espera activa puede colgar la UI si el foco no llega; Enter sin URL en un cuadro de solo lectura acaba enviando un Enter a TxtEnviar, que si está vacío **repite el último comando**.

---

## 4. Comandos internos del cuadro de entrada (`ProcessInputCommand`, `FrmCliente.cs:2148-2552`)

Orden exacto de evaluación:

1. **`_ruta`** (primer carácter `_`) — ejecutar path.
2. **Sustitución de alias** sobre la primera palabra.
3. **Triggers de comando** (`Happen` = `@palabra`).
4. `-triggers` / `+triggers`.
5. `-trigger X` / `+trigger X`.
6. `uncalias X`.
7. `calias X [acción]`.
8. Captura de dirección si se está grabando un path.
9. `switch` por **coincidencia exacta de la cadena completa** (sensible a mayúsculas, sin trim): `cls`, `triggers`, `recolecta`, `pruebaom`, `calias`, `paths`, `paths iniciar`, `paths ultima`, `paths ultima borrar`, `paths cancelar`, `paths grabado`, `paths detener`, `callate`, `hablar`; **default → `SendCommand`**.

Las respuestas se inyectan con `cliente_dataReceived(this, texto, len)`, es decir, pasan por TODO el pipeline de recepción (se muestran en Recibido, van al log, se hablan, **pueden disparar triggers**). Consecuencia: **en modo offline no se ve ninguna respuesta**, porque `cliente_dataReceived` retorna si `!cliente.connected` (`:1103`).

Al ir después de la sustitución de alias, un alias puede mapear a un comando interno. `ProcessInputCommand("")` lanzaría excepción (`comando[0]`), pero la UI nunca lo llama con cadena vacía.

| Comando | Sintaxis | Comportamiento y mensajes literales |
|---|---|---|
| **Path** | `_nombre` o `_nombre -r` (reverso) | Sin diccionario de direcciones: `"No hay paths añadidos actualmente."` · Solo `_`: `"Uso: _nombre_del_path. Ejemplo: _alandel-turman."` · No existe: `"No hay ningún path con ese name."` · Reverso imposible: mensaje largo "No es posible dar la vuelta a este path…" **y no hace return** → continúa con `direcciones=null` → `SendCommand(null)` → NullReferenceException (bug, `:2195-2222`). Normal: expande (`CPath.SplitPath(PathExpanded)`), normaliza cada dirección con el diccionario, une con `\n` y llama a `SendCommand` **una vez** con todo el bloque. Errores: `"Error: <mensaje>"` |
| **Alias (uso)** | primera palabra = alias | Búsqueda exacta y sensible a mayúsculas en `CAliasCollection` (SortedList). Solo sustituye la primera palabra, conserva el resto; **sin parámetros `%1`**, sin recursión, y se aplica **antes** de partir por el carácter de concatenación (en `a;b` solo `a` puede ser alias) (`:2228-2236`) |
| **Trigger de comando** | primera palabra coincide con un trigger cuyo `Happen` es `@palabra` | Ejecuta el trigger con `incognitas` = resto de palabras y `FullCommand` = línea completa; **el comando no se envía al MUD** (`:2239-2253`). Respeta `CaseSensitive`. Se salta si `_triggers_disabled` |
| **`-triggers` / `+triggers`** | sin argumentos | Deshabilita/habilita **todos** los triggers solo para la sesión (flag en memoria). Mensajes: `"Sistema de triggers deshabilitado. Utiliza el comando '+triggers' para volver a habilitarlo."` / `"Sistema de triggers habilitado. Utiliza el comando '-triggers' para deshabilitarlo."` / `"El sistema de triggers ya estaba [des]habilitado."` (sin `\n` final) |
| **`-trigger N` / `+trigger N`** | nombre sin espacios (se usa `palabras[1]`) | Sin nombre: `"Activar qué trigger?"` / `"Desactivar qué trigger?"`. Requiere personaje (MessageBox si no). `"No se encontró el trigger N."`, `"El trigger ya está activado."`, `"El trigger ya está desactivado."`, `"Trigger activado."`, `"Trigger desactivado."`, `"Error al activar/desactivar el trigger."`. **Persiste** con `CRegistro.ModifyTrigger` |
| **`uncalias N`** | un solo nombre | Requiere personaje. `"Debes especificar el name del alias a eliminar."`, `"El name del alias no puede contener espacios."`, `"No existe ningún alias con ese name."`, `"Alias N eliminado correctamente."`; fallo de persistencia → MessageBox |
| **`calias`** | solo | Abre el formulario de Alias (igual que F5) |
| **`calias N`** | consulta | `"El alias N está asignado a la acción A."` / `"El alias N no está almacenado."`; `calias ` (con espacio final) → `"Debes especificar el name del alias a consultar."` |
| **`calias N acción…`** | alta (`AddAlias`, `:2758-2809`) | Validaciones: `"El comando del alias no puede estar vacío."`, `"La acción del alias no puede estar vacía."`, no puede contener el carácter **æ** (0xE6, separador interno de almacenamiento), `"Ya existe un alias para el comando especificado. Borra ese alias con el comando cunalias, …"` (**errata: el comando real es `uncalias`**). Si ya hay otro alias con la misma acción → MessageBox Sí/No (`"Alias cancelado."`). Éxito: `"Alias N insertado correctamente."`; fallo: `"¡Error al insertar el alias!"` |
| **`cls`** | exacto | `TxtRecibido.Clear()` y dice `"OK"`. No toca Mensajes ni `MessageCol` |
| **`triggers`** | exacto | Abre el formulario de Triggers |
| **`paths`** | exacto | Abre el formulario de Paths (requiere personaje) |
| **`paths iniciar`** | | Requiere personaje y diccionario (MessageBox si no). Si ya graba: `"Ya hay iniciada la grabación de un path…"`. Si no: mensaje largo de ayuda y `making_path=true`. Mientras se graba, **todo comando que el diccionario reconozca como dirección** (`_ptd_col.IsKnownDirection(comando)`) suena `sounds\pop.wav`, se guarda abreviado y se envía igualmente al MUD (`:2376-2389`) |
| **`paths ultima`** | | `"La última dirección introducida ha sido: X.\nPara borrarla, escribe paths ultima borrar."` / `"No estás grabando un path en este momento…"` / `"Aún no hay direcciones grabadas en este path."` |
| **`paths ultima borrar`** | | Quita la última: `"La última dirección almacenada (X), ha sido eliminada del path."` |
| **`paths grabado`** | | `"El path que llevas grabado actualmente es:\n<abreviaturas concatenadas>"` |
| **`paths cancelar`** | | MessageBox de confirmación; `"Grabación de path cancelada."`. **Bug**: pone `tmp_path=null` pero no limpia `tmp_path_str` → la siguiente grabación hereda las direcciones |
| **`paths detener`** | | Abre `FrmAddEditPath` con `TxtCamino = CPath.CollapsePath(join)`. **Bug**: no comprueba `making_path` ni `tmp_path_str==null` → `string.Join("", null)` lanza excepción si no se ha grabado nada |
| **`callate`** | | `"Activando modo silencioso."` (se habla porque `silent` se activa después) / si ya estaba: `"all_speak:El modo silencioso ya estaba activado."` |
| **`hablar`** | | `"Desactivando modo silencioso."` / `"El modo silencioso no estaba activado."`. **El check del menú F8 no se sincroniza** si se teclean los comandos a mano |
| **`recolecta`** | (oculto/depuración) | `GC.Collect()` y dice `"¡OK!"` **solo por JAWS** (llama a `JFWSayString` directamente; sin `jfwapi.dll` → DllNotFoundException) |
| **`pruebaom`** | (oculto/depuración) | `OmGet("mirar")` y dice por JAWS `"Texto recuperado: …"` |

No existe ningún otro prefijo tipo `@…` tecleable salvo los triggers de comando descritos; `+triggers`/`-triggers` son ambos válidos (el enunciado mencionaba `+triggers` y `-trigger`).

Curiosidad: en muchos literales aparece **"name"** donde debería decir "nombre" (`"ningún path con ese name"`, `"El name del alias…"`): parece un buscar-y-reemplazar global accidental.

---

## 5. Pipeline de recepción (orden exacto)

### 5.1 Capa de red (`cClient.cs`)
- `TcpClient` directo (**sin proxy, sin SSL, sin compresión**), `LingerOption(false,1)` (`:142-149`). Conexión asíncrona `BeginConnect/EndConnect`.
- Hilo receptor MTA `"Client's Receive Thread"` (`:151-157`): buffer de **4096 bytes**; lee en bucle mientras `stream.DataAvailable`, acumulando en un StringBuilder; **decodifica cada lectura con `Encoding.Default`** (codepage ANSI del sistema, p.ej. Windows-1252) y dispara `dataReceived(texto, bytes)` **una vez por ráfaga** (`:186-223`). `readBytes==0` → cierra y dispara `ClientDisconnect(Server)`. Excepción de lectura → el hilo termina en silencio.
- **No hay troceo en líneas ni buffer de línea parcial**: la unidad de proceso es "lo que llegó en la ráfaga". Un prompt sin salto de línea se trata como cualquier otro texto: se añade, se registra y se habla inmediatamente. Una línea partida entre dos ráfagas se procesa (triggers, reglas, voz) en dos trozos.
- Envío: `Encoding.Default.GetBytes` + `stream.Write` síncrono (`:231-249`).

### 5.2 `cliente_dataReceived` (`FrmCliente.cs:1101-1125`)
Marshalling al hilo de UI con `Invoke` (síncrono); descarta si el formulario se está destruyendo o `!cliente.connected`; llama a `ProcessResponse`.

### 5.3 `ProcessResponse` (`:1610-1831`) paso a paso
1. **`RemoveUnusedChars`** (`:1433-1464`): por cada BS (0x08) elimina el BS y el carácter anterior (si es el primero, solo el BS). Después **elimina todos los `\r`**.
2. **`ProcessSounds` (MSP)** (`:1514-1601`): parte por `\n`; una línea es comando si empieza por `!!SOUND(` o `!!MUSIC(` y mide > 8. Se **elimina del texto** siempre. Si es el **último segmento** de la ráfaga (sin `\n` detrás) se guarda en `last_part_sound` y se antepone a la siguiente ráfaga. Se reproduce solo si: `EnableSounds`/`EnableMusic`, ventana activa o `PlaySoundsOutOfWindow`/`PlayMusicOutOfWindow`, y `mud.SoundDirectory` existe. Parámetros separados por espacio: nombre (`/`→`\`), `off` (para todos los de ese tipo), `V=` (100 si no parsea; **0 si se omite**), `L=` (1/0), `P=` (50/0), `C=` (bool), `T=`, `U=`. Llama a `CSounds.PlaySound(tipo, dir, nombre, V, L, P, C, T, U, opciones)`. Asume que la línea acaba exactamente en `)`.
3. **`ProcessNegotiation` (telnet)** — ver §10. Nota: MSP va **antes** que telnet.
4. **Captura `OmGet`** (`:1624-1636`): si un trigger de código está esperando respuesta (`ToTriggerOmGet`) y el texto casa con la regex esperada (o no hay), la ráfaga se acumula en `TriggerResponse` y **no se muestra, ni se registra, ni se habla**. Termina cuando el texto sin colores acaba en `\n` o la ráfaga acaba en espacio; timeout de 3 s en `OmGet` (`:1937-1953`, bucle con `DoEvents` + `Sleep(100)`).
5. **ANSI → RichTextBox** — ver §11. Se guarda posición del cursor y selección, se va al final, y se hace `AppendText` por cada tramo, quitando el marcador `all_speak:`.
6. **Posición del cursor** (`:1785-1795`): opción `CursorPositionOnReceived`: `GoEnd` → siempre al final + `ScrollToCaret`; `DependingOnCurrentPosition` (por defecto) → al final solo si el cursor ya estaba al final; `Keep` → restaura posición y selección previas.
7. `textoPlano = string.Join("", colores)` (texto sin códigos SGR, con `all_speak:` aún dentro).
8. **Triggers**: `ProcessTriggers(textoPlano)` si `!_triggers_disabled` (`:1799`, `:2595-2659`). Se evalúan sobre la ráfaga entera. Criterio (OR): regex si `UseRegExp` (Singleline, IgnoreCase si no es case-sensitive) · `text.Contains(happen)` · contains en minúsculas si no es case-sensitive · `CParseStrings.sscanf` (patrones `%s`, `%d`, `%w`, `%Ns`, `%Nw`, `%%`; **sustituye `\n` por espacio** antes de casar). `^`/`$` iniciales/finales se tratan a mano (contra el texto completo y luego línea a línea). Bugs: la lista `incognitas` se comparte y **no se limpia entre triggers**; `text` se muta con `TrimBlankLines` para los siguientes; en `ExecuteTrigger` las opciones de regex están **invertidas** (CaseSensitive → IgnoreCase, `:2673-2674`) y `Regex.Replace(text, Happen, Action)` devuelve **todo el texto** con la sustitución, que se envía entero como comando. Acciones: `SendCommand`, `PlaySound`, `Boot` (= ambas), `Sharp`/`VBNet` (código compilado a DLL por trigger y ejecutado en el ThreadPool, con API `CTriggerFunctions`: `OmSend`, `OmGet`, `OmDisplay`, `SayText`, `PlaySound/StopSound`, `AddMessage`, `OmSetVar/OmQueryVar/OmRemoveVar/OmVarIsSet`, `StartCount`, `GetLastActivity`). Sonido de trigger solo si ventana activa o `PlaySoundsOutOfWindow`.
9. **Regla `IRule`** (`:1800-1814`): `message = ObRule.ProcessMessage(textoPlano)`; si devuelve no-null → `AddMessage(message)`. Excepción → suena `sounds\error.wav`. Las reglas son DLL en `<app>\ProcessRules\*.dll` que implementan `OmnimudCommonRules.IRule { string Name; string ProcessMessage(string) }` (`IRule.cs`, `CProcessRules.cs`); en el repo: CBalzhur, CCallandor, CCyberlife, CSimauria. **El texto NO se retira de Recibido**: el mensaje aparece en ambos cuadros.
10. **Parpadeo**: `if (!activo) CWindowFlicker.FlickWindow(Handle)` (`:1815`). Implementación (`CWindowFlicker.cs:45-86`): Timer WinForms de 500 ms que llama a `FlashWindow(handle, true)` durante 3 s; si ya está en marcha, reinicia la cuenta. `FlashWindowEx` está comentado. **Bug**: cada arranque hace `tmr.Tick += …` otra vez → los handlers se acumulan. No hay opción para desactivarlo.
11. **Log**: `textoPlano` con `\n`→`\r\n` y sin `all_speak:` (`:1816`).
12. **Voz** (`:1817-1829`) — ver §8.

### 5.4 Límite de tamaño
**No hay ningún límite ni recorte de `TxtRecibido` ni de `TxtMensajes`**: crecen indefinidamente (MaxLength por defecto del RichTextBox). Solo `cls` vacía Recibido. Cada minuto `TmrSecs_Tick` fuerza `GC.Collect()` (`:1147`) como paliativo de memoria.

---

## 6. Pipeline de envío

Enter → `TxtEnviar_KeyDown` → `ProcessInputCommand` (§4) → `SendCommand` (`:2084-2141`):

1. Marshalling a UI si hace falta; si `_offline_mode` → muestra `"Estás en modo de desconexión. No puedes enviar ningún comando."` (directo por `ProcessResponse`) y sale.
2. Añade `\n` final si falta. Sale si no hay conexión. Actualiza `_dt_last_activity`.
3. **Concatenación** (`UseCharacterConcat`, por defecto **desactivada**, carácter por defecto `;`): el doble carácter (`;;`) es el escape de un literal; se parte por el carácter. **Bug**: al restaurar el escape siempre se pone `";"` literal, no el carácter configurado (`:2121`).
4. **Repetición** (`UseCharacterRepeat`, por defecto **desactivada**, carácter `#`): si lo que hay antes del primer `#` parsea como entero → `N#comando` envía el comando N veces, **máximo 50**, negativos → 0. Cada repetición es un `cliente.send` independiente.
5. Cada subcomando se envía con `\n` final (no `\r\n`), codificado con `Encoding.Default`.
6. **Log del comando**: si `PasswordChar == 0` y hay log abierto, escribe el comando **original** (sin expandir) con `\r\n` (`:2140`). En modo contraseña no se registra.

Otros aspectos:
- **No hay eco local**: el comando enviado **no se añade a TxtRecibido** ni se verbaliza; solo va al log.
- **NO_ECHO / contraseña**: `IAC WILL ECHO` → `TxtEnviar.Multiline=false`, `PasswordChar='*'`; `IAC WONT ECHO` → lo contrario (§10). En ese modo lo tecleado no entra en historial, ni en `ultimo`, ni en el log. Pero pasa igualmente por `ProcessInputCommand` (alias, comandos internos, concatenación…).
- **Enter vacío**: repite el último comando (§3.2). Para enviar línea en blanco: Shift+Enter o Ctrl+Enter con el cuadro vacío.
- **Historial** (`CHistory : List<string>`, `CHistory.cs`; solo en memoria, por sesión): tamaño `_options.NumberOfLastCommands` (por defecto **10**, `COptions.cs:558`); la comprobación es `Count > N` antes de añadir → en la práctica guarda hasta **N+1**. **No se filtran duplicados** consecutivos; solo se evita re-añadir cuando se reenvía tal cual una entrada elegida con las flechas. Como `TxtEnviar_TextChanged` no está enganchado, `posCursorUltimos` no se resetea al teclear: tras navegar y escribir otra cosa, la siguiente flecha arriba continúa desde la posición anterior.
- **Multilínea**: el texto con `\r\n` se pasa **entero** como un solo comando: alias/comandos internos solo miran la primera palabra / cadena completa; se envía tal cual (con `\r\n` interiores) en un único `send`.
- Movimientos del teclado numérico y triggers también entran por `ProcessInputCommand`, pero **no** se añaden al historial ni a `ultimo`.

---

## 7. Sistema de mensajes

- Origen: `IRule.ProcessMessage` (§5.3.9) o `CTriggerFunctions.AddMessage` desde triggers de código.
- Almacenamiento (`CMessages.cs`): `CMessage { DateTime Time; string Message; int CharStart; int CharEnd }` en `CMessageCollection : CollectionBase`. `AddMessage` (`FrmCliente.cs:2041-2073`) crea el mensaje con `DateTime.Now` y los offsets actuales de TxtMensajes, y añade `Message` (con `\n`→`\r\n`) + `\r\n` al cuadro.
- **Límite**: `MAX_MENSAJES = 1000`; se elimina el más antiguo cuando `Count > 1000` → hasta **1001** en memoria. **El texto de TxtMensajes no se recorta nunca**.
- Cursor en Mensajes: opción `CursorPositionOnMessages` (GoEnd / Keep / DependingOnCurrentPosition por defecto), misma lógica que Recibido.
- **Lectura con Ctrl+número / Ctrl+º**: ver §3.1. Dice exactamente `"<n>: <texto del mensaje>"` con interrupción de voz. **No incluye la hora. No hay segunda pulsación para copiar o deletrear.** Funciona aunque la ventana de mensajes esté oculta.
- **Timestamps**: no se muestran en el texto. Se consultan con (a) **Ctrl+F** en un cuadro de solo lectura → verbaliza `"HH:mm:ss, el dd/MM/yyyy"` del mensaje bajo el cursor de TxtMensajes; (b) panel `StlMsg` de la barra de estado al mover el cursor por TxtMensajes. La búsqueda por offset es `GetMessageFromCharIndex(charindex, wrap_line)` (`CMessages.cs:153-211`), con heurística según la línea visual.
- Los mensajes no se persisten ni tienen log propio (van al log general como parte del texto recibido).

---

## 8. Síntesis de voz (`CSintetizer.cs`)

- Lectores (`COptions.ScreenReaders`): `None`, `JAWS` (**valor por defecto**, `COptions.cs:559`), `WindowEyes`, `NVDA`, `AutoDetect`.
  - JAWS: P/Invoke `jfwapi.dll` → `JFWSayString(text, interrupt)`.
  - NVDA: `nvdaControllerClient32.dll` → `nvdaController_cancelSpeech()` si interrupt + `nvdaController_speakText(bytes UTF-16)`.
  - Window-Eyes: COM `GWSPEAKLib.SpeakClass` → `Silence()` si interrupt + `SpeakString`.
  - AutoDetect (`:68-82`): NVDA si `testIfRunning()==0`; si no, proceso `JFW`; si no, proceso `"wineyes.exe"` (**bug**: `GetProcessesByName` no lleva `.exe`, nunca detecta Window-Eyes). Se re-detecta en **cada** llamada.
  - **No hay SAPI ni salida braille propia**. Errores → MessageBox (¡en cada texto recibido si falta la DLL!).
- **Qué se habla y cuándo** (`FrmCliente.cs:1817-1829`):
  - Todo el texto recibido de cada ráfaga, **solo si la ventana está activa** (`activo`, vía Activated/Deactivate). **No existe opción de hablar fuera de la ventana** (sí la hay para sonidos/música). No depende de qué control tenga el foco.
  - `interrupt = false` para el texto entrante (se encola); `interrupt = true` solo para la lectura de mensajes con Ctrl+número/Ctrl+º.
  - **Modo silencio** (`silent`, F8 / `callate`): no se habla nada salvo que la ráfaga contenga `all_speak:`; entonces se habla **solo lo que va detrás de la primera aparición** (`Substring(ind+10)`). Fuera de modo silencio el marcador simplemente se elimina. `all_speak:` también se elimina de la pantalla y del log. El marcador puede venir del MUD, de triggers (`OmDisplay`) o de mensajes internos.
  - **Filtrado previo**: únicamente se quitan códigos ANSI SGR reconocidos, comandos MSP, telnet, BS y `\r`. No hay filtrado de símbolos, ni troceo por longitud, ni por líneas, ni supresión de prompts/duplicados.
- Otros usos: `"OK"` tras `cls`; lectura de la línea anterior/siguiente en TxtEnviar multilínea; hora del mensaje (Ctrl+F); `SayText` desde triggers.
- El modo silencio no afecta a sonidos ni a la lectura de mensajes con Ctrl+número.

---

## 9. Conexión y ciclo de vida

- **Arranque**: `new FrmCliente(mud[, character])` carga opciones (de personaje o de mud), movimientos, diccionario de direcciones; crea `cClient(host, port)`; ajusta la regla (`:484-516`). `Show()` → Load (menús, fuente, alias/triggers/paths del personaje, historial) → `Connect()` (`:526-544`): `BeginConnect`, estado `Connecting`, botón `&Cancelar`, menús ocultos, `StlInfo = "Conectando a <mud>..."`.
- **Conexión OK** (`ResultOperation`, `:231-328`): habilita los cuadros, `StlSecs="0 segundos."`, abre log, foco a TxtEnviar, `StartReceiving()`, botón `&Desconectar`, menús visibles, arranca `TmrSecs`.
- **Login automático** (`:312-314`): nada más conectar, **sin esperar a ningún prompt**, envía `character.Name + "\n"` y, si hay contraseña, `character.Password + "\n"` con `cliente.send` directo (no pasa por SendCommand → no se registra ni entra en historial). Solo si se conectó con personaje.
- **Fallo de conexión → modo offline** (`:253-265`): MessageBox `"Error al conectar al mud: …"` y pregunta `"¿Deseas iniciar este personaje en modo de desconexión?"`. No → cierra. Sí → `_offline_mode=true`: interfaz habilitada, botón `&Cerrar`, `StlInfo="Modo de desconexión con <pj> (<mud>)"`, no se recibe nada, `SendCommand` responde con el aviso de desconexión. Sirve para gestionar alias/triggers/paths. (Las respuestas de comandos internos no se ven, §4.)
- **Proxy**: `COptions` tiene `ProxyType/ProxyHost/ProxyPort`, pero **solo se usan para HTTP** (`CCheckUpdates.cs:55-64`, `CMailer.cs:41-50`). La conexión al MUD **nunca usa proxy**.
- **Reconexión**: **no existe** (ni automática ni manual). Si el servidor corta (`DisconnectBy.Server`) el formulario **se cierra solo** (`:1094-1097`) y reaparece la ventana padre (`Owner.Show()`, `:935`). No hay keep-alive.
- **Botón de acción** (`:1394-1407`): `Connecting` → cancela (`cliente.Close()` + `Close()`); `Connected` → `Close()`.
- **Cierre** (`FrmCliente_FormClosing`, `:1053-1074`):
  1. Si hay conexión, `ConfirmBeforeExiting` (por defecto true) y el motivo no es `ApplicationExitCall` → `"¿Estás seguro de que deseas cortar la conexión?"` Sí/No.
  2. Si `TrySaveBeforeExiting` (por defecto true), `QuitCommand` no vacío y estado Connected → envía **`mud.QuitCommand`** (ojo: la opción se llama "intentar salvar" pero envía el comando de **abandonar**, no `SaveCommand`).
  3. `cliente.Disconnect()` inmediatamente (no espera respuesta del MUD).
  4. Log: `"Partida finalizada el dd/MM/yyyy a las HH:mm:ss."` (desde `cliente_ClientDisconnect`, `:1079-1083`); `"Partida interrumpida inesperadamente, el …"` solo si el log sigue abierto en FormClosing (en la práctica: modo offline).
  5. `FormClosed` (`:933-976`): muestra el Owner, para sonidos y música, vacía colecciones, `GC.Collect()`, `Dispose`.
- **Timer de la barra de estado** (`TmrSecs_Tick`, `:1138-1188`): contador propio sg/mn/hr/ds incrementado cada tick (no usa reloj real → deriva). Formato: `"D día(s), H hora(s), Mminuto(s), S segundo(s)."` omitiendo componentes a cero. **Errata**: falta el espacio en `"minuto"` → `"5minutos"`.
- **Paneles de la barra de estado**:
  - `StlInfo`: `"Conectando a X..."` → `"Conectado con <pj> (<mud>)"` / `"Conectado a <mud>"` / `"Modo de desconexión con …"`.
  - `StlSecs`: tiempo de conexión (arriba).
  - `StlMsg` (solo visible si hay regla IRule): `"Sin mensajes"` / `"Último mensaje recibido a las HH:mm:ss, del dd/MM/yyyy"` / `"Mensaje seleccionado: …"`.
- `CStaticInfo.CharactersInUse` está definido pero **no se usa en ningún sitio**: no hay control de "personaje ya conectado". La comprobación de instancia única en `Program.cs:34-41` está comentada.
- Excepciones no controladas: `Program.cs` ofrece enviar informe al web service (usa `InputBox` de `CInputBox.cs` para email/comentario) y pregunta si continuar.

---

## 10. Negociación telnet (`ProcessNegotiation`, `FrmCliente.cs:1471-1507`; constantes en `CNegotiation.cs:19-44`)

- Implementación por **split de la cadena por IAC (255)**. Cada trozo que empiece por WILL(251)/WONT(252)/DO(253)/DONT(254) y mida ≥ 2 se considera comando; se le quitan 2 caracteres y el resto es texto.
- Opción `EnableTelnetNegotiation` desactivada → se eliminan los comandos del texto y **no se responde nada** (tampoco cambia el modo contraseña).
- Activada:
  - **`IAC WILL ECHO(1)`** → modo contraseña (`Multiline=false`, `PasswordChar='*'`) y responde **`IAC DO ECHO`**.
  - **`IAC WONT ECHO(1)`** → modo normal y responde **`IAC DONT ECHO`**.
  - **Cualquier otra opción** (incluidos todos los `DO x` / `DONT x` del servidor): se elimina del texto y **no se responde** (ni WONT ni DONT). No hay NAWS, TTYPE, SGA, EOR, MCCP, MXP, GMCP, MSDP, CHARSET…
  - `NEGOTIATE_MSP = 90` está definido pero **no se usa**: MSP se detecta por las cadenas `!!SOUND(`/`!!MUSIC(` sin negociar.
- **No soportado / bugs**: `IAC SB … IAC SE` (el contenido quedaría como texto), `IAC GA`/`IAC EOR` (al quitar el IAC queda visible el carácter 249 `ù` / 239 `ï`), `IAC IAC` (un `ÿ` literal desaparece), comando partido entre dos ráfagas, y un trozo de texto normal que casualmente empiece por `û ü ý þ` tras un `ÿ`. `CNegotiationCollection` y `NegotiateEnumState` están definidos pero no se usan (máquina de estados nunca implementada).

---

## 11. ANSI (`CColores.cs` — clase `CColours`; render en `FrmCliente.cs:1638-1784`)

- Parser: `response.Split(ESC + "[")`; el trozo 0 es texto plano. En cada trozo siguiente toma el primer `m`, y como código **solo lo que hay entre el último `;` y esa `m`** → de una secuencia multiparámetro **solo se aplica el último parámetro** (`ESC[1;31m` → solo rojo, se pierde la negrita). Además `LastIndexOf(";")` mira todo el trozo: si hay un `;` en el texto posterior a la `m`, se toma el código completo (`"1;31"`), no casa con nada y se ignora.
- Secuencias CSI que no sean SGR (`ESC[2J`, `ESC[H`, `ESC[K`…): si el trozo no contiene ninguna `m` se imprime crudo (`2J`); si hay una `m` más adelante en el texto, **se come todo el texto hasta esa `m`**.
- `RemoveColourStrings` = regex `ESC\[.+?m` (`CColores.cs:62-65`).
- **Códigos soportados**:

| Código | Efecto real |
|---|---|
| 0 | `inverse=false`, texto **Black**, fondo **White**, fuente Regular |
| 1 (bold/bright) | **FontStyle.Bold** (negrita de fuente; **no** hay colores brillantes) |
| 2 (dim) | quita Bold |
| 3 | Italic |
| 4 | Underline |
| 5 (blink) | se representa como **Bold** |
| 7 | intercambia color de texto y fondo (una vez) |
| 8 (hidden) | sin efecto |
| 9 | Strikeout |
| 22 | quita Bold |
| 23, 24, 25, 29 | **sin efecto** (código comentado) |
| 27 | deshace inverse |
| 28 | no contemplado (default) |
| 30-37 | color de texto |
| 39 | texto **White** |
| 40-47 | color de fondo |
| 49 | fondo **Black** |
| 90-97, 100-107, 38;5;n, 38;2;r;g;b | **no soportados** |

- **Paleta exacta** (colores con nombre de .NET, 8 colores, iguales para texto y fondo):

| ANSI | .NET | RGB |
|---|---|---|
| 30/40 | Color.Black | 0,0,0 |
| 31/41 | Color.Red | 255,0,0 |
| 32/42 | Color.Green | 0,128,0 |
| 33/43 | Color.Yellow | 255,255,0 |
| 34/44 | Color.Blue | 0,0,255 |
| 35/45 | Color.Purple | 128,0,128 |
| 36/46 | Color.Cyan | 0,255,255 |
| 37/47 | Color.White | 255,255,255 |

- **Sorpresa visual**: el fondo del cuadro se fuerza a **blanco** (`:463`) y el color inicial es el `ForeColor` por defecto (negro). Pero 37/39 ponen texto **blanco** y 0 pone negro sobre blanco → un MUD que envíe `ESC[37m` sin fijar fondo produce **texto blanco sobre fondo blanco** (invisible para quien ve; irrelevante para el lector de pantalla).
- Estado persistente entre ráfagas: `colorTexto`, `colorFondo`, `estilo`, `inverse`. Se crea un `new Font(...)` por cada cambio de estilo (sin Dispose).
- `CTriggerFunctions` expone constantes `Apearance*` (ESC[…m) para que los triggers de código coloreen el texto de `OmDisplay`.

---

## 12. Logs (`CLogs.cs`; uso en `FrmCliente.cs:269-288,1069-1083,1816,2140`)

- Opción `LogType`: `None`, `PerDay` (**por defecto**), `PerGame`. Directorio `_options.LogDirectory` (se crea si no existe; si falla, MessageBox y sin log).
- **Nombre de fichero**: `<LogDirectory>\<d>-<M>-<yyyy>.log` **sin ceros a la izquierda** (p.ej. `5-3-2011.log`). En `PerGame` se añade ` <H>-<m>-<s>` (p.ej. `5-3-2011 9-7-3.log`). **No incluye mud ni personaje** → con `PerDay`, todas las sesiones del día de cualquier personaje que compartan directorio van al mismo fichero (modo append).
- Apertura: al conectar (o al entrar en modo offline). `StreamWriter(file, append:true)` → codificación **UTF-8 sin BOM**; `Flush()` tras cada escritura. Si no se puede abrir (p.ej. otra instancia lo tiene abierto) → MessageBox y logs desactivados para la sesión.
- **Contenido**: cabecera `"Partida iniciada el dd/MM/yyyy a las HH:mm:ss."` (o `"Personaje iniciado en modo desconexión, el … a las ….\r.\n"` — con una **errata `\r.\n`** en el literal, `:280`); texto recibido **sin ANSI**, sin MSP, sin telnet, sin `all_speak:`, con CRLF; comandos enviados (texto original, sin expandir) salvo en modo contraseña y salvo el autologin; pie `"Partida finalizada el …"` o `"Partida interrumpida inesperadamente, el …"`. Sin marcas de tiempo por línea. Lo capturado por `OmGet` no se registra.
- Cierre: en desconexión (usuario o servidor) o en FormClosing.

---

## 13. Otras funcionalidades encontradas

- **Movimiento con teclado numérico**: `CMovementCollection` (tecla NumPad0-9 → dirección), por mud o por personaje; persistido vía `CRegistro`; configurable en `FrmMovements`; activable con F2 (estado persistente).
- **Variables de sesión** para triggers de código: `Hashtable TblVars` (`OmSetVar/OmQueryVar/OmRemoveVar/OmVarIsSet`, `:1842-1871`). Solo en memoria.
- **`OmGet(comando[, regexEsperada][, conColores])`**: envía un comando y captura la respuesta ocultándola de la pantalla (§5.3.4).
- **`OmDisplay(texto)`**: inyecta texto como si viniera del MUD (pasa por MSP, ANSI, triggers, reglas, log, voz).
- **`DtLastActivity`**: fecha del último envío, consultable por triggers (permite anti-idle por trigger).
- **Contadores** `StartCount(ticks, tipo, sonidoDurante, sonidoFin)` con `CExtendedTimer` (Timer con `Tag` e `IsDisposed`).
- **Sonidos de interfaz**: `sounds\click.wav` (historial), `sounds\pop.wav` (dirección grabada en path), `sounds\error.wav` (excepción en IRule), `url.mp3` (abrir URL), beeps del sistema.
- **Apertura de URLs y correos** desde los cuadros de solo lectura (Enter o clic).
- **Informe de errores/sugerencias** (menú Ayuda → `FrmReports`; web service `OmnimudReporting`).
- **`InputBox`** (`CInputBox.cs`): diálogo genérico estático (Label, TextBox, Aceptar/Cancelar; Enter acepta, Escape cancela); solo se usa en `Program.cs:69-70`.
- **`TextBoxBraille`** (no compilado): experimento de entrada braille tipo Perkins con `F D S J K L` = puntos 1-6 y espacio; escribe al soltar todas las teclas; cubre a-z, `, ; ( ) \ $`; signos de mayúscula / todo mayúsculas anunciados por JAWS pero **no aplicados** al escribir; verbaliza cada tecla (depuración). Inacabado.
- **`CStaticInfo`**: contenedor estático `CharactersInUse`, sin usar.

---

## 14. Resumen de bugs / cosas a medio hacer detectadas

1. `UcClient` completamente comentado; `tiflomud.csproj` obsoleto; `TextBoxBraille`/`Form1` sin compilar; `CNegotiation.cs.txt` duplicado.
2. `TxtEnviar_TextChanged` sin enganchar → el puntero de historial no se resetea al teclear.
3. Historial guarda N+1 y no deduplica; umbral de "multilínea" exige ≥ 3 líneas.
4. Ctrl+NumPad con modo movimiento → lee mensaje y además se mueve.
5. `FrmFindTextBox`: `CancelClose` nunca vuelve a false → diálogo incerrable tras buscar vacío.
6. `paths detener` sin grabación → excepción; `paths cancelar` no limpia las direcciones; `_x -r` no reversible → NullReference.
7. Escape de concatenación restaura siempre `;`.
8. ANSI: solo último parámetro; CSI no-SGR se come texto; 23/24/25/29 sin efecto; blanco sobre blanco.
9. Telnet: solo ECHO; nunca rechaza opciones; GA/SB/IAC IAC mal tratados.
10. Voz: AutoDetect no detecta Window-Eyes; `recolecta`/`pruebaom` llaman a JAWS a pelo; MessageBox por cada texto si falta la DLL; no se habla con la ventana inactiva.
11. `CWindowFlicker` acumula handlers de Tick.
12. Triggers: `incognitas` compartida entre triggers, flags de regex invertidos, `Regex.Replace` envía el texto completo.
13. Cierre: "TrySaveBeforeExiting" envía `QuitCommand` y corta sin esperar.
14. Respuestas de comandos internos invisibles en modo offline.
15. Erratas en literales: `"Sólo ay"`, `"5minutos"`, `"Mensaes:"`, `"cunalias"`, `"name"` en vez de "nombre", `"\r.\n"`.
16. Check de F8 desincronizable; cursiva del modo movimiento no aplicada al cargar; fuente de Opciones no aplicada en caliente (`ChangeTextFont` sin uso).
17. Sin límite de tamaño de los RichTextBox; `GC.Collect()` cada minuto.

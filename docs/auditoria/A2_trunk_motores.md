# OmniMud legacy (trunk) — Auditoría de MOTORES

Auditoría en modo solo lectura de `C:\projects\OMnimud\trunk` (C# WinForms, .NET Framework 2.0, versión de ensamblado 1.1.0.1).
Todas las referencias son `fichero:línea` relativas a `trunk\cliente\` salvo que se indique otra carpeta.
Los ficheros fuente están en ISO-8859-1 (Latin-1), salvo `ProcessRules\CCyberlife.cs` (UTF-8).

Convenciones: **[BUG]** = comportamiento verificado leyendo el código que casi seguro no era la intención; **[A MEDIAS]** = código muerto, comentado o no enlazado. Nada de lo aquí descrito se ha ejecutado: es análisis estático.

---

## 0. Mapa rápido de qué está vivo y qué no

| Elemento | Estado |
|---|---|
| `FrmCliente.cs` | Ventana de juego REAL. Contiene el motor de triggers, alias, paths, MSP y variables (no están en clases separadas). |
| `UcClient.cs` | **[A MEDIAS]** Copia antigua de la ventana de juego como UserControl (para pestañas). Se compila (`omnimud.csproj`) pero nadie la instancia. `ExecuteTrigger(tr,text)` antiguo, sin incógnitas ni C#. Ignorar. |
| `CApiFunctions.cs` | Solo un P/Invoke `SetForegroundWindow` (CApiFunctions.cs:10-11). NO es la API de triggers. |
| `CTriggerFunctions.cs` | La API REAL expuesta a los triggers C#/VB (clase base). |
| `CPipeClient.cs`, `CPipeServer.cs` | **[A MEDIAS]** Named pipes genéricos (namespace `Omnimud` con mayúscula, distinto del resto). Nadie los usa. |
| `CServer.cs` | **[A MEDIAS]** Listener TCP de pruebas que verbaliza por JAWS lo que lee. Nadie lo usa. |
| `CMailer.cs` | **[A MEDIAS]** Envío de informes por HTTP GET a `send_report.php`. Sustituido por el web service `OmnimudReporting`; nadie lo llama. |
| `Crc32.cs` | CRC32 de NullFX. Nadie lo usa. |
| `CData.cs`, `Data\SQLiteDataAccess.cs` | **[A MEDIAS]** `CData` vacía; `SQLiteDataAccess` está TODO comentado (`/* ... */`), mezcla SQLite con `MySqlTransaction`; apuntaba a `%AppData%\Omnimud\config.odb`. Intento abandonado de migrar del Registro a SQLite. |
| `CCollectionMuds.cs` | Clase vacía en namespace `tiflomud` (nombre antiguo del proyecto); no está en el csproj. |
| `CStaticInfo.cs` | `CharactersInUse` — nadie lo usa. |
| `FrmMap.cs`, `FrmPrueba.cs`, `Form1.cs` | Esqueletos / pruebas. El mapeador no existe (ver todo.txt). |
| `rdl\CRule.cs` | Regla "Reinos de leyenda" que devuelve siempre `null`. No está en la `.sln`. |
| `trunk\Backup\` | Copia antigua de todos los proyectos (generada por el asistente de conversión de VS, ver `UpgradeLog.htm`). |
| `omnimud.sln` | Incluye además `..\v2\src\Omnimud.Data` y `Omnimud.UI` (fuera del alcance). |

`trunk\todo.txt` (3 líneas): accesos directos que abran un mud/personaje; pestañas para varios muds a la vez; mapeador. Ninguno implementado.

---

## 1. TRIGGERS

### 1.1 Modelo de datos (`CTrigger`, CTrigger.cs:215-856)

| Campo | Tipo | Notas |
|---|---|---|
| `Name` | string | Clave única por personaje (búsqueda exacta, sensible a mayúsculas: CTrigger.cs:36-51). |
| `Guid` | string | GUID .NET (`System.Guid.NewGuid().ToString()`), único por personaje (CTrigger.cs:764-772). Se usa para nombrar la DLL compilada. |
| `Happen` | string | Patrón desencadenante. Si empieza por `@` es un trigger de COMANDO. |
| `Action` | string | Comando(s) a enviar, o código fuente C#/VB. Puede ser multilínea. |
| `Sound` | string | Ruta del sonido. |
| `What` | enum `TriggerAction` | `SendCommand=0, PlaySound=1, Boot=2, Sharp=3, VBNet=4` (CTrigger.cs:302-309). "Boot" es errata de "Both". |
| `Enabled` | bool | |
| `CaseSensitive` | bool | Por defecto `true` en los constructores cortos. |
| `UseRegExp` | bool | |
| `TriggerRegExpType` | enum | `RegExp=0, ReplaceRegExp=1` (CTrigger.cs:314-318). **Se persiste y se edita, pero NINGÚN código de ejecución lo consulta** (ver 1.4). |
| `Signature` | string | Firma RSA base64 (opcional). |
| `DynamicCode` | `CTriggerFunctions` | Runtime: instancia del ensamblado compilado (caché por objeto trigger). |
| `IsExecutting` | bool | Runtime: `true` mientras corre el hilo de un trigger C#/VB. |

Validación al construir desde XML (CTrigger.cs:845): obligatorio `name` y `happen`; `Boot` exige action y sound; `PlaySound` exige sound; `SendCommand` exige action. (Para Sharp/VBNet no se valida nada aquí.)

Validación en el formulario (FrmAddEditTrigger.cs:173-283): ningún campo puede contener `æ` (separador del Registro); si `Happen` empieza por `@` debe tener algo detrás y no contener espacios, y se deshabilita "usar expresión regular" (FrmAddEditTrigger.cs:397-408); si regex, se prueba a compilarla. Para C#/VB se oculta la opción "Expresión de reemplazo". **[BUG]** Editar un trigger lo deja siempre `Enabled=true` (FrmAddEditTrigger.cs:360, el 7º argumento es literal `true`).

### 1.2 Los 5 tipos de acción — qué hace cada uno (`ExecuteTrigger`, FrmCliente.cs:2666-2753)

1. **SendCommand (0)** — FrmCliente.cs:2669-2692
   - Si `UseRegExp`: envía `Regex.Replace(text, Happen, Action, ...)` a `ProcessInputCommand`. `text` es el BLOQUE COMPLETO recibido. `Regex.Replace` devuelve toda la entrada con las coincidencias sustituidas, así que **si la regex no casa el bloque entero, el texto sobrante del MUD también se envía como comando**. Por eso la ayuda recomienda patrones que lo cubran todo. **[BUG]** flags invertidos: si `CaseSensitive` usa `IgnoreCase`, y si no, no (FrmCliente.cs:2673-2674). Esto ocurre para AMBOS valores de `TriggerRegExpType`.
   - Si no es regex y hay incógnitas: sustituye `%1`, `%2`… en `Action` por cada incógnita (en orden ascendente con `String.Replace`, así que `%1` también pisa el prefijo de `%10`…`%19`) y llama a `ProcessInputCommand(resultado)`; **hace `return`** (por eso en `Boot` con incógnitas NO suena el sonido: FrmCliente.cs:2688 **[BUG]**).
   - Sin incógnitas: `ProcessInputCommand(Action)`.
   - Como pasa por `ProcessInputCommand`, la acción admite alias, paths (`_nombre`), comandos internos, otros triggers `@cmd`, concatenación `;` y repetición `#`. Una acción multilínea se envía tal cual (los `\n` separan comandos en el socket), pero alias/`@`/comandos internos solo se miran sobre la primera palabra del conjunto.
2. **PlaySound (1)** — FrmCliente.cs:2693-2697. Si la ventana no está activa y `PlaySoundsOutOfWindow` es false, no suena. Llama a `CSounds.PlaySound(tr.Sound)` = tipo `Sound`, volumen 100, sin loop, prioridad 50, sin búsqueda en el directorio del MUD (ruta tal cual; se le añade `.wav` si no tiene extensión). No comprueba `EnableSounds`.
3. **Boot (2)** = SendCommand + PlaySound (con el bug del `return` cuando hay incógnitas).
4. **Sharp (3)** y 5. **VBNet (4)** — FrmCliente.cs:2698-2752. Ver 1.5–1.7.

### 1.3 Cuándo y sobre qué se evalúan (texto recibido)

Cadena en `ProcessResponse` (FrmCliente.cs:1610-1831) por cada BLOQUE recibido del socket (un bloque = lo leído mientras `DataAvailable`, buffer 4096, decodificado con `Encoding.Default` = ANSI del sistema; cClient.cs:173-223):

1. `RemoveUnusedChars`: aplica backspaces (char 8) y elimina todos los `\r` (FrmCliente.cs:1433-1464).
2. `ProcessSounds`: quita y ejecuta las líneas MSP (sección 4).
3. `ProcessNegotiation`: quita la negociación telnet.
4. Si hay un `OmGet` pendiente y (no hay regex esperada o casa), el bloque se captura para el script y **NO se muestra, NO pasa por triggers, ni por reglas, ni por log, ni se verbaliza** (`return` en FrmCliente.cs:1624-1636).
5. Se pinta con colores ANSI partiendo por `ESC[`.
6. `textoPlano` = bloque SIN códigos ANSI (FrmCliente.cs:1797). Aún contiene el marcador `all_speak:` si lo hubiera.
7. `if (!_triggers_disabled) ProcessTriggers(textoPlano)` (FrmCliente.cs:1799).
8. Después: regla de procesamiento (`ObRule.ProcessMessage(textoPlano)` → ventana de mensajes), parpadeo de ventana si inactiva, log, lector de pantalla.

Conclusiones para el port:
- Evaluación **por bloque de red, no por línea**, **sin ANSI**, sin `\r`. No hay buffer de línea parcial: un texto partido entre dos paquetes no dispara.
- Los triggers se evalúan **DESPUÉS de pintar**: **no existe gag/ocultar línea ni sustitución del texto mostrado**. `ReplaceRegExp` NO reescribe la pantalla; (en teoría) solo construye el comando a enviar.
- `OmDisplay` reinyecta texto por `cliente_dataReceived`, de modo que vuelve a pasar por MSP, colores, triggers, reglas, log y voz (CTriggerFunctions.cs:422-425).

### 1.4 Algoritmo exacto de matching (`ProcessTriggers`, FrmCliente.cs:2595-2659)

```
lower = text.ToLower()
incognitas = new List<string>()        // ¡UNA sola lista para TODOS los triggers del bloque!
foreach trigger en orden de colección:
    si !Enabled o Happen empieza por "@": continuar
    happen = Happen; inicio = fin = false
    si happen empieza por "^": inicio = true; quitarlo
    si happen acaba  en  "$": fin    = true; quitarlo
    casa = (UseRegExp && Regex.IsMatch(text, Happen /*original*/, Singleline [+IgnoreCase si !CaseSensitive]))
        || text.Contains(happen)                                  // literal sensible, SIEMPRE se prueba
        || (!CaseSensitive && lower.Contains(happen.ToLower()))
        || ( CaseSensitive && sscanf(text,  Happen,           incognitas) > 0)
        || (!CaseSensitive && sscanf(lower, Happen.ToLower(), incognitas) > 0)
    si casa:
        execute = true
        si (inicio || fin) && incognitas.Count == 0:
            text = TrimBlankLines(text)      // muta 'text' para los triggers siguientes
            si inicio && !text.StartsWith(happen): execute = false
            si execute && fin && !text.EndsWith(happen): execute = false
            si !execute:   // probar línea a línea
                para cada línea no vacía (split \r\n, \r, \n):
                    (inicio&&fin: StartsWith && EndsWith) | (inicio: StartsWith) | (fin: EndsWith) -> execute = true
        si execute && !IsExecutting: ExecuteTrigger(trigger, text, incognitas.ToArray(), null)
```

Puntos clave:
- **Orden**: el de la colección = orden de guardado en el Registro (orden de creación; editar conserva la posición, `ModifyTrigger` CRegistro.cs:941-949). No hay prioridad.
- **Un trigger NO corta a los siguientes**: se evalúan y ejecutan todos los que casen (no hay `break`).
- **Literal**: subcadena en cualquier parte del bloque. `CaseSensitive=false` compara en minúsculas.
- **`^` / `$` en modo literal**: se comprueban con `StartsWith/EndsWith` de forma SIEMPRE sensible a mayúsculas (incluso si el trigger no lo es **[BUG]**), primero contra el bloque entero (recortando líneas en blanco) y luego línea a línea. Con ambos, la línea debe empezar y acabar por el literal (en la práctica, ser igual).
- **Regex**: .NET, `RegexOptions.Singleline` (sin `Multiline` → `^`/`$` anclan al bloque entero). **[BUG]** si la regex empieza por `^` o acaba en `$`, tras casar se aplica ADEMÁS la comprobación literal `StartsWith/EndsWith` con el texto fuente de la regex, que casi nunca se cumple → esos triggers regex normalmente no se ejecutan (salvo que `incognitas` ya tuviera elementos de un trigger anterior).
- **Incógnitas que se acumulan [BUG]**: `incognitas` no se limpia entre triggers; el 2º trigger con comodines del mismo bloque recibe también las del 1º (sus `%1`… quedan desplazados) y además se salta la comprobación `^`/`$` literal.
- **Triggers insensibles con comodines**: `sscanf` se ejecuta sobre `lower`, así que **las incógnitas llegan en minúsculas**.
- `TriggerRegExpType` no aparece en `ProcessTriggers` ni `ExecuteTrigger` (grep: solo en CTrigger.cs, CRegistro.cs y FrmAddEditTrigger.cs). La diferencia "Expresión regular / Expresión de reemplazo" es únicamente de validación en el formulario (FrmAddEditTrigger.cs:202-203).
- Reentrada: `IsExecutting` solo se pone a true dentro del hilo de C#/VB (CThreadTrigger.cs:25,34); para los otros tipos no protege de nada.

#### `sscanf` (CParseStrings.cs:97-147) — comodines `%s %d %w %Ns %Nw` (y `%Nd` solo en la ayuda)

Transformación del formato a regex:
1. `%%` → marcador temporal (luego vuelve a ser `%` literal).
2. Limpieza de un signo/espacio pegado tras `%w` (regex `[^%]?\%\d*w([\.,\ ]{1,2})`, CParseStrings.cs:81-94,110): como `%w` ya consume su terminador, se elimina un ` `, `.` o `,` que venga detrás.
3. `Regex.Escape(formato)` y sustitución de `\%(\d{0,2})(s|d|w)` por grupos con nombre `grupo0`, `grupo1`…:
   - `%s` → `(?<grupoN>.+?)` (perezoso, 1+ caracteres cualesquiera)
   - `%d` → `(?<grupoN>\d+?)`
   - `%w` → `((?<grupoN>[^\.,\s\r\n]+?)(\.|\s|,|(\r\n)|\r|\n|$)){1}`
   - `%Ns` (N de 1-2 dígitos) → `(?<grupoN>.{N})` (exactamente N caracteres)
   - `%Nw` → N palabras separadas por `. , espacio` o salto, capturadas en un solo grupo
   - `%Nd` → **no implementado**: el `switch` solo conoce `%w %s %d` exactos; `%5d` se deja tal cual (escapado) y no cuenta como incógnita (CParseStrings.cs:66-72), aunque la ayuda 3.10.1 lo documenta.
4. Si no hay ninguna incógnita → devuelve 0 (no es un patrón sscanf).
5. `\^` inicial → `^`, `\$` final → `$` (anclas reales).
6. El texto se recorta (líneas en blanco + `Trim()`, dos veces) y **todos los `\n` se cambian por espacio**: el bloque se trata como UNA sola línea, por lo que `^`/`$` significan inicio/fin del BLOQUE, no de línea (contradice la ayuda).
7. `Regex.Matches(text, formato, Singleline)`; solo se usa la PRIMERA coincidencia. Devuelve nº de incógnitas añadidas a `rets` (todas como string); -1 si la regex resultante es inválida.
- **No es thread-safe**: usa listas `static` (`list_incognitas`, `num_grupo`).
- `AgregaResolucion` (CParseStrings.cs:27-34) es código muerto con un `MessageBox` de depuración.

### 1.5 Triggers de comando (`@comando`) — FrmCliente.cs:2238-2253

- En `ProcessInputCommand`, tras la expansión de alias, se compara `Happen` con `"@" + primeraPalabra` (exacto; en minúsculas si `!CaseSensitive`).
- Se ejecutan TODOS los que casen; `args` = resto de palabras (split por un espacio, puede haber vacíos), `FullCommand` = comando completo ya con alias aplicado.
- Si alguno casó, **el comando NO se envía al MUD** (`return`, FrmCliente.cs:2253), aunque el trigger estuviera ocupado (`IsExecutting`).
- Para `SendCommand` con args: `%1…%N` = args. Riesgo de recursión infinita si la acción empieza por el mismo comando.
- `-triggers` también desactiva estos.

### 1.6 Compilación de C#/VB (CTrigger.cs:717-758) y plantilla completa

Plantilla C# (CTrigger.cs:219-234), el código de usuario sustituye a `@@USER_CODE@@` rodeado de `\n`:

```csharp
using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading;
using omnimud;

namespace DynamicTrigger
{
    public class CDynamicTrigger :CTriggerFunctions
    {
        public override void Main(object context, string FullCommand)
        {
            string[] args = (string[])context;
            @@USER_CODE@@        }
    }
}
```

Plantilla VB (CTrigger.cs:236-250): `Imports System / System.Windows.Forms / System.Collections.Generic / omnimud / System.Threading`; `Namespace DynamicTrigger`, `Class CDynamicTrigger Inherits CTriggerFunctions`, `Public Overrides Sub Main(ByVal context As Object, FullCommand As String)`, `Dim args As String() = DirectCast(context, String())`.

- Compilador: `CSharpCodeProvider` / `VBCodeProvider` (CodeDOM, csc/vbc de .NET 2.0). `GenerateExecutable=false`, `GenerateInMemory=false`, sin debug.
- Referencias: el propio `omnimud.exe` (`Application.ExecutablePath`), `System.Windows.Forms.dll`, `System.dll`, `System.web.dll` (CTrigger.cs:730-733). No System.Xml ni System.Drawing.
- Los números de línea de los errores se corrigen restando 14 (C#) o 13 (VB) (CTrigger.cs:746).
- **Variables disponibles para el código**: `args` (string[]), `FullCommand` (string; `null` en triggers de texto), más todo lo heredado de `CTriggerFunctions`, incluida la propiedad pública `Cln` (el `FrmCliente` completo → acceso total a la ventana, opciones, mud, etc.).
- **Contenido real de `args`**: en triggers de texto = SOLO las incógnitas (`args[0]` = primera), vacío si no hay comodines o si es regex (los grupos de la regex NO se pasan). La ayuda 3.10.3 dice en un párrafo que `args[0]` es el texto completo, pero su propio ejemplo y el código (FrmCliente.cs:2655, 2741) dicen lo contrario. El texto que disparó el trigger NO está disponible para el script.

**Caché de DLL**: `CTrigger.GetUserTriggerPath()` = carpeta padre de `Application.UserAppDataPath` (CTrigger.cs:774-779), es decir `%AppData%\<Company>\<Product>\` (con `AssemblyCompany("")` y producto `omnimud`, previsiblemente `%AppData%\omnimud\omnimud\`; no verificado en ejecución). Fichero: `<CharacterID>_<TriggerGuid>.dll`.
- Se compila al pulsar Aceptar en el editor (FrmAddEditTrigger.cs:304-314); si falla no se guarda.
- En ejecución, si la DLL no existe se compila de forma perezosa (FrmCliente.cs:2717-2725) — p. ej. triggers importados.
- Se carga con `Assembly.Load(File.ReadAllBytes(...))` (sin bloquear el fichero) y se crea `DynamicTrigger.CDynamicTrigger`; la instancia se cachea en `tr.DynamicCode` mientras viva el objeto trigger (FrmCliente.cs:2701-2728). Cada recompilación carga otro ensamblado en el AppDomain (nunca se descargan).
- La DLL se borra al eliminar el trigger o al cambiarlo a un tipo no-código (FrmTriggers.cs:331-335, FrmAddEditTrigger.cs:317-331).
- Sin sandbox: confianza total (CAS no configurado).

**Firma RSA** (CTrigger.cs:697-706; herramienta `SignCode\FrmSign.cs:56-61`):
- Clave pública embebida `Resources\key_sign.xml` (copia idéntica en `xml\key_sign.xml`): RSA 1024 bits (módulo de 128 bytes), exponente `AQAB`.
- Firma = `RSACryptoServiceProvider.SignData(Encoding.Unicode.GetBytes(code.Trim()), SHA1)` → PKCS#1 v1.5 + SHA-1 sobre el código en UTF-16LE recortado; en base64. CSP "Microsoft Strong Cryptographic Provider".
- SignCode carga la clave PRIVADA desde un XML externo (no está en el repo; sí hay `firma.pfx` y `tiflomud_TemporaryKey.pfx`, que son de firma ClickOnce/ensamblado, no de triggers) y copia al portapapeles `<signature>…</signature>`.
- **Solo se verifica al IMPORTAR un fichero de triggers** (CTrigger.cs:155-168): sin firma → aviso; firma inválida → aviso fuerte y, si se acepta, se borra la firma. Al importar un personaje/mud completo (`CreateFromXml`, CTrigger.cs:188-203) NO se verifica. En ejecución nunca se verifica. Al editar, si el código ya no casa con la firma se avisa y se elimina (FrmAddEditTrigger.cs:341-359).

### 1.7 Ejecución en hilos (CThreadTrigger.cs, CThreadWorkItem.cs, FrmCliente.cs:2736-2746)

- `tr.DynamicCode.AsignClientForm(this)` y `ThreadPool.QueueUserWorkItem(new CThreadTrigger().CallbackMethod, p)` con `p = {trigger, fn, incognitas, FullCommand}`.
- En el hilo: `ThreadStart()` (contador estático `Interlocked`), `IsExecutting=true`, `fn.Main(incognitas, FullCommand)`; se traga `ObjectDisposedException` (ventana cerrada); `finally`: `ThreadDone()`, `IsExecutting=false`. Cualquier otra excepción del script queda sin capturar en el hilo del pool → `UnhandledException` → diálogo de informe de error y cierre de la aplicación (Program.cs:114-130).
- Mientras corre, el MISMO trigger no puede dispararse otra vez (se descarta, no se encola). Triggers distintos corren en paralelo.
- `CThreadWorkItem.Interrupt` existe pero nunca se pone a true: no hay cancelación cooperativa. Al cerrar la ventana los scripts mueren por `ObjectDisposedException` al tocar `Cln`.
- Marshalling a UI: `SendCommand` y `AddMessage` hacen `Invoke` (FrmCliente.cs:2092-2098, 2046-2051); `cliente_dataReceived` también (1104-1112). **`OmGet` NO hace Invoke**: se ejecuta en el hilo del script, con `Application.DoEvents()` + `Thread.Sleep(100)` en bucle. `ProcessInputCommand` tampoco hace Invoke, así que comandos internos (`cls`, etc.) lanzados desde script tocan controles desde otro hilo.
- Estado por instancia: `_ticks_count`, `_count_sound_name` viven en la instancia cacheada, compartida entre ejecuciones del mismo trigger.

### 1.8 API completa expuesta al código de trigger (`CTriggerFunctions`, CTriggerFunctions.cs)

| Firma | Semántica exacta |
|---|---|
| `virtual void Main(object context, string FullCommand)` | Punto de entrada que sobreescribe la plantilla (l.158). |
| `FrmCliente Cln {get;}` | El formulario de juego; lanza `ObjectDisposedException` si está cerrado (l.128-138). Da acceso a `Cln.Mud`, `Cln.Options`, `Cln.Triggers`, `Cln.Aliases`, `Cln.Paths`, `Cln.Movements`, `Cln.PtdCol`, `Cln.DtLastActivity`… |
| `void AsignClientForm(FrmCliente c)` | Uso interno; engancha `Disposed` para parar sonidos de triggers (l.146-150; ver bug en 4.6). |
| `string OmGet(string command)` | Envía `command` con `SendCommand` (¡sin alias ni triggers `@`!, FrmCliente.cs:1944) y devuelve el/los siguiente(s) bloque(s) recibidos, sin ANSI. |
| `string OmGet(string command, bool IncludeColourStrings)` | Igual, conservando secuencias `ESC[…m` si `true`. |
| `string OmGet(string command, string ExpectedString)` | Solo captura bloques que casen la regex `ExpectedString` (`Regex.IsMatch` sin opciones, sobre el bloque CON códigos ANSI); los que no casan se muestran normalmente. |
| `string OmGet(string command, string ExpectedString, bool IncludeColourStrings)` | Forma completa (FrmCliente.cs:1937-1953). Bloquea el hilo del script. Acumula bloques hasta que uno acabe en `\n` (sin colores) o en espacio (prompt), o **timeout de 3 s** (la ayuda dice 5). En timeout devuelve `""` (no `null` como dice la ayuda). Los bloques capturados NO se muestran, ni disparan triggers, ni se registran en log. Un solo `OmGet` a la vez (estado global en el formulario: `ToTriggerOmGet`, `TriggerResponse`, `TriggerResponseExpected`); dos scripts simultáneos se pisan. |
| `string RemoveColours(string text)` | `Regex.Replace(text, "\x1b\\[.+?m", "")` (CColores.cs:62-65). |
| `void OmSend(string command)` | `Cln.ProcessInputCommand(command)`: pasa por paths `_x`, alias, triggers `@`, comandos internos, y luego `;`/`#`. Cadena vacía → excepción (`comando[0]`). |
| `void PlaySound(string name)` | = `PlaySound(name, 0)`. |
| `void PlaySound(string name, int loop)` | Resuelve `name`: 1) tal cual si existe, 2) `Mud.SoundDirectory\name`, 3) `<app>\sounds\name`; si no existe, no hace nada. Reproduce como tipo `Triggers`, volumen 100, prioridad 0→50, `loop`: 0/1 = una vez, N>1 = N veces, -1 = infinito. wav/mp3/ogg. No mira `EnableSounds` ni si la ventana está activa. |
| `bool StopSound(string name)` | Misma resolución de ruta; detiene ese sonido de tipo `Triggers`; `true` si estaba registrado. |
| `void SayText(string text)` | Verbaliza por el lector configurado (`Cln.Options.ScreenReader`), sin interrumpir. No pinta nada. |
| `DateTime GetLastActivity()` | Hora del último `SendCommand` con conexión (FrmCliente.cs:2108); incluye los envíos hechos por triggers. |
| `void OmSetVar(string name, string value)` | Variable de SESIÓN (Hashtable `TblVars` del formulario; string→string; crea o sobrescribe). No persiste. Compartida entre todos los triggers de esa ventana. FrmCliente.cs:1842-1846. |
| `bool OmRemoveVar(string name)` | `true` si existía. |
| `string OmQueryVar(string name)` | Valor o `null`. |
| `bool OmVarIsSet(string name)` | |
| `void AddMessage(string msg)` | Añade a la ventana de mensajes (la muestra si estaba oculta por no haber regla) y a la colección de mensajes (máx. 1000; lectura con Ctrl+número). No verbaliza. |
| `void StartCount(int secs, string SoundEnd)` | = `StartCount(secs, "s", null, SoundEnd)`. |
| `void StartCount(int ticks, string type, string SoundEnd)` | = con `SoundUntil=null`. |
| `void StartCount(int ticks, string type, string SoundUntil, string SoundEnd)` | Cuenta atrás BLOQUEANTE (l.346-416). `type`: `s`=1000 ms, `d`=100, `c`=10, `m`=1, o con multiplicador `^\d+[sdcm]$` (`"2s"`=2000 ms por tick). Arranca `SoundUntil` en bucle infinito, espera `ticks` intervalos (timer + `Thread.Sleep`), lo detiene y reproduce `SoundEnd`. Se aborta si se cierra la ventana. `type` inválido → `NotImplementedException`. |
| `void OmDisplay(string text)` | Inyecta `text` como si viniera del MUD: MSP, ANSI, triggers, regla, log y voz. Si no hay conexión (modo offline) `cliente_dataReceived` lo descarta (FrmCliente.cs:1103). |
| `static object ObjetoBloqueo` | Objeto de lock público (solo lo usa `Cln_Disposed`). |

Constantes `public const string` (secuencias ANSI completas `ESC[<n>m`, para usarlas con `OmDisplay` o comparar en `OmGet(...,true)`), CTriggerFunctions.cs:27-54 y CColores.cs:15-59:
`ApearanceNormal`(0) `ApearanceBright`(1) `ApearanceBold`(1) `ApearanceDim`(2) `ApearanceItalic`(3) `ApearanceUnderline`(4) `ApearanceBlink`(5) `ApearanceInverse`(7) `ApearanceHidden`(8) `ApearanceStrikethrough`(9) `ApearanceBoldOff`(22) `ApearanceItalicOff`(23) `ApearanceUnderlineOff`(24) `ApearanceBlinkOff`(25) `ApearanceInverseOff`(27) `ApearanceHiddenOff`(28) `ApearanceStrikethroughOff`(29) `ApearanceBlack`(30) `ApearanceRed`(31) `ApearanceGreen`(32) `ApearanceYellow`(33) `ApearanceBlue`(34) `ApearancePurple`(35) `ApearanceCyan`(36) `ApearanceWhite`(37) `ApearanceDefault`(39). (Sic: "Apearance".) No hay constantes de fondo (40-49) en la API aunque el pintado las soporta.

Al ser .NET completo, los scripts usan además `Thread.Sleep`, `MessageBox`, `System.Web`, etc. La ayuda menciona un trigger de traducción vía web.

**Superficie mínima que debe cubrir la API Lua**: send (con y sin pipeline de alias), get-con-espera (con patrón esperado, timeout, con/sin color), display/echo reinyectable, say (TTS), play/stop sound con loop y búsqueda en 3 rutas, variables de sesión (set/get/remove/isset), add_message, contador con sonidos, last_activity, strip_colors, constantes ANSI, `sleep`, args/FullCommand, y acceso de lectura a mud/personaje/opciones. Conviene añadir lo que el legacy NO tiene: texto/línea que disparó, grupos de regex, gag/replace de línea, cancelación.

### 1.9 Comandos de consola relacionados (FrmCliente.cs:2257-2316, 2398-2400)

- `-triggers` / `+triggers`: desactiva/activa TODO el sistema para la sesión (no persiste).
- `-trigger <nombre>` / `+trigger <nombre>`: desactiva/activa uno y lo persiste en el Registro. El nombre no puede tener espacios (se toma `palabras[1]`).
- `triggers`: abre la ventana de triggers (también F6). En ella: Agregar, Editar, Quitar, Activar/Desactivar, Exportar (todos), Importar desde fichero o desde otro personaje.

### 1.10 Ejemplos reales

No hay triggers, alias ni paths de serie: `cliente\xml` y `cliente\Resources` solo contienen `key_sign.xml` e imágenes; `cliente\Data` solo el DAL comentado; no hay ningún `.oxf` en el repo. Los únicos ejemplos están en la ayuda (`chm\3.10.1.htm`, `3.10.3.htm`, `3.10.4.htm`):

- Simple con comodines: Happen `%s te dice: '%s'` → Action `decir Anda, %1 me ha dicho %2.`
- Anclado: `^%1w te mira$`; combinado: `^%s te dice: 'Dame %d mithriles'$`
- C#: Happen `^%s ha muerto.` → `OmSend("coger todo de " + args[0]);`
- Variables: `^Te duermes.$` → `OmSetVar("dormido","si");` · `^Te despiertas.$` → `OmRemoveVar("dormido");` · `^%w llega` → `if (OmVarIsSet("dormido") && OmQueryVar("dormido") == "si") { OmSend("despertar"); OmSend("¡Hola " + args[0] + "!"); }`
- Mensajes: `^%w canta: '%s'$` → `AddMessage(args[0] + " canta: '" + args[1] + "'");`
- Comando: Happen `@AvisaCura` → bucle con `Thread.Sleep(2000)`, `OmGet("pv")`, parseo `actual/max`, `PlaySound("curado.wav")`.
- Contadores: `StartCount(150, "m", "durante.mp3", "fin.mp3"); StartCount(3, "2s", "durante.mp3", "fin.mp3"); StartCount(8, "fin.mp3");`

---

## 2. ALIAS

- Modelo: `CAlias {Command, Action}`; colección = `SortedList` clave→acción (orden alfabético, CAlias.cs:16-46). **No hay Enabled/Disabled**, ni parámetros, ni regex.
- Matching (FrmCliente.cs:2227-2236): `palabras = comando.Split(' ')`; si `palabras[0]` es EXACTAMENTE una clave (sensible a mayúsculas, comparador por defecto del `SortedList`) se sustituye por la acción y se recompone: **el resto de palabras se añade detrás tal cual**. No hay `%1`, ni `$*`. Una sola pasada, no recursivo.
- Orden en `ProcessInputCommand`: 1) path `_…` (antes que alias), 2) alias, 3) triggers `@cmd`, 4) comandos internos `±triggers`, `±trigger`, `uncalias`, `calias`, 5) grabación de path, 6) `switch` de comandos internos exactos, 7) `SendCommand`. Por tanto un alias puede expandirse a un comando interno o a un `@trigger`, pero no a un path.
- `;` (concatenación) y `N#` (repetición) se procesan DESPUÉS, en `SendCommand` (FrmCliente.cs:2109-2139): el alias solo se aplica a la primera palabra de toda la línea, no a cada subcomando; una acción de alias que contenga `;` sí se trocea.
- Comandos inline:
  - `calias` (solo) → abre ventana de alias (F5).
  - `calias <cmd>` → muestra la acción asignada.
  - `calias <cmd> <acción…>` → crea (rechaza vacíos, `æ`, duplicado de comando; avisa si ya hay otro alias con la misma acción). FrmCliente.cs:2348-2374, 2758-2809.
  - `uncalias <cmd>` → borra (FrmCliente.cs:2318-2346; NRE si el personaje no tiene ningún alias **[BUG]**). El mensaje de error menciona erróneamente "cunalias".
  - Requieren sesión con personaje guardado.
- Ventana de alias: agregar/editar/quitar/exportar todos/importar desde fichero o desde otro personaje.
- Validación XML: atributos `command` y `action` obligatorios y sin `æ` (CAlias.cs:242-254).

---

## 3. PATHS, DICCIONARIO Y MOVIMIENTOS

### 3.1 Formato compacto (CPath.cs)
- Cadena de tokens `[N]c`: N = dígitos decimales opcionales (repeticiones), c = UN carácter no dígito (abreviatura del diccionario). Ej.: `5eo3e` = e,e,e,e,e,o,e,e,e.
- `SplitPath` (CPath.cs:245-268): corta tras cada carácter no dígito; si acaba en dígitos → `InvalidPathException`. Cualquier carácter no dígito vale como dirección (espacios incluidos).
- `ExpandPath` (304-328): repite cada dirección N veces (N multi-dígito permitido; `0x` la elimina).
- `CollapsePath` (337-365): run-length de la forma expandida (`sss`→`3s`). Se guarda SIEMPRE colapsado (FrmAddEditPath.cs:153,174).
- `IsValid` (276-298): no vacío, diccionario no nulo, sintaxis correcta y todas las abreviaturas presentes en el diccionario.
- Inversión `ReversePath` (81-91, 155-183): expande, recorre al revés y sustituye cada abreviatura por `OtherRealDir`; resultado = direcciones REALES separadas por `\n`. Si alguna no está → `null`.
- Normalización para ida: `NormaliceDirection` → `RealDir` (374-380).

### 3.2 Diccionario de direcciones (CPathDictionary.cs)
- Entrada `{RealDir (string), AbbrDir (char), OtherRealDir (string)}`; **por MUD** (compartido por todos sus personajes). Abreviatura única (búsqueda sensible a mayúsculas).
- **No hay valores por defecto**: ni en código, ni en recursos, ni en el instalador. Un MUD nuevo tiene diccionario vacío y sin él no se pueden crear ni ejecutar paths (FrmCliente.cs:2167-2171, 2421-2425; FrmAddEditPath.cs:139-144). Los únicos ejemplos están en la ayuda 3.11: `sur/s/norte`, `suroeste/q/nordeste`.
- Validación (FrmAddEditPathDictionary.cs:94-131): los tres campos obligatorios, sin `æ`, abreviatura = primer carácter del texto recortado, no duplicada.
- No se exporta suelto; viaja dentro del XML del MUD.

### 3.3 Ejecución (FrmCliente.cs:2156-2224)
- Sintaxis: `_nombre` (ida) o `_nombre -r` (vuelta). El nombre es todo lo que sigue a `_` (puede llevar espacios; exacto y sensible a mayúsculas; máx. 200 caracteres).
- Todas las direcciones se unen con `\n` y se mandan en UNA llamada a `SendCommand` → una sola escritura de socket. **Sin retardo entre pasos, sin esperar al prompt, sin posibilidad de abortar.** No pasan por alias ni triggers `@` (pero sí por `;`/`#` si están activos, porque eso ocurre en `SendCommand`).
- Si el diccionario está vacío el mensaje dice "No hay paths añadidos actualmente" (engañoso).
- **[BUG]** si `-r` no es invertible se muestra el error pero no hay `return` (FrmCliente.cs:2195-2201) → `SendCommand(null)` → `NullReferenceException`.

### 3.4 Grabación (FrmCliente.cs:2376-2389, 2414-2529)
- `paths` → abre la ventana (F7). `paths iniciar` → empieza. Mientras graba, todo comando que sea exactamente una abreviatura (1 carácter) o un `RealDir` del diccionario (`IsKnownDirection`) se guarda como abreviatura, suena `sounds\pop.wav` y se envía. Las teclas del numpad también cuentan (pasan por `ProcessInputCommand`).
- `paths ultima` (muestra la última), `paths ultima borrar`, `paths grabado` (muestra la cadena), `paths cancelar` (pide confirmación), `paths detener` → abre el formulario de alta con el camino colapsado.
- No se comprueba que el movimiento tuviera éxito en el MUD.
- **[BUG]** `paths cancelar` pone `tmp_path=null` pero no limpia `tmp_path_str`; `paths detener` sin ninguna dirección → `ArgumentNullException`.

### 3.5 Alta/edición de paths (FrmAddEditPath.cs:90-202)
Nombre no vacío, ≤200, sin `æ`, único por personaje; camino no vacío, sin `æ`, válido contra el diccionario; avisa si otro path tiene el mismo camino; avisa si no es invertible. Paths **por personaje**. Importar desde fichero o desde otro personaje; exportar todos.

### 3.6 Movimientos numpad (CMovements.cs, FrmMovements.cs, FrmCliente.cs:1242-1252)
- `CMovement {Keys NumberKey, string Direction}`; solo `NumPad0`..`NumPad9` (códigos `System.Windows.Forms.Keys` 96..105). No hay `+ - * / .` ni Enter.
- **No hay valores por defecto**: la lista aparece con las 10 teclas vacías (FrmMovements.cs:34-43). Solo se guardan las que tengan texto.
- Se guardan por MUD o por personaje (`MovementDeepLevel`), más un flag `MovEnabled` por MUD o personaje (menú "Modo Movimiento con teclado numérico", F2).
- En juego: conectado con personaje → solo los movimientos del PERSONAJE (FrmCliente.cs:501); sin personaje → los del MUD (489). **No hay herencia en juego**; el formulario de configuración sí precarga los del MUD si el personaje no tiene (FrmMovements.cs:30), y al Aceptar quedan copiados al personaje.
- Al pulsar la tecla (con el modo activo y tecla definida) se ejecuta `ProcessInputCommand(Direction)` — admite cualquier comando, alias, etc. — y se suprime la tecla.

---

## 4. SONIDOS

### 4.1 Parser MSP (`ProcessSounds`, FrmCliente.cs:1514-1601)
- Se trocea el bloque por `\n`. Una línea es MSP si `Length > 8` y **empieza** por `!!SOUND(` o `!!MUSIC(` (mayúsculas exactas, solo a principio de línea; no se detecta en mitad de línea).
- Paquetes partidos: si la línea MSP es la ÚLTIMA del bloque se guarda en `last_part_sound` y se antepone al bloque siguiente (la condición `EndsWith("\n")` nunca es cierta tras el split, así que SIEMPRE se difiere la última línea, esté completa o no).
- La línea MSP se elimina SIEMPRE del texto mostrado, suene o no.
- No suena si: tipo Sound y (`!EnableSounds` o (ventana inactiva y `!PlaySoundsOutOfWindow`)); tipo Music ídem con `EnableMusic`/`PlayMusicOutOfWindow`; o `mud.SoundDirectory` vacío o inexistente.
- Parámetros: `linea.Substring(8, len-9)` (quita prefijo y el último carácter, que se asume `)`) y `Split(" ")`. `[0]` = nombre (se cambian `/` por `\`). `off` como nombre → `CSounds.StopSounds(tipo)`.
- Resto (deben medir ≥3 caracteres, prefijo de 2 sensible a mayúsculas):
  - `V=` volumen int (si no parsea → 100)
  - `L=` repeticiones int (si no parsea → 1)
  - `P=` prioridad int (si no parsea → 50)
  - `C=` continuar: `Boolean.TryParse` → solo acepta `true`/`false`; **el `C=1`/`C=0` del estándar MSP da `false`**.
  - `T=` tipo → se usa como SUBDIRECTORIO
  - `U=` URL base de descarga
- No soporta comodines (`*`) en el nombre ni elección aleatoria. No hay negociación telnet de MSP (opción 90); solo detección en línea.

### 4.2 Reproducción (`CSounds.PlaySound`, CSounds.cs:195-329)
- Librería: **irrKlang** (`irrKlang.NET2.0.dll` v1.1.3, x86) + plugin `ikpMP3.dll`. Requiere VC++ redist (de ahí `vcredist_x86.exe`). Un `ISoundEngine` estático.
- Normalización: `loop==1`→0; `T=` se antepone como `tipo/nombre`; sin extensión → `.wav`; extensiones admitidas exactamente `mp3`, `ogg`, `wav` en minúsculas (otra → no suena); ruta = `SoundPath + "/" + nombre` con `/`→`\`.
- Valores por defecto: prioridad 0→50; volumen 0→100, <1→1, >100→100; `ISound.Volume = V/100f`. **No hay volumen maestro ni por categoría** en las opciones.
- `C=true` y ya sonando ese fichero → no hace nada (continúa). Si ya suena y no es continue → se reinicia.
- Se reproduce con `Play2D(name, false, startPaused=true, NoStreaming)` y luego se despausa.
- **Prioridades**: dos tablas `ISound→prioridad` (Sound y Music). Al reproducir: si algún sonido vivo de la tabla tiene prioridad MAYOR → el nuevo no suena; los de prioridad MENOR se detienen; igual prioridad → se mezclan. Se quita de la tabla al parar (`SoundStopEventReceiver`, CSounds.cs:506-530).
- **Categorías**: `SoundType { Sound, Music, Triggers }`. **[BUG]** para `Triggers` se consulta la tabla de MÚSICA (CSounds.cs:299) sin añadirse a ella: un sonido de trigger (prioridad 50) detiene músicas MSP con P<50 y es bloqueado por músicas con P>50.
- **Loop**: no usa el loop nativo; un `CExtendedTimer` relanza `Play2D` (en modo Streaming) cuando faltan ≤500 ms; contador N-1 o -1 infinito (CSounds.cs:316-327, 379-412).
- Limpieza: timer de 60 s libera sources que ya no suenan + `GC.Collect()` (336-371; **[BUG]** borra de `MusicNames` la lista de triggers).
- `StopSounds(tipo)`: para timers, libera sources y vacía la tabla de prioridades del tipo.

### 4.3 Descarga (CSounds.cs:242-287, 413-427; CSoundDownloader.cs)
- Solo si el fichero no existe o mide 0, `DownloadSoundsFromWeb` está activo, hay `U=` y hay directorio de sonidos.
- **No hay URL base configurada**: la URL viene únicamente del parámetro `U=` de cada orden MSP. URL final = `U` (sin `/` final) + `/` + `[T/]nombre[.wav]`. Destino = `SoundDirectory\[T\]nombre` (crea subdirectorios).
- `WebClient.DownloadFileAsync` (la clase se llama literalmente `s`, CSoundDownloader.cs:14). Proxy según opciones: `Automatic` = `WebProxy.GetDefaultProxy()` + credenciales por defecto; `Manual` = host:puerto con `UseDefaultCredentials`.
- Al terminar: si error, cabecera `Content-Refresh`, `Content-Type` que empiece por `text/html`, o tamaño 0 → borra el fichero; si no, lo reproduce con los parámetros originales (opciones `null`).
- **[BUG]** `IsDownloading(name)` compara con `FileName` (ruta local completa) → nunca coincide; puede lanzar descargas duplicadas del mismo sonido.
- Errores de descarga se verbalizan por JAWS (hardcoded, CSounds.cs:284).

### 4.4 Directorios
- Por MUD: `CMud.SoundDirectory` (valor `SoundDirectory` del Registro). Ruta absoluta, debe existir al guardar (FrmAddEditMud.cs:95-97). Sin él, no hay MSP en ese MUD. No hay valor por defecto.
- De la aplicación: `<StartupPath>\sounds\` (CSounds.cs:490-493). En el repo `trunk\sounds`: `click.wav`, `Pop.wav`, `reloj.mp3`, `reloj_fin.mp3`. El código usa además `url.mp3` (al pasar por una URL, FrmCliente.cs:1357), `error.wav` (excepción en regla, 1808), `click.wav` (historial, 1275/1302), `pop.wav` (grabación de path) y `dedicatoria.mp3` (huevo de pascua en FrmPrincipal.cs:204-220). El instalador solo incluye `Sounds\url.mp3`.

### 4.5 Fuera de ventana
`activo` lo ponen `Form.Activated/Deactivate` (FrmCliente.cs:925, 930). Opciones separadas para sonido y música. Los triggers tipo PlaySound/Boot respetan `PlaySoundsOutOfWindow`; `PlaySound()` desde script no mira nada.

### 4.6 Otros bugs
- `StopSounds(Triggers)` (al cerrar la ventana, CTriggerFunctions.cs:64-70) opera sobre las listas de MÚSICA (CSounds.cs:467-468): no para los sonidos de triggers.
- `StopSound` siempre quita el timer de `TimersTriggers` aunque el tipo sea otro (CSounds.cs:452).

---

## 5. OPCIONES (`COptions`)

### 5.1 Lista completa (orden = posición en la cadena del Registro; CRegistro.cs:1107-1165)

| # | Propiedad | Elemento XML | Tipo | Defecto `new COptions()` (COptions.cs:550-570) | Defecto si falta el campo en Registro |
|---|---|---|---|---|---|
| 0 | ConfirmBeforeExiting | `confirmExit` | bool | true | — |
| 1 | TrySaveBeforeExiting | `trySaveBeforeExit` | bool | true | — |
| 2 | ScreenReader | `screenReader` | enum int: None0 JAWS1 WindowEyes2 NVDA3 AutoDetect4 | JAWS(1) | — |
| 3 | EnableSounds | `enableSounds` | bool | true | — |
| 4 | EnableMusic | `enableMusic` | bool | true | — |
| 5 | PlaySoundsOutOfWindow | `playSoundsOutWindow` | bool | true | — |
| 6 | PlayMusicOutOfWindow | `playMusicOutWindow` | bool | true | — |
| 7 | LogType | `logType` | enum int: None0 PerDay1 PerGame2 | PerDay(1) | — |
| 8 | LogDirectory | `logDirectory` | string | null (global: `<app>\logs`) | — |
| 9 | CursorPositionOnReceived | `cursorOnReceived` | enum int: GoEnd0 Keep1 DependingOnCurrentPosition2 | 2 | — |
| 10 | CursorPositionOnMessages | `cursorOnMessages` | ídem | 2 | — |
| 11 | NumberOfLastCommands | `lastCommands` | int (≥1) | 10 | — |
| 12 | EnableTelnetNegotiation | `enableTelnetNegotiation` | bool | **false** (no se inicializa) | — |
| 13 | OptionLevel | (no se exporta) | enum int: Global0 Mud1 Character2 | Global | — |
| 14 | ProxyType | `proxyType` | enum int: Disabled0 Automatic1 Manual2 | Disabled | Disabled |
| 15 | ProxyHost | `proxyHost` | string | null | null |
| 16 | ProxyPort | `proxyPort` | int | 0 | 0 |
| 17 | DownloadSoundsFromWeb | `downloadSoundsFromWeb` | bool | true | true |
| 18 | TextFont | `font` | `"Familia;tamaño;estiloInt"` | Courier New;8.25;0 | `TextBox.DefaultFont` |
| 19 | UseCharacterConcat | `useCharacterConcat` | bool | false | false |
| 20 | CharacterConcat | `characterConcat` | código char en decimal | `;` (59) | `;` |
| 21 | UseCharacterRepeat | `useCharacterRepeat` | bool | false | false |
| 22 | CharacterRepeat | `characterRepeat` | código char en decimal (no dígito) | `#` (35) | `#` |

Fuera de `COptions`: `CheckUpdates` (global), `MovEnabled` (por mud/personaje), info personal.

Fuente (CFonts.cs): `FontFamily.Name;Size;(int)FontStyle`; el tamaño se serializa con la cultura actual (coma decimal en español, `8,25`); si no parsea → Arial 8.25.

Semántica:
- `TrySaveBeforeExiting`: pese al nombre, al cerrar envía el **QuitCommand** del MUD (FrmCliente.cs:1064-1066). `ConfirmBeforeExiting`: pregunta antes de cerrar si hay conexión.
- Concatenación/repetición (FrmCliente.cs:2109-2139): doble carácter = literal, pero **[BUG]** el escape se restaura siempre como `;` aunque el carácter configurado sea otro (l.2121). Repetición `N#cmd`, N limitado a 0..50; si lo anterior a `#` no es entero, se envía tal cual. Ambos caracteres deben ser distintos.
- `NumberOfLastCommands`: tamaño del historial (flechas arriba/abajo con `click.wav`). Enter con la caja vacía **repite el último comando**; si no hay, envía `\n`. Shift/Ctrl+Enter inserta salto de línea (o envía `\n` si está vacía). En modo contraseña (eco telnet desactivado) no se guarda en historial ni en log.
- Log: `<LogDirectory>\d-M-yyyy.log` (por día) o `d-M-yyyy H-m-s.log` (por partida), en append; texto plano sin ANSI, incluye lo enviado.

### 5.2 Herencia personaje → MUD → global (CRegistro.cs:1172-1289)

**Por objeto completo, no por opción, y sin flag "usar opciones propias".**
- `GetCharacterOptions(id)`: si el personaje tiene el valor `options` → esas. Si no → `GetMudOption(mud)` y `LogDirectory += "\<NombrePersonaje>"`.
- `GetMudOption(id)`: si el MUD tiene `Options` → esas. Si no → `GetGlobalOptions()` y `LogDirectory += "\<NombreMud>"`.
- `GetGlobalOptions()`: valor `Options` en la raíz; si no existe → `new COptions()` con `LogDirectory = <app>\logs`.
- Al pulsar Aceptar en Opciones de un nivel se guarda una COPIA COMPLETA en ese nivel; desde entonces deja de heredar. No hay UI para "volver a heredar" (habría que borrar el valor del Registro).
- La sesión carga opciones una vez al conectar (FrmCliente.cs:488, 500); cambiarlas desde la ventana de juego (F9) actualiza la sesión (FrmOptions.cs:286).
- Importar/exportar opciones a `.oxf` desde el diálogo.

---

## 6. PERSISTENCIA

### 6.1 Registro — `HKCU\Software\KastweySoftware\omnimud\` (CRegistro.cs:15)

Separador de campos: **`æ` (U+00E6; byte 0xE6 en el fuente)** (`REG_SEPARATOR`, CRegistro.cs:35). Prohibido en cualquier campo de usuario. Los nombres de valor del Registro no distinguen mayúsculas (el código mezcla `Options`/`options`, `PathsDictionary`/`pathsdictionary`).

```
HKCU\Software\KastweySoftware\omnimud
│  Options        REG_SZ   23 campos unidos por æ (tabla 5.1); versiones antiguas pueden tener 14..22
│  CheckUpdates   REG_SZ   "True"/"False" (el instalador lo escribe bajo ...\Omnimud)
├─ PersonalInfo
│     name, email, guid   REG_SZ
│     DontAskMeAgain      REG_SZ "yes"
├─ muds\<id>                       id = 30 letras aleatorias 'a'..'y' (GenerateId, CRegistro.cs:1659-1666)
│     name            REG_SZ
│     host            REG_SZ
│     port            REG_DWORD   (se lee con ToString+Int32.Parse)
│     SaveCommand     REG_SZ      ("" si no hay)
│     QuitCommand     REG_SZ
│     ProcessRules    REG_SZ      nombre de la regla; "" o "ninguna" = sin regla
│     SoundDirectory  REG_SZ
│     id              REG_SZ      (= nombre de la subclave)
│     DefaultCharacter REG_SZ     ¡NOMBRE del personaje, no su id!
│     options         REG_SZ      opcional (si falta, hereda)
│     movements       REG_MULTI_SZ  cada línea "<KeysInt>æ<comando>"   (96..105)
│     MovEnabled      REG_DWORD   0/1
│     PathsDictionary REG_MULTI_SZ  cada línea "<RealDir>æ<AbbrChar>æ<OtherRealDir>"
└─ characters\<id>
      name        REG_SZ
      password    REG_SZ   cifrada (sección 7); "" si no hay
      mud         REG_SZ   id del mud
      id          REG_SZ
      options     REG_SZ   opcional
      aliases     REG_MULTI_SZ  "<command>æ<action>"
      paths       REG_MULTI_SZ  "<name>æ<pathColapsado>"
      triggers    REG_MULTI_SZ  ver abajo
      movements   REG_MULTI_SZ
      MovEnabled  REG_DWORD
```

Línea de trigger (CRegistro.cs:885; lectura 799-805), 11 campos (se aceptan 9, 10 u 11; otra longitud se descarta en silencio):

```
0 Name æ 1 Happen æ 2 Action æ 3 Sound æ 4 (int)What æ 5 Enabled æ 6 CaseSensitive æ 7 UseRegExp æ 8 (int)RegExpType æ 9 Guid æ 10 Signature
```
Booleanos como `True`/`False`. Action/Sound vacíos → `null`. El código C# multilínea va dentro de la misma cadena (con sus `\r\n`).

Notas: el personaje se identifica por (nombre, mud); nombres de MUD únicos. Borrar un MUD borra sus personajes. Primer personaje creado en toda la instalación → predeterminado de su MUD (CRegistro.cs:445, mira el total global, no el del MUD). Cada operación relee/reescribe el Registro entero de esa entidad (sin caché). **[BUG]** `AddMud` guarda el literal `"ninguna"` como regla, mientras que editar guarda `""` (FrmAddEditMud.cs:107 vs 118); `AjustaReglaProcesamiento` tolera ambos (FrmCliente.cs:335) pero hace `mud.ProcessRule.ToLower()` sin comprobar null.

### 6.2 XML de import/export — ficheros `.oxf` (CXmlOperations.cs:32-33,53-54)

UTF-8, indentado, con declaración XML, sin namespaces ni atributo de versión. El elemento raíz determina el tipo. Booleanos `True`/`False`; enums como entero.

**Alias** (raíz `aliases`; CAlias.cs:61-70, 226-232):
```xml
<?xml version="1.0" encoding="utf-8"?>
<aliases>
  <alias command="abp" action="abrir puerta 1 con llave 3" />
</aliases>
```

**Paths** (raíz `paths`; CPath.cs:191-197, 491-500):
```xml
<paths>
  <path name="nandor-simauria" path="5eo3e" />
</paths>
```

**Triggers** (raíz `triggers`; CTrigger.cs:637-684). Orden de escritura: guid, name, happen, type, [action], [sound], enabled, caseSensitive, useRegExp, regExpType, [signature]. Al leer, el orden da igual, se exigen ≥6 hijos y los desconocidos se ignoran:
```xml
<triggers>
  <trigger>
    <guid>3f2b0c1e-....</guid>
    <name>saludar</name>
    <happen>^%w llega</happen>
    <type>3</type>
    <action>if (OmVarIsSet("dormido")) { OmSend("despertar"); }</action>
    <sound>c:\sonidos\x.wav</sound>
    <enabled>True</enabled>
    <caseSensitive>True</caseSensitive>
    <useRegExp>False</useRegExp>
    <regExpType>0</regExpType>
    <signature>BASE64...</signature>
  </trigger>
</triggers>
```
Si falta `guid` se genera uno. (**[BUG]** al importar un personaje completo se llama con `ch.ID` aún nulo, CCharacters.cs:237.)

**Movimientos** (`movements`; CMovements.cs:69-75; exactamente 2 atributos):
```xml
<movements>
  <movement number="104" direction="norte" />
</movements>
```

**Diccionario de paths** (`pathDictionary`; CPathDictionary.cs:101-114; exactamente 3 hijos):
```xml
<pathDictionary>
  <pathDictionaryEntry>
    <realDir>sur</realDir><abbrDir>s</abbrDir><otherDir>norte</otherDir>
  </pathDictionaryEntry>
</pathDictionary>
```

**Opciones** (raíz `options`; COptions.cs:579-655; al leer exige ≥15 hijos). Orden de escritura: confirmExit, trySaveBeforeExit, enableSounds, enableMusic, playSoundsOutWindow, playMusicOutWindow, downloadSoundsFromWeb, proxyType, [proxyHost], proxyPort, lastCommands, [logDirectory], logType, cursorOnMessages, cursorOnReceived, screenReader, enableTelnetNegotiation, font, useCharacterConcat, characterConcat, useCharacterRepeat, characterRepeat.
```xml
<options>
  <confirmExit>True</confirmExit>
  <trySaveBeforeExit>True</trySaveBeforeExit>
  <enableSounds>True</enableSounds>
  <enableMusic>True</enableMusic>
  <playSoundsOutWindow>True</playSoundsOutWindow>
  <playMusicOutWindow>True</playMusicOutWindow>
  <downloadSoundsFromWeb>True</downloadSoundsFromWeb>
  <proxyType>0</proxyType>
  <proxyPort>0</proxyPort>
  <lastCommands>10</lastCommands>
  <logDirectory>C:\...\logs\Simauria\Kastwey</logDirectory>
  <logType>1</logType>
  <cursorOnMessages>2</cursorOnMessages>
  <cursorOnReceived>2</cursorOnReceived>
  <screenReader>1</screenReader>
  <enableTelnetNegotiation>False</enableTelnetNegotiation>
  <font>Courier New;8,25;0</font>
  <useCharacterConcat>False</useCharacterConcat>
  <characterConcat>59</characterConcat>
  <useCharacterRepeat>False</useCharacterRepeat>
  <characterRepeat>35</characterRepeat>
</options>
```

**MUD** (raíz `mud`, o `muds` con varios; CMuds.cs:167-221, 445-453):
```xml
<mud>
  <name>Simauria</name>
  <host>mud.simauria.org</host>          <!-- valor ilustrativo, no sale del código -->
  <port>23</port>
  <saveCommand>salvar</saveCommand>        <!-- opcional -->
  <quitCommand>fin</quitCommand>           <!-- opcional -->
  <processRules>Simauria</processRules>
  <soundDirectory>c:\sonidos\simauria</soundDirectory>
  <defaultCharacter>Kastwey</defaultCharacter>  <!-- solo si se exportan personajes -->
  <options>…</options>                     <!-- siempre (las efectivas, heredadas incluidas) -->
  <movements>…</movements>                 <!-- si hay -->
  <pathDictionary>…</pathDictionary>       <!-- si hay -->
  <characters> <character>…</character> </characters>   <!-- si IncludeCharacters -->
</mud>
```
Importar: exige ≥3 hijos; si ya existe un MUD con ese nombre pregunta y lo BORRA con todos sus personajes antes de recrearlo. **[BUG]** `defaultCharacter` se lee pero no se guarda (`AddMud` no lo recibe).

**Personaje** (raíz `character`; CCharacters.cs:95-138):
```xml
<character>
  <name>Kastwey</name>
  <password>CIFRADA</password>             <!-- opcional; MISMO cifrado que el Registro -->
  <mud>Simauria</mud>                      <!-- nombre; o un <mud>…</mud> completo si se exporta "con mud" -->
  <options>…</options>
  <aliases>…</aliases>
  <paths>…</paths>
  <triggers>…</triggers>
  <movements>…</movements>
</character>
```
Importar (CCharacters.cs:167-291): `<mud>` con hijos → si existe un MUD con ese nombre se reutiliza (se toma `ChildNodes[0]` como nombre), si no se crea; `<mud>` de texto → debe existir o falla. Si el personaje ya existe en ese MUD, pregunta y lo sobrescribe. El importador nuevo debe tolerar ambas variantes de `<mud>`.

Excepciones específicas en XmlExceptions.cs (`XmlInvalidOxfFileException`, `…Trigger…`, `…Alias…`, `…Path…`, `…PathDictionary…`, `…Options…`, `…Movement…`, `…Character…`, `…Mud…`). Los mensajes de importar alias dicen "exportados" y los de triggers mencionan "paths" (copiar/pegar).

---

## 7. CIFRADO DE CONTRASEÑAS (CEncriptar.cs)

Sustitución posicional tipo César sobre dos alfabetos permutados de 62 caracteres (`[0-9A-Za-z]`). No es criptografía: ofuscación reversible sin clave secreta.

```
PatrEncripta = "PuZcetj0NBm6gR8Sr4yiJo1fnEvT3xA7IDwOXLMUYHK9dGCa5WqQh2kVlbszFp"
PatrBusqueda = "NZl3QMthqxdmjVPpR7vKF4CDUr5a2Tg6HwzWSy0LJkG9IAXOniBs8c1eobEfYu"
```
- Cifrar carácter `c` en posición `i` (base 0) de una cadena de longitud `n`: si `c ∈ PatrBusqueda` → `PatrEncripta[(PatrBusqueda.IndexOf(c) + n + i) mod 62]`; si no (símbolos, espacios, acentos, ñ…) → se deja igual.
- Descifrar: si `c ∈ PatrEncripta` → `PatrBusqueda[(PatrEncripta.IndexOf(c) − n − i) mod 62]` (módulo matemático, siempre ≥0); si no, igual.
- La longitud no cambia. Verificado con un script Python equivalente: ambas cadenas son permutaciones del mismo conjunto de 62 caracteres y el round-trip es exacto (`hola` → `6pNw`).
- Se usa igual en el Registro (`characters\<id>\password`) y en el XML (`<password>`).

Referencia para el migrador:
```python
E="PuZcetj0NBm6gR8Sr4yiJo1fnEvT3xA7IDwOXLMUYHK9dGCa5WqQh2kVlbszFp"
B="NZl3QMthqxdmjVPpR7vKF4CDUr5a2Tg6HwzWSy0LJkG9IAXOniBs8c1eobEfYu"
def dec(s):
    n=len(s)
    return ''.join(B[(E.index(c)-n-i)%62] if c in E else c for i,c in enumerate(s))
```
Auto-login: al conectar con personaje se envía `nombre\n` y, si hay, `contraseña\n` inmediatamente, sin esperar prompt (FrmCliente.cs:313-314).

---

## 8. MUDs PREDEFINIDOS DE SERIE

**No hay ninguno.** Ni en código (`AddMud` solo se llama desde el formulario y desde la importación XML), ni en recursos, ni en `cliente\xml`/`Data`, ni en el instalador (que no escribe claves de MUDs ni incluye `.oxf`). Una instalación limpia arranca sin MUDs, sin personajes, sin diccionario de direcciones y sin teclas de movimiento.

Lo único "de serie" por MUD son las **reglas de procesamiento** de `ProcessRules.dll`: `Balzhur`, `Callandor`, `Cyberlife`, `Simauria` (más `Reinos de leyenda` en `rdl`, vacía y fuera de la solución). No aparecen hosts/puertos de esos MUDs en ningún fichero del trunk.

Datos del MUD: Name, Host, Port (validado 0..99999), SaveCommand, QuitCommand, ProcessRule, SoundDirectory, DefaultCharacter. `SaveCommand` → menú Acciones/Salvar (F3); `QuitCommand` → Acciones/Abandonar (F4) y al cerrar si `TrySaveBeforeExiting`.

---

## 9. PROCESS RULES (directivas de procesamiento)

### 9.1 Infraestructura
- Interfaz `OmnimudCommonRules.IRule` (OmnimudCommonRules\IRule.cs:10-24): `string Name {get;}` y `string ProcessMessage(string msg)` → devuelve el texto para la ventana de MENSAJES o `null`.
- Carga (CProcessRules.cs:15-70): una vez por proceso; todos los `*.dll` de `<app>\ProcessRules\` con `Assembly.LoadFile`; instancia cada tipo con constructor sin parámetros que implemente `IRule`; descarta ensamblados y nombres duplicados; errores silenciados. `GetRule(name)` insensible a mayúsculas.
- Uso (FrmCliente.cs:1800-1814): se llama con `textoPlano` (bloque completo sin ANSI, con `\n`). Si devuelve no-null → `AddMessage`. **El texto NO se quita de la ventana principal** (se duplica en mensajes). Excepción en la regla → suena `sounds\error.wav`. Sin regla, la ventana de mensajes se oculta (aparece si un script llama a `AddMessage`).
- Es un sistema de plugins .NET de terceros (ayuda 3.14). En la versión nueva equivale a "patrones de canal de comunicación por MUD" y debería ser declarativo o Lua.

### 9.2 Balzhur (ProcessRules\CBalzhur.cs)
- Divide en líneas no vacías (`\r\n`, `\r`, `\n`) y cada línea en palabras por espacio.
- Canales (formato 1): `Clan, Faccion, Novatos, Orden, Concilio, Gremio, Raza, Avatar, Reino, Trivial, GOSOCIAL`. Casa si `palabras[0]` empieza por `[` y para algún canal: (`palabras[0]=="[Canal:"` y `palabras[1]` acaba en `]:` y `palabras[2]` empieza por `'`) o `palabras[0]=="[Canal]:"` o `palabras[0]=="[Canal]"`. → añade `linea + "\r\n"`. (**[BUG]** accede a `palabras[2]` habiendo comprobado solo `Length>1`.)
- Si no, casa si la línea empieza por `Charlas '`, `Cuentas a `, `Dices '`, `Respondes a `, `Susurras a `, o si: `palabras[1]=="charla"` y `palabras[2]` empieza por `'`; o `palabras[1]=="te"` y `palabras[2]` ∈ {`responde`,`cuenta`,`susurra`} y `palabras[3]` empieza por `'`. → añade `linea` **sin separador** (líneas consecutivas quedan pegadas).
- Devuelve la concatenación de todas las líneas casadas del bloque, o `null`.

### 9.3 Callandor (ProcessRules\CCallandor.cs; namespace `omnimud`)
- Divide por `\n`. Para la PRIMERA línea que case, devuelve desde esa línea hasta el final del bloque, cortado en la primera aparición de `'\n` (incluida; `Substring(0, index+3)` se lleva un carácter de más **[BUG]** menor); si no hay `'\n`, todo el resto.
- Propios (prefijos): `Solicitas '`, `Instruyes`, `Dices '`, `Charlas '`, `Transmites a `, `<Comunicas> '`, `Susurras '`, `Gritas '`, `Conspiras '`, `Gruñes '`, `Transmites cariñosamente a `.
- Ajenos (palabras por espacio; `X` = `palabras[0]` cualquiera):
  - `X te transmite '…`
  - `X dice al grupo '…` · `X gruñe al grupo '…` · `X dice al equipo '…`
  - `X (dice|charla|conspira|comunica|gruñe) '…`
  - `X susurra '…`
  - `X grita cerca de aqui '…` (sin tilde) · `X grita '…`
  - `<…> conversa '…` (`palabras[0]` entre `<` y `>`)
  - `X te transmite cariñosamente '…`
  - `X solicita '…`
  - `Trivial: X '…` (`palabras[0]=="Trivial:"` y `palabras[2]` empieza por `'`)
- Un solo mensaje por bloque. Entrada vacía → devuelve la propia entrada.

### 9.4 Simauria (ProcessRules\CSimauria.cs)
- Divide por `\n`. Por cada línea:
  - Canal: si `i>0`, la línea anterior es vacía, esta empieza por `[` y existe `]` a partir del desplazamiento acumulado → devuelve desde esta línea hasta el final del bloque.
  - Conversación: `X dice: '…` (`palabras[1]=="dice:"`), `X te dice: '…`, `Dices: '…` (`palabras[0]=="Dices:"`), `Dijiste a X: …` (`palabras[0]=="Dijiste"`, `[1]=="a"`, `[2]` acaba en `:`). Si `palabras[1]` es `a` o `te` → devuelve hasta el final del bloque; si no → hasta el ÚLTIMO apóstrofo del bloque incluido.
- Un solo mensaje por bloque.

### 9.5 Cyberlife (ProcessRules\CCyberlife.cs; UTF-8, C# moderno — añadido posterior)
15 regex con `Compiled | Multiline | IgnoreCase`, probadas en orden; devuelve `m.Value` de la PRIMERA que case (una línea, por `^…$` multilínea), o `null`:
1. `^(Murmuras|Dices) con acento .+?, ".+?"$`
2. `^(Murmuras|Dices): ".+?"$`
3. `^gritas: ".+?"$`
4. `^.+? grita: ".+?"$`
5. `^.+? grita cerca de aquí: ".+?"$`
6. `^\[.+?\] .+?: ".+?"$`
7. `^\[.+?:\] ".+?"$`
8. `^.+? (Murmura|Dice) con acento .+?, ".+?"$`
9. `^.+? (Murmura|Dice): ".+?"$`
10. `^".+? chatea: ".+?"$` (sic, comilla inicial)
11. `^Transmites a .+?, ".+?"$`
12. `^.+? te transmite, ".+?"$`
13. `^\*{2,} .+? Ha solicitado asistencia con el siguiente motivo: .+?\*{2,}$`
14. `^.+?(te)? dice por teléfono, ".+?"$`
15. `^dices por teléfono, ".+?"$`

Usa sintaxis C# 6 (`=>`) pese a que el csproj apunta a .NET 2.0. Como el cliente ya eliminó los `\r`, `$` funciona.

---

## 10. Program.cs, instancia única, pipes, actualizaciones, informes

### 10.1 Arranque (Program.cs:31-52)
- `static void Main()` **sin parámetros: no hay argumentos de línea de comandos** (coherente con el TODO de accesos directos).
- **Instancia única: NO.** El bloque que lo hacía (`Process.GetProcessesByName`…) está comentado (Program.cs:34-41). Varias instancias comparten Registro sin sincronización; dos escribiendo en el mismo log provocan el aviso de FrmCliente.cs:285.
- Registra `AppDomain.UnhandledException` y `Application.ThreadException`; lanza `FrmPrincipal`.
- P/Invoke `OpenIcon`/`SetForegroundWindow` sin uso.

### 10.2 Pipes / servidor
`CPipeClient`/`CPipeServer`: named pipes Win32 (dúplex, overlapped, buffer 4096, ASCII) con evento `MessageReceived`. `CServer`: `TcpListener` de prueba. **Ninguno se instancia**: estaban pensados para pasar órdenes a la instancia viva, pero no hay nada conectado. No hay protocolo que portar.

### 10.3 Actualizaciones (CCheckUpdates.cs + AutoUpdater)
- Al arrancar, si `CheckUpdates`==`True` (FrmPrincipal.cs:189). Menú: "Buscar actualizaciones" y casilla "comprobar al inicio".
- Requiere `<app>\autoupdater.exe`. Descarga `http://www.omnimud.org/omnimud.xml` a `<app>\omnupd.xml` (escribe en la carpeta del programa), lee `//version` y `//priority`.
- Comparación de versiones (CCheckUpdates.cs:26-34): quita puntos, rellena con ceros a la derecha y compara como enteros (`1.10.0.0` y `1.1.0.0` → iguales **[BUG]**).
- Si hay nueva y el usuario acepta: `autoupdater.exe -nomnimud -v<ver> -p<ruta> -x<urlXml> [-o<host:puerto>] -i<pid>` (guiones literales duplicados `--`; **[BUG]** falta un espacio antes de `-o`) y sale.
- AutoUpdater (`AutoUpdater\Program.cs`, `FrmPrincipal.cs`): descarga el catálogo a `update.xml`; bajo `<current>`: `version`, `author`, `reldate`, `comments`, `priority`, `<download url>`, `<operations>`, `<msgsuccess title text>`; opcional `<miniupdate version url>` + `<minioperations>` para parche incremental cuando la versión instalada coincide. Operaciones: `displaymessage[type,title]`, `killprogramprocess`, `exec[wait,setupfactory6]`, `delete[checkIfExists]`, `unzip` (SharpZipLib `FastZip` sobre la carpeta del programa).
- `trunk\omnimud.xml` = catálogo de ejemplo v1.0.0.6 (2008-05-27): killprogramprocess → unzip `update.zip` → exec `omnimud.exe`. (Desajuste: el updater guarda la descarga como `Update.<ext>`; en Windows no importa.)
- `CloseOmnimuds.exe`: mata todos los procesos `omnimud` (para instalador/desinstalador).
- Sin firma ni hash de la actualización; HTTP plano.

### 10.4 Informes de error y sugerencias
- Excepción no controlada (Program.cs:54-130): aviso especial si `OutOfMemoryException`; pregunta si enviar informe y si añadir comentario/e-mail (`InputBox`). **[BUG]** `p.Guid` se sobrescribe con un GUID nuevo en cada envío y el e-mail tecleado se pisa en la línea 72. Envía por web service SOAP `OmnimudReporting.Reporting.SendError(version, fecha, guid, SO, nombre, email, comentario, tipoExcepción, mensaje, stackTrace, source, 1)` en `http://www.omnimud.org/Reporting/Reporting.asmx` (app.config); devuelve int (id del error) o string (error). En `ThreadException` ofrece continuar.
- Manual: menú Ayuda → Enviar error / sugerencia (`FrmReports`, `SendError` / `SendSuggestion`).
- `CPersonalInfo` {Name, Email, Guid} en `PersonalInfo`; al primer arranque se ofrece rellenarla (`DontAskMeAgain`).
- `COsInfo`: nombre/edición/service pack/bits del SO para el informe.
- `CMailer.SendReport`: vía antigua por GET, sin uso.

---

## 11. OTRAS FUNCIONALIDADES ENCONTRADAS

- **Comandos internos de consola** (FrmCliente.cs:2392-2551): `cls`, `triggers`, `calias`, `paths…`, `callate` / `hablar` (modo silencioso; F8), `recolecta` (GC), `pruebaom` (debug: `OmGet("mirar")`), más `±triggers`, `±trigger`, `calias`, `uncalias`, `_path`.
- **Modo silencioso + `all_speak:`**: en silencio solo se verbaliza lo que sigue al marcador `all_speak:`; el marcador se elimina siempre del texto mostrado y del log (FrmCliente.cs:1658, 1783, 1816-1827). Pensado para que un trigger haga `OmDisplay("all_speak:…")` y hable aunque el cliente esté callado.
- **Lectores de pantalla** (CSintetizer.cs): JAWS (`jfwapi.dll`), Window-Eyes (COM `GWSpeak`), NVDA (`nvdaControllerClient32.dll`), autodetección (NVDA → proceso `JFW` → `wineyes.exe`, este último con nombre de proceso incorrecto). Se verbaliza todo el texto recibido con la ventana activa.
- **Ventana de mensajes**: máx. 1000; lectura con Ctrl+dígitos (ventana de 400 ms para números de varias cifras, FrmCliente.cs:985-1000); barra de estado con mensajes y tiempo conectado.
- **Modo desconexión** (FrmCliente.cs:253-265): si falla la conexión, se puede abrir la sesión offline para editar alias/triggers/paths/opciones.
- **URLs y e-mails** en recibidos/mensajes (`CConsts.RegUrl`, `RegMail`): Enter o doble clic abre; suena `url.mp3`.
- **Eco telnet** (WILL/WONT ECHO → caja de envío en modo contraseña), solo si `EnableTelnetNegotiation`; si está desactivada, las secuencias IAC se eliminan sin responder.
- **ANSI**: SGR 0-9, 22-29, 30-37/39, 40-47/49 (8 colores básicos; sin 256/truecolor; itálica/subrayado/parpadeo/tachado "off" comentados). Reset = negro sobre blanco.
- **Parpadeo de ventana** al recibir texto inactiva (`CWindowFlicker`). **Buscar** (Ctrl+B / Ctrl+S).
- **Personajes predeterminados** por MUD y menú de conexión rápida; importación "desde otro personaje" para alias/triggers/paths.
- **Huevo de pascua** en FrmPrincipal.cs:204-220 (solo si existe `dedicatoria.mp3`).
- **Ayuda CHM** (`chm\omnimud.chm`, F1; "Manual de usuario. OMnimud 1.1"). Temas: 1 Introducción · 1.1 Novedades · 2 Instalación · 2.1 Requisitos · 2.2 Instalación de Omnimud · 2.3 Scripts para JAWS · 3 Usando la aplicación · 3.1 Actualizando · 3.2 Pantalla principal · 3.3 Muds (agregar/editar/borrar/exportar/importar) · 3.4 Conectando con un mud · 3.5 Personajes · 3.6 Conectando con un personaje · 3.7 La ventana de juego · 3.8 Caracteres de concatenación y repetición · 3.9 Alias · 3.10 Triggers (3.10.1 simples, 3.10.2 regex, 3.10.3 dinámicos, 3.10.4 asociados a comandos) · 3.11 Paths · 3.12 Modo movimiento con teclado numérico · 3.13 Opciones (3.13.1 generales, 3.13.2 logs, 3.13.3 sonido, 3.13.4 conexión, 3.13.5 apariencia, 3.13.6 caracteres especiales) · 3.14 Directivas de procesamiento personalizadas · 4 Enviando errores y sugerencias · 5 Consideraciones finales · 6 Agradecimientos.
- **Instalador** (`instalador\instalador.sf6`, Setup Factory 6, "OmniMud 1.1", destino `%ProgramFiles%\OmniMud`): instala `omnimud.exe`(+pdb), `omnimud.chm`, `OmnimudCommonRules.dll`, `irrKlang.NET2.0.dll`, `ikpMP3.dll`, `GWSpeak.dll`, `jfwapi.dll`, `AutoUpdater.exe`, `CloseOmnimuds.exe`, `vcredist_x86.exe` (lo ejecuta), `Sounds\url.mp3`, `ProcessRules\ProcessRules.dll`(+pdb). Comprueba .NET 2.0 (`SOFTWARE\Microsoft\.NETFramework\v2.0.50727`), pregunta por `CheckUpdates=True`, y al desinstalar ofrece borrar `Software\KastweySoftware\omnimud`. No incluye `nvdaControllerClient32.dll`, `GWSPEAKLib.dll`, `ICSharpCode.SharpZipLib.dll` ni los otros sonidos (instalador desactualizado respecto al código). No crea MUDs ni datos.
- **`trunk\dlls`**: GWSpeak.dll, GWSPEAKLib.dll, ICSharpCode.SharpZipLib.dll, ikpMP3.dll, irrKlang.NET2.0.dll, jfwapi.dll, nvdaControllerClient32.dll, System.Data.SQLite.DLL, vcredist_x86.exe.

---

## 12. Resumen de sorpresas para el port

1. `TriggerRegExpType` (RegExp/ReplaceRegExp) no se usa en ejecución; no existe gag ni reescritura del texto mostrado.
2. Matching por bloque TCP, no por línea; en comodines `^`/`$` anclan al bloque (los `\n` se vuelven espacios).
3. Regex con `^`/`$` prácticamente no disparan (doble comprobación literal); `CaseSensitive` invertido en el `Regex.Replace`.
4. Las incógnitas se acumulan entre triggers del mismo bloque; en triggers insensibles llegan en minúsculas; `%Nd` documentado pero no implementado.
5. Los scripts solo reciben incógnitas: ni el texto que disparó ni los grupos de regex.
6. `OmGet`: timeout 3 s, devuelve `""`, se traga los bloques, estado global único, envía sin alias.
7. Todos los triggers que casan se ejecutan, en orden de creación; C#/VB en ThreadPool con bloqueo de reentrada por trigger; excepción en script = caída de la app.
8. Firma RSA-1024/SHA-1 verificada solo al importar ficheros de triggers.
9. Alias: primera palabra exacta, resto concatenado; sin parámetros ni enabled.
10. Paths: envío en ráfaga sin retardo; sin diccionario ni teclas por defecto; `-r` fallido lanza NRE.
11. MSP: `C=` solo `true/false`, `T=` como subcarpeta, URL solo por `U=`, sin comodines, solo a inicio de línea; prioridades de triggers mezcladas con música.
12. Opciones: herencia por objeto completo sin flag; al guardar se copia todo.
13. Persistencia: todo en HKCU con separador `æ`; `DefaultCharacter` guarda el nombre; ids de 30 letras.
14. Cifrado de contraseñas trivial y reversible (mismo en Registro y XML).
15. Sin MUDs predefinidos, sin argumentos de línea de comandos, sin instancia única, pipes/servidor sin usar, SQLite abandonado.

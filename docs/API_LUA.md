# OMnimud v2 — Referencia de la API Lua para triggers y reglas de mensajes

Los triggers de tipo **Script Lua** (y los triggers de comando `@nombre`) ejecutan código [Lua 5.2](https://www.lua.org/manual/5.2/) dentro de un entorno seguro. Todo lo que el script puede hacer sobre la sesión está en la tabla global `om`.

Los conjuntos de **reglas de mensajes** de tipo Script Lua usan el mismo entorno con un contrato propio: ver [Reglas de mensajes](#10-reglas-de-mensajes).

Si vienes del cliente original (C#/VB.NET): al final hay una [tabla de equivalencias](#equivalencias-con-el-cliente-original) y los [ejemplos de la ayuda original](#ejemplos-de-la-ayuda-original-portados) reescritos.

## 1. Lo esencial

```lua
-- Trigger: ^(\w+) te dice: '(.+)'$   (expresión regular)
om.send("decir Hola, " .. om.captures[1])
om.playsound("aviso.wav")
```

- Los errores de un script **nunca cierran el cliente**: se muestran con el nombre del trigger y el número de línea (`line 3: attempt to index a nil value`).
- El mismo trigger no se solapa consigo mismo; triggers distintos sí corren a la vez. Cada ejecución tiene su propio estado Lua: las variables globales de Lua **no** sobreviven de una ejecución a otra. Para recordar cosas usa `om.setvar` / `om.getvar`.
- `print(...)` equivale a `om.echo(...)`.

## 2. Datos disponibles

| Nombre | Contenido |
|---|---|
| `om.line` | La línea que disparó el trigger (sin colores). |
| `om.block` | El bloque completo de líneas recibido junto con ella (para triggers multilínea). En triggers de línea coincide con `om.line`. |
| `om.lines` | Las líneas de `om.block` en una tabla, **empezando en 1** (`#om.lines` es cuántas hay). En triggers de línea tiene una sola. |
| `om.captures` | Tabla con las capturas del patrón, **empezando en 1**: comodines `%s`, `%d`, `%w`… o grupos de la expresión regular. |
| `om.args` | Alias de `om.captures`. En triggers de comando son las palabras escritas tras el comando. |
| `om.command` | En triggers de comando (`@nombre`), la línea completa que tecleó el usuario. En los demás, `nil`. |
| `om.mud.name` | Nombre del MUD (cadena vacía si no se conoce). |
| `om.character.name` | Nombre del personaje (cadena vacía si no hay). |

```lua
-- Trigger de comando con patrón "@matar": el usuario escribe  matar orco rapido
-- (la arroba solo va en el patrón; al teclear el comando no se pone)
om.echo(om.command)   --> matar orco rapido
om.echo(om.args[1])   --> orco
om.echo(#om.args)     --> 2
om.echo(om.character.name .. " juega en " .. om.mud.name)
```

## 3. Enviar al MUD

### `om.send(comando)`
Envía el comando como si lo hubieras tecleado: pasa por alias, paths (`_nombre`), comandos internos, triggers `@`, concatenación (`;`) y repetición (`3#`). Una cadena vacía se ignora.
```lua
om.send("coger todo de " .. om.captures[1])
om.send("n;n;e")        -- si la concatenación está activada
```

### `om.sendraw(comando)`
Envía directamente al MUD, sin alias ni ningún otro procesado.
```lua
om.sendraw("mirar")
```

### `om.get(comando [, opciones])` → cadena o `nil`
Envía `comando` directamente al MUD y devuelve su respuesta como texto. La respuesta capturada **no se muestra, no se lee, no se registra y no dispara triggers**. El script espera sin bloquear el cliente. Si nadie responde a tiempo devuelve `nil`.

Opciones (tabla, todas opcionales):

| Opción | Por defecto | Significado |
|---|---|---|
| `pattern` | — | Expresión regular .NET: solo se capturan las líneas que la cumplen; las demás se muestran normalmente. |
| `timeout` | `3` | Segundos de espera (entre 0,1 y 60). |
| `colors` | `false` | `true` para conservar las secuencias ANSI en la respuesta. |

Como atajo, si el segundo argumento es una cadena se toma como `pattern`.
```lua
local r = om.get("pv")
if r == nil then
  om.echo("El MUD no ha contestado")
else
  local actual, maximo = string.match(r, "(%d+)/(%d+)")
  om.echo("Te quedan " .. actual .. " de " .. maximo)
end

local oro = om.get("dinero", { pattern = "^Tienes \\d+ monedas", timeout = 5 })
local conColor = om.get("inventario", { colors = true })
```
Varias peticiones `om.get` simultáneas se atienden en cola: las respuestas no se mezclan.

## 4. Mostrar, hablar y avisar

### `om.display(texto)`
Inyecta el texto como si viniera del MUD: colores ANSI, triggers, reglas de mensajes, registro y voz.
```lua
om.display(om.ANSI_RED .. "¡Peligro!" .. om.ANSI_RESET)
```

### `om.echo(texto)`
Solo lo pinta en la ventana. No dispara triggers ni se guarda en el registro.
```lua
om.echo("[script] objetivo fijado")
```

### `om.say(texto [, interrumpir])` y `om.notify(texto)`
Lo dice por el lector de pantalla, sin pintarlo. Con `interrumpir = true` corta lo que se estuviera leyendo. `om.notify(t)` es lo mismo que `om.say(t, false)`.
```lua
om.say("Vida baja", true)
om.notify("Ha llegado correo")
```

### `om.message(texto)`
Añade el texto al cuadro de Mensajes (lo muestra si estaba oculto).
```lua
om.message(om.captures[1] .. " te ha dicho: " .. om.captures[2])
```

### `om.status(texto)`
Escribe en la barra de estado.
```lua
om.status("Objetivo: " .. om.args[1])
```

### `om.log(texto)`
Escribe solo en el fichero de registro de la sesión.
```lua
om.log("Trigger de botín disparado con " .. om.line)
```

### `om.gag()`
Oculta la línea que disparó el trigger (no se pinta, no se lee, no se registra). Solo tiene efecto en triggers de línea.
```lua
-- Trigger: ^Un mosquito zumba\.$
om.gag()
```

## 5. Sonido

### `om.playsound(nombre [, opciones])`
Busca el fichero tal cual, luego en la carpeta de sonidos del MUD y luego en la carpeta `sounds` de la aplicación. Opciones: `loop` (1 = una vez, N = N veces, -1 = sin fin; por defecto 1), `volume` (0-100, por defecto 100), `priority` (0-100, por defecto 50). Como atajo, un número como segundo argumento es `loop`.
```lua
om.playsound("ding.wav")
om.playsound("lluvia.mp3", { loop = -1, volume = 40 })
om.playsound("campana.wav", 3)          -- tres veces
```

### `om.stopsound(nombre)` → booleano
Detiene ese sonido. Devuelve `true` si estaba sonando.
```lua
if om.stopsound("lluvia.mp3") then om.echo("Deja de llover") end
```

## 6. Variables de sesión

Compartidas por todos los triggers de la ventana; duran lo que dure la sesión. Los valores se guardan como texto.

| Función | Hace |
|---|---|
| `om.setvar(nombre, valor)` | Crea o sobrescribe. `valor` puede ser cadena, número o booleano. |
| `om.getvar(nombre)` | Devuelve el valor o `nil`. |
| `om.removevar(nombre)` | Borra; devuelve `true` si existía. |
| `om.isset(nombre)` | `true` si existe. |

```lua
om.setvar("muertes", (tonumber(om.getvar("muertes")) or 0) + 1)
om.echo("Llevas " .. om.getvar("muertes") .. " muertes")
if om.isset("objetivo") then om.removevar("objetivo") end
```

## 7. Esperas, cuentas atrás y temporizadores

Las esperas son **cooperativas**: mientras el script espera no ocupa ningún hilo ni bloquea el cliente, y ese tiempo no cuenta para el límite de tiempo de ejecución (sí para el tope total de 10 minutos).

### `om.sleep(segundos)`
Pausa el script. Máximo 10 segundos por llamada (un valor mayor se recorta a 10).
```lua
om.send("beber pocion")
om.sleep(1.5)
om.send("mirar")
```

### `om.countdown(ticks [, opciones])`
Cuenta atrás con sonidos (el `StartCount` del original). Arranca el sonido `during` en bucle, espera `ticks × unidad`, lo detiene y reproduce `finish`. El script continúa cuando termina la cuenta. Opciones:

| Opción | Por defecto | Significado |
|---|---|---|
| `unit` | `"s"` | `"s"` segundos, `"d"` décimas, `"c"` centésimas, `"m"` milésimas; admite multiplicador: `"2s"` = 2 segundos por tick. |
| `during` | — | Sonido en bucle mientras dura la cuenta. |
| `finish` | — | Sonido al terminar. |

```lua
om.countdown(8, { finish = "fin.mp3" })                                   -- 8 segundos
om.countdown(3, { unit = "2s", during = "tictac.mp3", finish = "fin.mp3" }) -- 6 segundos
```
Si la sesión se cierra o el script se cancela a mitad, el sonido `during` se detiene y `finish` no suena.

### `om.timer(nombre, segundos, funcion [, repetir])`
Programa `funcion` para dentro de `segundos` (mínimo 0,1) y el script sigue sin esperar. Con `repetir = true` se repite cada `segundos` hasta cancelarlo. Reglas:
- Un temporizador con el mismo `nombre` **sustituye** al anterior.
- Máximo **10 temporizadores vivos** por sesión; el undécimo da error.
- La función se ejecuta con los mismos límites que cualquier script y puede usar toda la API (incluidos `om.sleep` y `om.get`) y las variables locales del script que la creó.
- Si una repetición falla, el temporizador se cancela. Al cerrar la sesión se cancelan todos.
- Un temporizador de un solo uso puede volver a programarse a sí mismo desde su función.

```lua
om.timer("pocion", 30, function()
  om.say("Ya puedes beber otra poción")
end)

local n = 0
om.timer("latido", 60, function()
  n = n + 1
  om.sendraw("guardar")
  om.log("Guardado automático número " .. n)
end, true)
```

### `om.canceltimer(nombre)` → booleano
Cancela un temporizador pendiente. Devuelve `true` si existía. No interrumpe una función de temporizador que ya esté ejecutándose.
```lua
om.canceltimer("latido")
```

## 8. Utilidades

### `om.lastactivity()` → número
Segundos desde el último comando enviado al MUD (incluye los enviados por triggers).
```lua
if om.lastactivity() > 300 then om.sendraw("mirar") end   -- anti-inactividad
```

### `om.removecolors(texto)` → cadena
Quita las secuencias ANSI.
```lua
local limpio = om.removecolors(om.get("pv", { colors = true }) or "")
```

### `om.match(texto, patron)` → tabla o `nil`
Expresión regular de **.NET** (más potente que los patrones de Lua), con un tiempo máximo de evaluación de 100 ms. Si casa devuelve una tabla: `[0]` la coincidencia completa, `[1]`, `[2]`… los grupos, y los grupos con nombre por su nombre. Si no casa, `nil`. Para ignorar mayúsculas usa `(?i)` al principio del patrón.
```lua
local m = om.match(om.line, [[^(?<quien>\w+) te da (\d+) monedas]])
if m then om.echo(m.quien .. " -> " .. m[2]) end
```
Usa cadenas largas `[[...]]` para no tener que duplicar las barras invertidas.

### Constantes ANSI
Secuencias completas `ESC[<n>m`, para `om.display` / `om.echo` o para comparar con lo que devuelve `om.get(..., {colors = true})`.

| Grupo | Constantes |
|---|---|
| Atributos | `om.ANSI_RESET` (0), `ANSI_BOLD` (1), `ANSI_DIM` (2), `ANSI_ITALIC` (3), `ANSI_UNDERLINE` (4), `ANSI_BLINK` (5), `ANSI_INVERSE` (7), `ANSI_HIDDEN` (8), `ANSI_STRIKE` (9) |
| Apagar atributo | `ANSI_BOLD_OFF` y `ANSI_DIM_OFF` (22), `ANSI_ITALIC_OFF` (23), `ANSI_UNDERLINE_OFF` (24), `ANSI_BLINK_OFF` (25), `ANSI_INVERSE_OFF` (27), `ANSI_HIDDEN_OFF` (28), `ANSI_STRIKE_OFF` (29) |
| Color de texto | `ANSI_BLACK` (30), `ANSI_RED` (31), `ANSI_GREEN` (32), `ANSI_YELLOW` (33), `ANSI_BLUE` (34), `ANSI_MAGENTA` = `ANSI_PURPLE` (35), `ANSI_CYAN` (36), `ANSI_WHITE` (37), `ANSI_DEFAULT` (39) |
| Color de fondo | `ANSI_BG_BLACK` (40) … `ANSI_BG_WHITE` (47) con los mismos nombres, `ANSI_BG_DEFAULT` (49) |

```lua
om.echo(om.ANSI_BOLD .. om.ANSI_YELLOW .. om.ANSI_BG_BLUE .. "AVISO" .. om.ANSI_RESET .. " texto normal")
```

## 9. El entorno seguro

**Disponible:** el lenguaje completo, `string`, `table`, `math`, `bit32`, `pcall` / `xpcall` / `error` / `assert`, metatablas (`setmetatable`, `getmetatable`, `rawget`, `rawset`…), `pairs`, `ipairs`, `select`, `tonumber`, `tostring`, `type`, `unpack`, `os.time`, `os.date`, `os.clock`, `os.difftime`, y las extensiones de MoonSharp `string.startsWith`, `string.endsWith`, `string.contains`.

**No disponible:** `io`, `os.execute` y el resto de `os`, `debug`, `load`, `loadstring`, `loadfile`, `dofile`, `require`, `package`, `coroutine`, `string.dump`, ni ningún acceso a .NET, a ficheros, a la red o a la ventana del cliente. `collectgarbage` existe pero no hace nada.

**Límites** (por ejecución, y de nuevo para cada disparo de un temporizador):

| Límite | Valor por defecto | Qué pasa al superarlo |
|---|---|---|
| Instrucciones Lua | 100 000 | El script se detiene con un error. |
| Tiempo ejecutando código (no cuenta el tiempo en espera) | 5 s | Ídem. |
| Tiempo total, esperas incluidas | 10 min | Ídem. |
| `om.sleep` | 10 s por llamada | Se recorta. |
| Longitud de una cadena construida con `string.rep`, `table.concat`, `string.format` o `string.gsub` | 1 000 000 caracteres | Error de Lua (capturable con `pcall`). |
| Memoria reservada por el script | ≈ 100 MB | El script se detiene. |
| Temporizadores vivos | 10 por sesión | `om.timer` da error. |

Un bucle infinito (`while true do end`), una recursión sin fin o un bucle escondido dentro de una función de `table.sort` o `string.gsub` se cortan siempre; `pcall` no puede impedirlo. Los errores de argumentos de la API (`om.send: argument #1 must be a string (got nil)`) sí son errores normales de Lua y se pueden capturar con `pcall`.

Notas:
- Los patrones de Lua de MoonSharp dan el error `pattern too complex` con textos largos y cuantificadores como `.-` o `.*`. Para esos casos usa `om.match`.
- `om.sleep`, `om.get` y `om.countdown` no pueden llamarse desde dentro de una función que ejecuta código nativo (el comparador de `table.sort`, la función de reemplazo de `string.gsub`, `__tostring`…); dan un error de Lua.

## 10. Reglas de mensajes

Las **reglas de mensajes** deciden qué texto del MUD se copia al cuadro de Mensajes. Un conjunto de reglas (menú Herramientas del lanzador, «Reglas de mensajes», Ctrl+R) es de uno de dos tipos:

- **Patrones**: una lista de expresiones regulares que se prueban línea a línea.
- **Script Lua**: un script que recibe el **bloque completo** de texto que acaba de llegar, como hacían las DLL de «directivas de procesamiento» del cliente original. Es lo que hace falta cuando un mensaje ocupa varias líneas o depende de las líneas de alrededor. Un conjunto con script **no evalúa sus patrones**.

Los cuatro conjuntos integrados (Balzhur, Callandor, Simauria y Cyberlife) son scripts Lua, ports fieles de las reglas originales. No se pueden editar, pero sí **duplicar**: el duplicado es tuyo y lo adaptas a tu MUD.

### Contrato

El script se ejecuta **una vez por cada bloque recibido** y es una función pura de ese bloque:

| Entrada | Contenido |
|---|---|
| `om.lines` | Tabla con las líneas del bloque, **empezando en 1**, sin colores ANSI y sin el marcador `all_speak:`. Solo líneas completas: los prompts y las líneas MSP no están. |
| `om.block` | Las mismas líneas unidas con `\n`. |
| `om.mud.name`, `om.character.name` | Como en los triggers. |

| Salida | |
|---|---|
| `om.message(texto)` | Cero o más veces. Cada llamada es un mensaje; el texto puede tener varias líneas (`\n`). Los saltos de línea del final se recortan. |

Todo lo demás **se ignora** en este contexto: `om.send`, `om.sendraw`, `om.display`, `om.echo`, `om.say`, `om.notify`, `om.status`, `om.log`, `om.gag`, `om.playsound`... no hacen nada, y las variables (`om.setvar`) no sobreviven de un bloque al siguiente. **No se puede esperar**: `om.sleep`, `om.get`, `om.countdown` y `om.timer` dan un error de Lua (capturable con `pcall`).

El texto inyectado con `om.display` y las líneas del propio cliente no pasan por el script. Si las líneas de las que sale el mensaje ya se leyeron en voz alta, el mensaje no se vuelve a leer; y si el MUD manda el mismo mensaje por GMCP (`Comm.Channel.Text`), no se duplica.

### Límites y errores

Para que la recepción no se frene nunca, los límites son mucho más estrictos que los de un trigger: **100 ms** de ejecución y **50 000 instrucciones** por bloque. Si el script falla o se pasa de un límite, se muestra **una sola línea** de aviso (no una por bloque) y la partida sigue; ese bloque no produce mensajes. Si se pasa de los límites en tres bloques seguidos, queda desactivado hasta que se recarguen las reglas. Los errores de sintaxis se avisan al cargar.

Consejos para ir sobrado: descarta pronto las líneas que no interesan con `string.find(linea, "texto", 1, true)` (búsqueda literal, muy barata), usa `om.match` en lugar de los patrones de Lua y junta varias alternativas en una sola expresión regular.

### Ejemplo: mensajes de varias líneas

Un mensaje empieza en «Fulano dice '...» y sigue hasta la línea que acaba en comilla:

```lua
local lines = om.lines
local i = 1
while i <= #lines do
  if om.match(lines[i], [[^\w+ (dice|susurra|grita) ']]) then
    local last = i
    while last < #lines and string.sub(lines[last], -1) ~= "'" do
      last = last + 1
    end
    om.message(table.concat(lines, "\n", i, last))
    i = last + 1
  else
    i = i + 1
  end
end
```

### Ejemplo: canales, todos juntos en un mensaje

```lua
local found = {}
for _, line in ipairs(om.lines) do
  if om.match(line, [[^\[(Chat|Clan|Novatos)\] ]]) then
    found[#found + 1] = line
  end
end
if #found > 0 then om.message(table.concat(found, "\n")) end
```

### Probar

En la ventana de reglas, escribe en «Texto de prueba» un bloque de varias líneas tal como lo manda el MUD y pulsa **Probar**: el resultado lista los mensajes numerados, dice «Ningún mensaje» o muestra el error con su línea (y deja el cursor en ella). Al guardar, el script se valida y un error de sintaxis lleva el cursor a su línea. En el cuadro del script, Tab escribe un tabulador; **Ctrl+Tab** sale del cuadro.

## Equivalencias con el cliente original

| Original (C#) | Lua |
|---|---|
| `OmSend(cmd)` | `om.send(cmd)` |
| — | `om.sendraw(cmd)` |
| `OmGet(cmd)` · `OmGet(cmd, true)` · `OmGet(cmd, "patrón")` | `om.get(cmd)` · `om.get(cmd, {colors=true})` · `om.get(cmd, {pattern="patrón"})`. Devuelve `nil` al vencer (el original devolvía `""`). |
| `OmDisplay(texto)` | `om.display(texto)` |
| `SayText(texto)` | `om.say(texto)` / `om.notify(texto)` |
| `AddMessage(texto)` | `om.message(texto)` |
| `PlaySound(n)` · `PlaySound(n, rep)` | `om.playsound(n)` · `om.playsound(n, rep)` o `{loop=rep}` |
| `StopSound(n)` | `om.stopsound(n)` |
| `OmSetVar` · `OmQueryVar` · `OmRemoveVar` · `OmVarIsSet` | `om.setvar` · `om.getvar` · `om.removevar` · `om.isset` |
| `StartCount(secs, fin)` | `om.countdown(secs, {finish=fin})` |
| `StartCount(ticks, tipo, fin)` | `om.countdown(ticks, {unit=tipo, finish=fin})` |
| `StartCount(ticks, tipo, durante, fin)` | `om.countdown(ticks, {unit=tipo, during=durante, finish=fin})` |
| `GetLastActivity()` (fecha) | `om.lastactivity()` (segundos transcurridos) |
| `RemoveColours(t)` | `om.removecolors(t)` |
| `ApearanceRed`, `ApearanceBold`… | `om.ANSI_RED`, `om.ANSI_BOLD`… (y ahora también fondos) |
| `Thread.Sleep(ms)` | `om.sleep(segundos)` |
| `args[0]`, `args[1]`… | `om.args[1]`, `om.args[2]`… (**empiezan en 1**) |
| `FullCommand` | `om.command` |
| DLL de «directiva de procesamiento» (`IRule.ProcessMessage(bloque)`) | Conjunto de reglas de mensajes de tipo Script Lua: `om.lines` / `om.block` y `om.message(texto)`. Ver la sección 10. |
| `Cln.*` (acceso total a la ventana) | No existe. Solo `om.mud.name` y `om.character.name`. |
| — | Nuevo: `om.line`, `om.block`, `om.lines`, grupos de regex en `om.captures`, `om.echo`, `om.gag`, `om.status`, `om.log`, `om.match`, `om.timer`, `om.canceltimer`. |

## Ejemplos de la ayuda original, portados

Estos scripts son tests de aceptación del motor (`LuaHelpExamplesAcceptanceTests`).

**Responder a quien te habla** — desencadenante `%s te dice: '%s'`
```lua
om.send("decir Anda, " .. om.captures[1] .. " me ha dicho " .. om.captures[2] .. ".")
```

**Coger el botín cuando alguien muere** — desencadenante `^%s ha muerto.`
```lua
om.send("coger todo de " .. om.args[1])
```

**Estado "dormido" con variables** — tres triggers
```lua
-- ^Te duermes.$
om.setvar("dormido", "si")
```
```lua
-- ^Te despiertas.$
om.removevar("dormido")
```
```lua
-- ^%w llega
if om.isset("dormido") and om.getvar("dormido") == "si" then
  om.send("despertar")
  om.send("¡Hola " .. om.args[1] .. "!")
end
```

**Copiar las canciones a la ventana de mensajes** — desencadenante `^%w canta: '%s'$`
```lua
om.message(om.args[1] .. " canta: '" .. om.args[2] .. "'")
```

**Comando con patrón `@AvisaCura` (se teclea `AvisaCura`): avisar cuando estés curado del todo**
```lua
while true do
  om.sleep(2)
  local respuesta = om.get("pv")
  if respuesta then
    local actual, maximo = string.match(respuesta, "(%d+)/(%d+)")
    if actual and tonumber(actual) >= tonumber(maximo) then
      om.playsound("curado.wav")
      break
    end
  end
end
```
Recuerda que un script tiene un tope total de 10 minutos; para vigilar durante más tiempo usa un temporizador con repetición:
```lua
om.timer("avisacura", 2, function()
  local respuesta = om.get("pv")
  local actual, maximo = string.match(respuesta or "", "(%d+)/(%d+)")
  if actual and tonumber(actual) >= tonumber(maximo) then
    om.playsound("curado.wav")
    om.canceltimer("avisacura")
  end
end, true)
```

**Contadores**
```lua
om.countdown(150, { unit = "m", during = "durante.mp3", finish = "fin.mp3" })  -- 150 milésimas
om.countdown(3, { unit = "2s", during = "durante.mp3", finish = "fin.mp3" })   -- 3 ticks de 2 segundos
om.countdown(8, { finish = "fin.mp3" })                                         -- 8 segundos
```

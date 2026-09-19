# OMnimud v2 — Arquitectura de sesión y reparto de trabajo

Documento de trabajo para implementar las fases 1 a 5 del [plan](02_PLAN_PARIDAD_V2.md). Lectura obligatoria junto con [01_FUNCIONALIDAD_ORIGINAL.md](01_FUNCIONALIDAD_ORIGINAL.md) y los anexos de `auditoria/`.

## 1. Principios

- **Portable al 100 %**: nada de Registro de Windows. Todo en SQLite y ficheros dentro de `data\` junto al ejecutable (`Omnimud.Core.Storage.AppPaths`). Se publicará como ejecutable autocontenido.
- **Core no conoce ni la interfaz ni la base de datos**. Habla con el exterior por interfaces: `ISessionStore`, `IOptionsService`, `ISessionSound`, `IScriptHost`, `IConnection`.
- **Una sesión por conexión** (`IMudSession`), dueña de sus motores. Nada de singletons con estado compartidos entre ventanas.
- **La ventana es una vista**: se suscribe a eventos y reenvía lo que teclea el usuario.
- **Añadir sí, recortar no**: no se elimina ninguna capacidad pública existente de la v2.
- **Tests de todo lo importante**, sin red real salvo el servidor TCP local de los tests de conexión, y sin `Thread.Sleep` largos: inyectar `TimeProvider` donde haya tiempos.

## 2. Contratos ya escritos (no cambiar su forma sin avisar)

| Fichero | Contenido |
|---|---|
| `Core/Session/IMudSession.cs` | Superficie pública de la sesión: ciclo de vida, entrada, eventos |
| `Core/Session/SessionContracts.cs` | `SessionProfile`, `SessionState`, `SessionLine`, `SessionMessage`, `AnnouncePriority`, `SessionWindow`, `ISessionStore`, `MessageRule` |
| `Core/Scripting/IScriptHost.cs` | Lo que un script Lua puede hacer sobre la sesión |
| `Core/Scripting/IScriptEngine.cs` | Motor: sobrecarga con `IScriptHost`, `Validate` |
| `Core/Scripting/ScriptContext.cs` | Línea, bloque, capturas, comando completo, nombres |
| `Core/Options/OmnimudOptions.cs` · `IOptionsService.cs` | Todas las opciones tipadas y su servicio con herencia por bloque |
| `Core/Sound/ISessionSound.cs` | Sonido por sesión: MSP, sonidos de trigger, efectos del cliente |
| `Core/Triggers/TriggerDefinition.cs` | Añadidos `Multiline`, `GagLine`, `IsCommandTrigger`, acción `SendCommandAndPlaySound` |
| `Core/Storage/AppPaths.cs` | Rutas portables |

## 3. Recepción (dentro de `MudSession`)

```
bytes
 → TelnetNegotiator CON ESTADO por sesión: tolera IAC/SB/SE partidos entre lecturas; IAC IAC;
   responde DONT/WONT a toda opción no soportada; soporta ECHO (modo contraseña) y GMCP.
   Si la opción TelnetNegotiation está apagada, solo se eliminan las secuencias.
 → StreamDecoder: System.Text.Decoder con estado, con la codificación del MUD
 → retrocesos (0x08 borra el carácter anterior) y eliminación de \r
 → LineBuffer: emite líneas completas; lo pendiente sin \n se vacía como Prompt tras
   Options.PromptFlushMilliseconds sin datos nuevos. Las líneas vacías se conservan.
 → por cada BLOQUE (líneas de una misma lectura):
     por cada línea:
       · MSP: si es !!SOUND( / !!MUSIC( → ISessionSound.HandleMspAsync y la línea desaparece
       · si hay un om.get pendiente que la reclama → se le entrega y fin
       · ANSI → segmentos + texto plano. El estado de color PERSISTE entre líneas.
         Las secuencias CSI que no son SGR se descartan.
       · marcador "all_speak:" → se retira del texto y se recuerda para el anuncio
       · triggers de línea (no Multiline), por prioridad descendente y luego orden de carga;
         se ejecutan TODOS los que casan; GagLine / om.gag ocultan la línea
       · si no está oculta: LineReceived, log, anuncio
       · reglas de mensajes (primera que casa) → mensaje
     después: triggers Multiline sobre el texto plano del bloque (líneas unidas con \n)
 → GMCP Comm.Channel.Text → mensaje (novedad de la v2, se conserva)
```

Anuncio (evento `Announce`), siempre sin ANSI:
- Solo con `IsWindowActive`. Texto del MUD → `Queue`, si `Options.AnnounceMudText`.
- `SilentMode`: solo se anuncia lo que va tras `all_speak:`. Fuera de ese modo el marcador solo se elimina.
- Mensajes nuevos: si `Options.AnnounceMessages` **y** el texto de esa línea no se anunció ya (evitar doble lectura).
- Si `!IsWindowActive` y `Options.FlashWindow` → `FlashRequested`.

Mensajes: máximo 1000; numeración 1 = más reciente al consultarlos; cada uno con `DateTime`.

Log (`SessionLogWriter`): modos None / PerDay / PerSession; UTF-8; añadir al final; sin ANSI; lo recibido y lo enviado; **nunca** lo tecleado en modo contraseña ni la contraseña del login script. Nombres `yyyy-MM-dd.log` y `yyyy-MM-dd HH-mm-ss.log` en `<LogDirectory o data\logs>\<mud>\<personaje>\`. Al cerrar: línea `Partida finalizada el dd/MM/yyyy a las HH:mm:ss.` (localizada).

## 4. Envío (`InputProcessor`, usado por `MudSession`)

`SubmitInputAsync(texto)`:
- Vacío → repite el último comando; si no hay, línea en blanco.
- En modo contraseña: se envía tal cual, sin historial, sin "último", sin log, sin alias.
- Historial: tamaño `Options.HistorySize`, sin duplicados consecutivos, solo en memoria.

Orden de evaluación de un comando (idéntico al original; A1 §4):
1. `_nombre` / `_nombre -r` → path (todas las direcciones completas, en orden; la vuelta usa las contrarias en orden inverso).
2. Alias: primera palabra, coincidencia exacta y sensible a mayúsculas; el resto de la línea se añade detrás. Solo alias activados. Una pasada, sin recursión.
3. Triggers de comando `@palabra` (respetan `CaseSensitive` y `-triggers`): capturas = resto de palabras; `FullCommand` = línea. **El comando no se envía.**
4. `-triggers` / `+triggers` (sesión) · 5. `-trigger N` / `+trigger N` (persisten) · 6. `uncalias N` · 7. `calias`, `calias N`, `calias N acción…`
8. Si se está grabando un path y el comando es una dirección conocida (abreviatura o nombre completo): anotar, `UiSoundRequested("pop")`, y seguir (se envía igualmente).
9. Exactos: `cls`, `triggers`, `paths`, `paths iniciar|ultima|ultima borrar|grabado|cancelar|detener`, `callate`, `hablar`.
10. Concatenación (si `UseConcatChar`; doble carácter = literal, **con el carácter configurado**) → cada parte vuelve a pasar por 1-9 con un límite de profundidad de 10 → repetición `N#cmd` (si `UseRepeatChar`; máx. 50) → envío con la codificación del MUD terminado en `\n` → log.

Las respuestas de los comandos internos son líneas `System`: se pintan, se registran y se anuncian, pero **no** disparan triggers. En modo `Offline` se ven igual; lo único que no funciona es enviar, que responde "Estás en modo de desconexión. No puedes enviar ningún comando.".

Todos los textos para el usuario van a `Core/Resources/Strings.resx` y `Strings.es.resx` (usar `tools/strings.ps1`). Se usan los literales del original corregidos ("nombre", no "name"; `uncalias`, no "cunalias"; "Solo hay", no "Sólo ay").

Corrige los defectos **[CORREGIR]** del documento 01: `paths detener` sin grabación, `_x -r` no invertible, `paths cancelar` que no limpiaba, etc. devuelven un mensaje, nunca una excepción.

## 5. Triggers

- `TriggerMatcher` existente (literal con anclas, regex con timeout, sscanf) se mantiene. Regex: los grupos de captura pasan a `Captures`; en modo línea `^` y `$` son los de la línea; en `Multiline` se usa `RegexOptions.Multiline`.
- Acción `SendCommand`: `%1…%N` sustituidos de mayor a menor índice (para que `%1` no pise `%10`), y el resultado entra por el procesador de entrada completo.
- `PlaySound` y `SendCommandAndPlaySound` → `ISessionSound.PlayTriggerSound`.
- `Script` → `IScriptEngine.ExecuteAsync(script, contexto, host)` en segundo plano; el mismo trigger no se solapa consigo mismo (si sigue ejecutándose, se descarta el nuevo disparo); triggers distintos sí corren en paralelo.
- `-triggers` también desactiva los de comando.

## 6. `om.get`

Cola FIFO por sesión. Con una petición activa: se envía el comando directo al MUD; las líneas siguientes se capturan (si hay patrón, solo las que lo cumplen) hasta un `Prompt` o hasta `PromptFlushMilliseconds` sin datos tras haber capturado algo; vence a `timeout` y devuelve `null`. Lo capturado no se pinta, registra, anuncia ni pasa por triggers.

## 7. Propiedad de ficheros (para trabajar en paralelo)

| Bloque | Es dueño de |
|---|---|
| **A · Sesión** | `Core/Session/*` (salvo los dos ficheros de contrato), `Core/Telnet`, `Core/Text` (salvo `MspExtractor`, `SoundCommand`), `Core/Triggers`, `Core/Commands`, `Core/Aliases`, `Core/Paths`, `Core/Messages`, `Core/Logging`, `Core/Resources/*` y sus tests |
| **A1 · Conexión** | `Core/Connection/*` y `tests/Omnimud.Core.Tests/Connection` |
| **B · Lua** | `Core/Scripting/*` (salvo `IScriptHost.cs`) y `tests/.../Scripting` |
| **C · Datos** | `Omnimud.Data/*`, `tests/Omnimud.Data.Tests/*`, `Core/Options/OptionsSerializer.cs`, `Core/Storage/*` y sus tests |
| **D · Sonido** | `Core/Sound/*` (salvo `ISessionSound.cs`), `Core/Text/MspExtractor.cs`, `Core/Text/SoundCommand.cs`, `UI/Services/Audio/*`, `UI/sounds/*`, la referencia a NAudio en `Omnimud.UI.csproj`, y `tests/.../Sound` |

Nadie más toca `UI/Services/ServiceConfigurator.cs`, `UI/Forms/*` ni `UI/Resources/*` en esta ronda.
Solo el bloque A edita `Core/Resources/Strings*`. Los demás bloques de Core lanzan excepciones con mensajes en inglés o devuelven códigos; no añaden cadenas localizadas.

## 8. Compilar y probar sin pisarse

Varios bloques compilan a la vez sobre el mismo árbol. Para no bloquear ficheros de `bin/obj`, **cada bloque usa su propia carpeta de artefactos**:

```
dotnet test tests/Omnimud.Core.Tests --artifacts-path <scratch>/<bloque> --filter "FullyQualifiedName~<TuEspacioDeNombres>"
```

Si la compilación falla por un fichero que **no es tuyo**, es trabajo en curso de otro bloque: no lo arregles; espera un minuto y reintenta. Si tras varios intentos sigue roto, termina tu parte, verifica tus ficheros lo mejor posible y dilo en el informe.

No hay git: no intentes usarlo. No borres ficheros ajenos.

## 9. Estilo

C# 14, `Nullable` activado, `sealed` por defecto, `async`/`await` con `ConfigureAwait(false)` en Core, comentarios solo donde expliquen un porqué, nombres en inglés en el código y español en la documentación. Tests con xUnit + FluentAssertions 7 + NSubstitute, nombres `Método_Escenario_Resultado`.

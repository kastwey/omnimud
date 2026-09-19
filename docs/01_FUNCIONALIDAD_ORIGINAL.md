# OMnimud original (trunk) — Especificación funcional verificada

Fecha: 2026-09-18. Fuente: lectura completa del código de `trunk\` (C#, WinForms, .NET Framework 2.0, x86, versión 1.1.0.1; la rama `branches\1.2` es idéntica salvo lo indicado en §16).

Este documento sustituye a `trunk\DOCUMENTACION_OMNIMUD.md`, que contenía errores (ver §17). Es el **contrato de paridad** para la v2: todo lo que aparece aquí debe existir en la v2, salvo lo marcado como *no portar* o *corregir*.

El detalle literal (mensajes exactos, citas `fichero:línea`, tablas de controles, formatos XML) está en los anexos de auditoría:

| Anexo | Contenido |
|---|---|
| [A1](auditoria/A1_trunk_ventana_principal.md) | Ventana de juego: controles, menús, teclado, comandos internos, pipelines, voz, conexión, telnet, ANSI, logs |
| [A2](auditoria/A2_trunk_motores.md) | Triggers y API de código, alias, paths, sonido, opciones, Registro, formato `.oxf`, cifrado, reglas de mensajes, arranque, actualizaciones |
| [A3](auditoria/A3_trunk_dialogos.md) | Todos los diálogos secundarios, validaciones, import/export, patrones de accesibilidad, comparación trunk/1.2/tag |
| [A4](auditoria/A4_v2_estado.md) | Estado real de la v2 |

Marcas usadas: **[CORREGIR]** = defecto del original que la v2 no debe reproducir. **[NO PORTAR]** = código muerto o ajeno al producto. **[DECISIÓN]** = pendiente de decidir (ver el plan).

---

## 1. Qué es y qué está vivo

Cliente de MUD por telnet pensado para usarse con lector de pantalla. Interfaz en español, sin localización (todos los textos están en el código).

Proyectos vivos: `cliente` (la aplicación), `OmnimudCommonRules` (interfaz `IRule`), `ProcessRules` (4 reglas de extracción de mensajes), `AutoUpdater`, `CloseOmnimuds` (cierra instancias antes de actualizar), `SignCode` (firma RSA de triggers con código), `instalador`, `chm` (manual).

**[NO PORTAR]** Código muerto: `UcClient` (todo comentado), `tiflomud.csproj`, `Form1`, `TextBoxBraille`, `FrmMap` (rejilla vacía que nunca se instancia), `FrmPrueba`, `CPipeClient/CPipeServer/CServer`, `CMailer`, `Crc32`, `CData` y `Data\SQLiteDataAccess` (intento abandonado de SQLite), `CStaticInfo`, `rdl\`, `trunk\Backup\`, comandos de depuración `recolecta` y `pruebaom`, y el huevo de pascua de `dedicatoria.mp3` en `FrmPrincipal`.

`todo.txt` (nunca implementado): accesos directos a un MUD/personaje, pestañas para varios MUD, mapeador.

---

## 2. Flujo de ventanas

Solo hay una ventana visible a la vez: cada ventana abre a su hija con `Show()` y se oculta; al cerrar la hija reaparece la madre. Son modales únicamente los diálogos de alta/edición de MUD y de alias, información personal, buscar e InputBox.

```
FrmPrincipal ─┬─ Conectar con… (menú de personajes predeterminados) ─► FrmCliente
              ├─ Muds ─► FrmMuds ─► FrmAddEditMud / Conectar ─► FrmCliente
              ├─ Personajes ─► FrmCharacters ─► FrmAddEditCharacter / Conectar ─► FrmCliente
              ├─ Opciones globales ─► FrmOptions
              └─ Información personal, Informes, Ayuda
FrmCliente ─── F5 Alias · F6 Triggers · F7 Paths · F9 Opciones · Teclas de movimiento · Direcciones
```

`FrmPrincipal`: botones `Conectar con…`, `Muds`, `Personajes`, `Salir`; menús Archivo, Herramientas (comprobar actualizaciones y su marca "al iniciar", Muds, Conectar con, Personajes, Opciones globales, Información personal) y Ayuda (F1 manual, web, enviar error o sugerencia). Al arrancar: comprueba actualizaciones si `CheckUpdates` es true (por defecto false) y pregunta por la información personal (con "no volver a preguntar").
**[CORREGIR]** En trunk el botón Salir dice una frase de prueba por voz y no cierra; el comportamiento correcto es el de la rama 1.2.

---

## 3. Ventana de juego (`FrmCliente`)

### 3.1 Controles, de arriba abajo

| Etiqueta visible | Control | Tipo | Notas |
|---|---|---|---|
| `&Mensajes:` (Alt+M) | `TxtMensajes` | RichTextBox solo lectura | Se oculta (con su etiqueta y su panel de estado) si el MUD no tiene regla de mensajes; reaparece si un trigger añade un mensaje |
| `&Recibido:` (Alt+R) | `TxtRecibido` | RichTextBox solo lectura | Texto del MUD con colores; detecta URL |
| `&Texto a enviar:` (Alt+T) | `TxtEnviar` | TextBox multilínea | En modo contraseña pasa a una línea con `*` |
| — | `BtnAccion` | Button | `&Cancelar` conectando · `&Desconectar` conectado · `&Cerrar` en modo desconexión. Su `AccessibleDescription` se actualiza con el texto |
| — | Barra de estado | 3 paneles | Info de conexión · tiempo conectado · estado de mensajes |

Orden de tabulación: botón → Texto a enviar → Recibido → Mensajes. Cada etiqueta precede a su cuadro en el orden de tabulación y el cuadro lleva `AccessibleName`/`AccessibleDescription` con el mismo texto. Todo deshabilitado hasta conectar. La etiqueta "Texto a enviar" se pone en cursiva con el modo movimiento activo.
**[CORREGIR]** `TxtMensajes` sin `AccessibleName`; errata "Mensaes:"; la cursiva no se aplica al cargar; la fuente elegida en Opciones no se aplica a la ventana ya abierta.

Barra de estado: (1) `Conectando a X...` → `Conectado con <pj> (<mud>)` / `Conectado a <mud>` / `Modo de desconexión con <pj> (<mud>)`; (2) `D día(s), H hora(s), M minuto(s), S segundo(s).` omitiendo ceros; (3) `Sin mensajes` / `Último mensaje recibido a las HH:mm:ss, del dd/MM/yyyy` / `Mensaje seleccionado: …` durante 2 s al moverse por Mensajes.
**[CORREGIR]** "5minutos" sin espacio; el contador deriva porque no usa el reloj.

### 3.2 Menús

| Menú | Ítem | Atajo | Efecto |
|---|---|---|---|
| **&Acciones** | &Salvar partida | F3 | Envía `SaveCommand` del MUD (deshabilitado si está vacío) |
| | &Abandonar la partida | F4 | Envía `QuitCommand` |
| **&Edición** | &Copiar / C&ortar / &Pegar / &Deshacer | Ctrl+C/X/V/Z | Sobre el cuadro activo; habilitados según contexto |
| | Seleccionar todo | Ctrl+E | |
| | &Buscar | Ctrl+B | Solo con foco en Recibido o Mensajes |
| | Buscar &siguiente | Ctrl+S | Repite la última búsqueda **de ese cuadro** |
| **&Herramientas** | Modo Mo&vimiento con teclado numérico (marca) | F2 | Se guarda por MUD o personaje |
| | &Alias / &Triggers / &Paths | F5 / F6 / F7 | Requieren personaje |
| | Modo silencioso (marca) | F8 | Ejecuta `callate` / `hablar` |
| | &Opciones | F9 | Nivel personaje si lo hay; si no, nivel MUD |
| | Configurar &teclas de movimiento | — | |
| | &Direcciones para los paths | — | |
| **Ay&uda** | &Acerca de… · &Manual de usuario (F1) · Omnimud en la web ▸ (web, lista, e-mail) · &Enviar errores o sugerencias ▸ | | |

Menú contextual de Recibido y Mensajes: Copiar, Seleccionar todo, Buscar, Buscar siguiente. Los menús se ocultan mientras se conecta.

### 3.3 Teclado (además de los atajos de menú)

**En toda la ventana**

| Tecla | Comportamiento |
|---|---|
| **Ctrl+1 … Ctrl+9, Ctrl+0** (fila superior y teclado numérico) | Lee el mensaje N más reciente (0 = 10). Dice `"N: <mensaje>"` **interrumpiendo** la voz. Si se pulsan dígitos con menos de **400 ms** entre sí se concatenan (Ctrl+1, Ctrl+5 → mensaje 15), máximo 3 cifras (la cuarta: pitido). Sin mensajes: `"No hay mensajes."`; fuera de rango: `"Solo hay X mensajes."`. AltGr+número no lo dispara. Funciona aunque el cuadro de Mensajes esté oculto |
| **Ctrl+º** (tecla a la izquierda del 1) | Repaso hacia atrás: la primera pulsación lee el 1; cada pulsación antes de **800 ms** pasa al 2, 3…; si se espera más, vuelve al 1 |
| **Escape** | Cancela la concatenación de dígitos |
| Alt+T / Alt+R / Alt+M | Foco a cada cuadro (mnemónicos de las etiquetas) |

**Con foco en Texto a enviar**

| Tecla | Comportamiento |
|---|---|
| Enter con texto | Procesa y envía; lo añade al historial; limpia el cuadro |
| Enter vacío | **Repite el último comando**; si no hay, envía línea en blanco |
| Shift+Enter o Ctrl+Enter | Vacío: envía línea en blanco. Con texto: inserta salto de línea |
| Flecha arriba / abajo | Historial (suena `click.wav`, selecciona todo el texto); al pasar del más reciente limpia el cuadro. Si el cuadro es multilínea y hay línea anterior/siguiente, **verbaliza esa línea** y mueve el cursor normalmente |
| NumPad 0-9 | Con modo movimiento (F2) y tecla configurada: ejecuta su comando por el procesador de entrada y suprime la tecla |

Modo contraseña (el MUD envía `IAC WILL ECHO`): lo tecleado se muestra como `*` y **no entra en historial, ni en "último comando", ni en el log**.

**Con foco en Recibido o Mensajes**

| Tecla | Comportamiento |
|---|---|
| Enter sobre URL o e-mail | Lo abre (suena `url.mp3`); si falla, sonido de exclamación |
| Ctrl+F | Verbaliza la hora del mensaje bajo el cursor: `"HH:mm:ss, el dd/MM/yyyy"` o `"Hora no encontrada."` |
| Cualquier carácter imprimible | "Escritura directa": pitido, foco a Texto a enviar y la tecla se escribe allí |
| Flechas, Inicio/Fin, Shift+… | Navegación y selección estándar |

**[CORREGIR]** El historial solo considera multilínea con 3+ líneas; Ctrl+NumPad con modo movimiento lee el mensaje **y además** mueve; la escritura directa usa una espera activa que puede colgar; Enter sin URL en un cuadro de lectura acaba repitiendo el último comando; "Sólo ay X mensajes"; Ctrl+F usa siempre el cursor de Mensajes aunque el foco esté en Recibido; el puntero del historial no se reinicia al teclear; el historial guarda N+1 entradas.

### 3.4 Comandos internos (tecleados en Texto a enviar)

Orden de evaluación: (1) `_path`, (2) alias, (3) triggers de comando `@`, (4) `±triggers`, (5) `±trigger`, (6) `uncalias`, (7) `calias`, (8) captura de dirección si se está grabando, (9) comandos de coincidencia exacta, (10) envío al MUD. Las respuestas se inyectan como texto recibido: se ven, se registran, se hablan y pueden disparar triggers.

| Comando | Efecto |
|---|---|
| `_nombre` / `_nombre -r` | Ejecuta el path, o su vuelta |
| `calias` | Abre Alias |
| `calias N` | Dice a qué acción está asignado N |
| `calias N acción…` | Crea el alias (valida vacío, `æ`, duplicado; avisa si otra entrada tiene la misma acción) |
| `uncalias N` | Borra el alias |
| `-triggers` / `+triggers` | Desactiva/activa todo el sistema **solo para la sesión** |
| `-trigger N` / `+trigger N` | Desactiva/activa un trigger y **lo guarda** |
| `triggers` / `paths` | Abren sus ventanas |
| `paths iniciar` | Empieza a grabar: cada comando que sea una dirección conocida suena `pop.wav`, se anota y se envía |
| `paths ultima` · `paths ultima borrar` · `paths grabado` | Consulta y corrección durante la grabación |
| `paths cancelar` | Cancela (con confirmación) |
| `paths detener` | Abre el alta de path con el camino grabado, ya colapsado |
| `cls` | Vacía Recibido y dice "OK" |
| `callate` / `hablar` | Activa/desactiva el modo silencioso |

Literales completos en A1 §4.
**[CORREGIR]** "name" por "nombre" en muchos mensajes; "cunalias"; `paths detener` sin grabación y `_x -r` no invertible lanzan excepción; `paths cancelar` no limpia lo grabado; en modo desconexión no se ve ninguna respuesta; la marca de F8 no se sincroniza si se teclea `callate`.

---

## 4. Recepción: orden exacto del procesamiento

Unidad de proceso: **la ráfaga TCP** (lo leído mientras hay datos, buffer de 4096 bytes, decodificado con la página ANSI del sistema). No hay troceo en líneas ni buffer de línea parcial.

1. Aplicar retrocesos (0x08) y eliminar todos los `\r`.
2. **MSP**: extraer y ejecutar las líneas `!!SOUND(…)` / `!!MUSIC(…)`; se eliminan siempre del texto (§9).
3. **Telnet**: quitar la negociación (§6).
4. Si un trigger con código espera respuesta (`OmGet`), la ráfaga se le entrega y **no se muestra, ni registra, ni habla, ni dispara triggers**.
5. **ANSI** → pintado en Recibido (§7), quitando el marcador `all_speak:`.
6. **Cursor** según la opción: ir al final · mantener posición y selección · **ir al final solo si ya estaba al final** (por defecto). Es lo que permite repasar texto con el lector sin que el cursor salte.
7. **Triggers** sobre el texto sin ANSI (§8), si el sistema no está desactivado.
8. **Regla de mensajes** del MUD → si devuelve texto, se añade a Mensajes (§5). El texto **no** se retira de Recibido. Si la regla falla suena `error.wav`.
9. Si la ventana no está activa: **parpadeo** en la barra de tareas (cada 500 ms durante 3 s). Sin opción para desactivarlo.
10. **Log** (§12).
11. **Voz** (§5.2).

**[CORREGIR]** Líneas partidas entre ráfagas se procesan en dos trozos (triggers y reglas fallan); Recibido y Mensajes crecen sin límite con `GC.Collect()` cada minuto como paliativo; el temporizador de parpadeo acumula manejadores.

### 4.1 Envío

1. En modo desconexión responde `"Estás en modo de desconexión. No puedes enviar ningún comando."`.
2. **Concatenación** (opcional, **desactivada por defecto**, carácter `;`): parte la línea; el carácter doble es el literal.
3. **Repetición** (opcional, **desactivada por defecto**, carácter `#`): `N#comando` lo envía N veces, máximo 50.
4. Cada comando se envía terminado en `\n`.
5. Se anota en el log el comando original, salvo en modo contraseña.

**No hay eco local**: lo enviado no aparece en Recibido ni se habla. El alias solo se aplica a la primera palabra de la línea completa, antes de la concatenación. Los movimientos numpad y las acciones de triggers pasan por el mismo procesador pero no entran en el historial.
Historial: solo en memoria, por sesión, tamaño configurable (10 por defecto), sin filtrar duplicados.
**[CORREGIR]** El escape de concatenación restaura siempre `;` aunque el carácter configurado sea otro.

---

## 5. Mensajes y voz

### 5.1 Mensajes

- Origen: la regla de mensajes del MUD (§11) o `AddMessage` desde un trigger con código.
- Cada mensaje guarda texto, **fecha y hora** y su posición en el cuadro. Límite de 1000 en memoria; no se persisten.
- Lectura rápida con Ctrl+número y Ctrl+º (§3.3). No incluye la hora; la hora se consulta con Ctrl+F o en la barra de estado.
- El cursor de Mensajes sigue su propia opción de posición (mismas tres variantes que Recibido).

### 5.2 Voz

- Lectores: Ninguno, JAWS (**por defecto**), Window-Eyes, NVDA, Autodetectar (NVDA → JAWS → Window-Eyes, re-detectado en cada llamada). Sin SAPI ni braille propio.
- Se habla **todo el texto de cada ráfaga, solo con la ventana activa**; no existe opción de hablar fuera de la ventana. El texto entrante **se encola** (no interrumpe); la lectura de mensajes **sí interrumpe**.
- **Modo silencioso** (F8, `callate`/`hablar`): no se habla nada salvo lo que venga detrás del marcador `all_speak:`. Fuera de ese modo el marcador simplemente se elimina. El marcador nunca se muestra ni se registra. No afecta a sonidos ni a Ctrl+número.
- No hay filtrado del texto antes de hablar, más allá de quitar ANSI, MSP, telnet, retrocesos y `\r`.
- Otros anuncios: "OK" tras `cls`; línea anterior/siguiente en el cuadro de envío multilínea; hora del mensaje; activar/desactivar un trigger con Espacio en la lista de triggers; `SayText` desde triggers.

**[CORREGIR]** Autodetectar nunca encuentra Window-Eyes (busca el proceso con `.exe`); si falta la DLL del lector sale un MessageBox por cada texto recibido.

---

## 6. Conexión y telnet

- Alta de sesión con MUD, o con MUD + personaje. Conexión asíncrona cancelable.
- **Login automático**: nada más conectar, **sin esperar ningún prompt**, envía el nombre y, si la hay, la contraseña. No se registra ni entra en el historial.
- **Modo de desconexión**: si falla la conexión, ofrece abrir el personaje sin conexión para gestionar alias, triggers y paths.
- **Proxy**: las opciones existen, pero solo se usan para HTTP (actualizaciones, descarga de sonidos). La conexión al MUD nunca pasa por proxy.
- **Reconexión: no existe.** Si el servidor corta, la ventana se cierra sola y reaparece la anterior.
- **Cierre**: (1) si hay conexión y "Confirmar", pregunta `¿Estás seguro de que deseas cortar la conexión?`; (2) si "Intentar salvar antes de salir" y hay `QuitCommand`, **envía `QuitCommand`** (sic); (3) corta sin esperar respuesta; (4) escribe en el log `Partida finalizada el … a las ….`; (5) para sonidos y música.
- **Telnet** (opcional, **desactivado por defecto**): solo entiende ECHO. A `IAC WILL ECHO` responde `DO ECHO` y entra en modo contraseña; a `WONT ECHO` sale. El resto de opciones se descartan sin responder.
- Codificación: la ANSI del sistema, en ambos sentidos.

**[CORREGIR]** `GA`, subnegociaciones e `IAC IAC` mal tratados; no se rechazan las opciones no soportadas; "intentar salvar" envía el comando de abandonar (**[DECISIÓN]**: ¿enviar `SaveCommand` y luego `QuitCommand`?).

---

## 7. Colores ANSI

Atributos 0-9 y sus desactivaciones 22-29, colores de texto 30-37/39 y de fondo 40-47/49, con estado acumulativo e inverso. Ocho colores con los nombres de .NET (Green = 0,128,0; Purple = 128,0,128). La negrita se pinta como **fuente negrita**, no como color brillante. Fondo del cuadro forzado a blanco.

**[CORREGIR]** De una secuencia con varios parámetros solo se aplica el último; 23/24/25/29 no hacen nada; 37 y 39 dan blanco sobre blanco. La v2 ya tiene un parser mejor (256 colores, TrueColor, brillantes): se conserva.

---

## 8. Triggers

### 8.1 Datos

Nombre (único por personaje) · texto desencadenante · acción · sonido · **qué hará** (5 valores) · activado · distinguir mayúsculas · usar expresión regular · tipo de expresión (normal / de reemplazo) · GUID · firma.

"Qué hará" en el editor: **acción** (enviar comando) · **sonido** · **ambas** · **código C#** · **código VB.Net**. El editor habilita Acción y/o Sonido según el valor; con código, la etiqueta y el `AccessibleName` pasan a "Código c#:" / "Código Vb.Net:" y desaparece la opción de reemplazo. Sin botón de probar: la regex y el código se validan al Aceptar.

### 8.2 Cuándo se evalúan

Tras pintar cada ráfaga, sobre su texto **sin ANSI**. Se ejecutan **todos** los que coinciden, en orden de creación; ninguno corta a los demás. **No existe ocultar (gag) ni reescribir el texto mostrado.**

### 8.3 Coincidencia

Un trigger coincide si se cumple cualquiera de:
- **Regex** .NET (si está marcada), con `Singleline` e `IgnoreCase` según la casilla.
- **Literal**: el desencadenante aparece como subcadena (respetando o no mayúsculas).
- **Comodines** estilo sscanf: `%s` (texto), `%d` (dígitos), `%w` (palabra), `%Ns` (N caracteres), `%Nw` (N palabras), `%%` (literal). Cada comodín produce una **incógnita**.

Anclas en modo literal: `^` al principio y `$` al final del desencadenante exigen que el **bloque** o alguna de sus **líneas** empiece/acabe por el texto. Con comodines, los saltos de línea se convierten en espacios, así que las anclas se refieren al bloque.

### 8.4 Acciones

- **Enviar comando**: la acción pasa por el procesador de entrada completo (alias, paths `_x`, comandos internos, triggers `@`, concatenación, repetición). Con comodines, `%1`, `%2`… se sustituyen por las incógnitas. Con regex, la acción es la cadena de reemplazo de `Regex.Replace`.
- **Sonido**: reproduce el fichero (prioridad 50, volumen 100) si la ventana está activa o se permiten sonidos fuera de ventana.
- **Ambas**.
- **Código**: ver §8.6.

### 8.5 Triggers de comando (`@palabra`)

Si el desencadenante empieza por `@`, no se evalúa contra el texto recibido sino contra la **primera palabra de lo que teclea el usuario** (después de aplicar alias). Si coincide: se ejecuta con las demás palabras como argumentos (`%1`…/`args`) y con la línea completa en `FullCommand`, y **el comando no se envía al MUD**. Son, en la práctica, comandos definidos por el usuario. No admiten regex ni espacios en el nombre.

### 8.6 Código C#/VB y su API (a portar a Lua)

El código del usuario se inserta en el `Main` de una clase que hereda de `CTriggerFunctions`, se compila a una DLL por trigger y se ejecuta en un hilo aparte (un mismo trigger no se solapa consigo mismo; triggers distintos sí corren en paralelo). Recibe `args` (solo las incógnitas o los argumentos del comando) y `FullCommand`. Sin sandbox: acceso total a .NET y a la ventana (`Cln`).

| Función original | Semántica |
|---|---|
| `OmSend(cmd)` | Envía por el procesador de entrada completo |
| `OmGet(cmd [, patrónEsperado] [, conColores])` | Envía **sin** alias y **espera la respuesta**: captura las ráfagas (que no se muestran) hasta que una acaba en salto de línea o en espacio, o 3 s. Con patrón, solo captura las ráfagas que lo cumplen. Devuelve `""` si vence |
| `OmDisplay(texto)` | Inyecta texto como si viniera del MUD (pasa por todo: colores, triggers, mensajes, log, voz) |
| `SayText(texto)` | Lo dice por el lector, sin interrumpir |
| `AddMessage(texto)` | Lo añade a Mensajes (y muestra el cuadro si estaba oculto) |
| `PlaySound(nombre [, repeticiones])` | Busca en: ruta tal cual → carpeta de sonidos del MUD → `sounds\` de la aplicación. `-1` = bucle infinito |
| `StopSound(nombre)` | Detiene ese sonido; devuelve si estaba sonando |
| `OmSetVar` / `OmQueryVar` / `OmRemoveVar` / `OmVarIsSet` | Variables de sesión (texto), compartidas por todos los triggers de la ventana; no se guardan |
| `StartCount(ticks [, tipo] [, sonidoDurante], sonidoFin)` | Cuenta atrás bloqueante; tipo `s`, `d`, `c`, `m` (segundos, décimas, centésimas, milésimas) con multiplicador opcional (`"2s"`); suena un bucle durante y un sonido al final |
| `GetLastActivity()` | Hora del último envío |
| `RemoveColours(texto)` | Quita secuencias ANSI |
| 26 constantes `Apearance*` | Secuencias ANSI para usar con `OmDisplay` |
| `Thread.Sleep`, resto de .NET | Usados en los ejemplos de la ayuda (esperas, peticiones web) |

Ejemplos de la ayuda (A2 §1.10): responder a un `%s te dice: '%s'`; recoger botín tras `^%s ha muerto.`; estado "dormido" con variables; copiar canciones a Mensajes; comando `@AvisaCura` que consulta los puntos de vida cada 2 s con `OmGet` y avisa con sonido; contadores con `StartCount`.

**Firma**: RSA-1024 + SHA-1 sobre el código. Solo se comprueba al importar un fichero de triggers (aviso si falta o no es válida). **[NO PORTAR]**: con Lua en sandbox deja de ser necesaria.

### 8.7 Gestión

Lista con Agregar, Editar, Quitar (Supr), Activar/Desactivar (Espacio, con anuncio por voz), Exportar, Importar **desde fichero o desde otro personaje**. Orden por columnas recordado por personaje.

**[CORREGIR]** (A2 §1.4) Incógnitas compartidas entre triggers del mismo bloque; mayúsculas invertidas en el reemplazo; regex con `^`/`$` que casi nunca disparan; con regex se envía al MUD todo el bloque y no solo la sustitución; incógnitas en minúsculas en triggers no sensibles; "ambas" con comodines no suena; editar un trigger lo reactiva; el tipo "de reemplazo" no tiene efecto; `%Nd` documentado y no implementado; una excepción en el código cierra la aplicación; dos `OmGet` simultáneos se pisan; la ayuda dice 5 s de espera y son 3.

---

## 9. Sonido (MSP)

- Líneas `!!SOUND(nombre V= L= P= C= T= U=)` y `!!MUSIC(…)`, **solo a principio de línea**; `off` detiene todo lo de ese tipo. Siempre se eliminan del texto.
- `V` volumen 0-100 · `L` repeticiones (-1 infinito) · `P` prioridad (50) · `C` continuar si ya suena · `T` tipo, usado como **subcarpeta** · `U` URL base de descarga.
- Requiere **carpeta de sonidos configurada en el MUD**; sin ella no hay MSP.
- Tres categorías: sonido, música, sonidos de trigger. Opciones separadas para activar sonido y música, y para permitir cada uno **con la ventana inactiva**.
- Prioridades por categoría: uno nuevo no suena si hay otro vivo de mayor prioridad, y detiene a los de menor.
- Formatos wav, mp3, ogg (irrKlang). Sin extensión se asume `.wav`.
- **Descarga**: si el fichero no existe, está activada la opción y la orden trae `U=`, se descarga a la carpeta del MUD (respetando el proxy) y se reproduce al terminar. No hay URL base configurable.
- Sonidos propios: `click.wav` (historial), `pop.wav` (dirección grabada), `url.mp3` (abrir enlace), `error.wav` (fallo de regla), `reloj.mp3` y `reloj_fin.mp3` (contadores).

**[CORREGIR]** `C=1` del estándar no se reconoce (solo `true`/`false`); sin comodines en el nombre; los sonidos de trigger consultan la tabla de prioridades de música; descargas duplicadas; no hay ajuste de volumen general.

---

## 10. Alias, paths y movimiento

### 10.1 Alias
Por personaje. Comando → acción. Coincidencia **exacta y sensible a mayúsculas** con la primera palabra; el resto de la línea se añade detrás. Sin parámetros, sin recursión, sin activar/desactivar. Gestión: Agregar, Editar, Quitar, Exportar, Importar desde fichero o desde otro personaje, y los comandos `calias`/`uncalias`.

### 10.2 Diccionario de direcciones
**Por MUD.** Cada entrada: dirección completa, abreviatura de **un carácter** y dirección contraria. **Sin valores de serie**: mientras esté vacío no se pueden crear ni ejecutar paths. Viaja dentro de la exportación del MUD.

### 10.3 Paths
Por personaje. Formato `[N]c` repetido (`5eo3e`); se guardan siempre colapsados. Validados contra el diccionario. `_nombre` envía todas las direcciones completas de una vez, **sin retardo entre pasos**; `_nombre -r` recorre al revés usando las contrarias. Al guardar avisa si no es invertible o si otro path tiene el mismo camino. Grabación interactiva (§3.4). Importar desde fichero o desde otro personaje.

### 10.4 Teclas de movimiento
NumPad 0-9 → un comando cualquiera cada una. **Sin valores de serie.** Por MUD o por personaje (al editar las del personaje se parte de las del MUD); la marca de modo movimiento (F2) también se guarda por MUD o personaje.

---

## 11. Reglas de mensajes

Plugins `IRule { Name; ProcessMessage(bloque) }` cargados de `ProcessRules\*.dll`; cada MUD elige una o ninguna. Reciben el bloque sin ANSI y devuelven el texto para Mensajes o `null`. Cuatro reglas: **Balzhur**, **Callandor**, **Simauria**, **Cyberlife** (15 regex). Patrones exactos en A2 §9.

---

## 12. Logs

Tipo: ninguno · **por día** (por defecto) · por partida. Carpeta configurable; al heredar se añade una subcarpeta con el nombre del MUD o del personaje. Fichero `d-M-yyyy.log` o `d-M-yyyy H-m-s.log`, en UTF-8, añadiendo al final, **sin ANSI**, con lo recibido y lo enviado (salvo contraseñas). Cierra con `Partida finalizada el dd/MM/yyyy a las HH:mm:ss.`
**[CORREGIR]** Nombres sin ceros a la izquierda, que no ordenan bien (**[DECISIÓN]**: `yyyy-MM-dd`).

---

## 13. Opciones

Seis pestañas: **General** (lector de pantalla; posición del cursor en Recibido y en Mensajes; número de últimos comandos; intentar guardar al desconectar; confirmar acciones; negociación telnet) · **Logs** · **Sonidos** (efectos; música; cada uno fuera de ventana; descargar automáticamente) · **Conexión** (proxy: desactivado, automático, manual + host y puerto) · **Apariencia** (fuente con vista previa) · **Caracteres especiales** (concatenación y repetición: casilla + carácter, distintos entre sí). Exportar e importar todas las opciones. Tabla completa con claves y valores por defecto en A2 §5.1.

**Ámbito**: lo fija quien abre el diálogo — global desde la pantalla de inicio; personaje (o MUD si no hay personaje) con F9 desde el juego. **Herencia** personaje → MUD → global **por bloque completo**: mientras un nivel no ha guardado nada, hereda; en cuanto guarda, copia todo y deja de heredar.
**[CORREGIR]** No hay forma de volver a heredar ni indicación del ámbito; cada pestaña repite sus botones; al importar se duplican los ítems de los combos; la casilla de repetición muestra el texto de la de concatenación; controles huérfanos; el foco puede ir a un control de una pestaña no visible.

---

## 14. Persistencia e intercambio

- **Registro**: `HKCU\Software\KastweySoftware\omnimud` con `muds\<id>` y `characters\<id>`; listas en `REG_MULTI_SZ` con `æ` como separador (por eso `æ` está prohibido en todos los campos). Estructura completa en A2 §6.1.
- **Ficheros `.oxf`**: XML UTF-8 sin versión; la raíz indica el tipo (muds, personajes, alias, triggers, paths, opciones…). Un MUD exportado lleva su diccionario y, opcionalmente, sus personajes; un personaje lleva alias, triggers, paths, movimientos y opciones. Esquema y ejemplos en A2 §6.2.
- Importar: pregunta uno a uno si sobrescribir los duplicados y muestra un resumen final; sobrescribir un MUD con personajes pide doble confirmación.
- **Contraseñas**: cifrado de sustitución posicional, reversible (A2 §7 incluye la rutina de descifrado, necesaria para migrar).
- MUD: nombre, host, puerto, comando de guardar, comando de salir, regla de mensajes, carpeta de sonidos, personaje predeterminado. Personaje: nombre, contraseña (opcional, "recordar"), MUD. **No hay MUD predefinidos.**

---

## 15. Patrones de accesibilidad del original (obligatorios en la v2)

1. Cada control de entrada va precedido de una **etiqueta visible con mnemónico**, con `TabIndex` consecutivos.
2. `AccessibleName` y `AccessibleDescription` iguales al texto de la etiqueta, **actualizados cuando el texto cambia en ejecución** (botón de acción, etiqueta de código del trigger).
3. `AcceptButton` y `CancelButton` en todos los diálogos (Enter conecta en Muds y Personajes; Escape cierra).
4. Listas: **Supr** quita, menú contextual con Agregar/Editar/Quitar, **siempre hay un elemento seleccionado**, y tras borrar se selecciona el vecino.
5. Confirmación Sí/No en cada borrado y cada sobrescritura.
6. Tras un error de validación, **el foco va al campo erróneo**; los cuadros se cargan con todo el texto seleccionado.
7. Botones deshabilitados según el estado.
8. Cambios de estado no visuales **anunciados por voz** (activar/desactivar trigger, modo silencioso, "OK" tras `cls`).

**[CORREGIR]** Mnemónicos duplicados (&D, &C, &A, &E, &B, &G, &I); cuadros sin nombre accesible (abreviatura, contraria, puerto del proxy, últimos comandos, texto del informe); el diálogo de direcciones no cierra con Escape; no hay Insert para agregar ni Enter para editar; la ordenación por columnas solo con ratón; el doble clic para conectar en Personajes no está enganchado; el diálogo Buscar queda imposible de cerrar tras buscar texto vacío; `FrmPaths` exporta aunque se cancele.

---

## 16. Servicios externos y versiones

- **Actualizaciones**: catálogo XML por HTTP en `omnimud.org`, sin firma, con `AutoUpdater.exe` y `CloseOmnimuds.exe`. **[CORREGIR]** la comparación de versiones trata `1.10` como `1.1`.
- **Informes de error y sugerencias**: servicio SOAP `Reporting.asmx`; también lo usa el manejador global de excepciones.
- **Ayuda**: `omnimud.chm` con F1; enlaces a la web, lista de distribución y correo.
- **Instalador**: incluye `vcredist_x86` (por irrKlang) y `url.mp3`.
- **trunk vs 1.2 vs tag**: `cliente` es idéntico en trunk y 1.2 salvo `FrmPrincipal` (Salir roto en trunk), `PlatformTarget x86` y versión 1.2.0.0 en la rama, y la regla Cyberlife solo en trunk. El tag 1.1.0.1 es anterior (sin NVDA/Autodetectar). **Referencia de paridad: trunk + `BtnSalir_Click` de la 1.2.**

---

## 17. Errores de la documentación anterior

| Decía | Realidad |
|---|---|
| `Shift+Ctrl+Enter` inserta línea | Shift+Enter **o** Ctrl+Enter |
| Enter vacío "re-envía" sin más | Repite el último comando; la línea en blanco es Shift/Ctrl+Enter |
| `Ctrl+\` lee los últimos N con hora | Ctrl+º repasa de uno en uno hacia atrás, sin hora |
| El lector respeta "leer fuera de ventana" | La voz **solo** funciona con la ventana activa; "fuera de ventana" solo existe para sonido y música |
| La voz interrumpe al recibir texto | El texto entrante se encola; solo interrumpe la lectura de mensajes |
| Proxy para la conexión | Solo para HTTP |
| Codificación UTF-8 | Página ANSI del sistema |
| Negociación de MSP por telnet | Solo ECHO; MSP se detecta en el texto |
| Diccionario con norte/sur/… de serie | Vacío; sin él no hay paths |
| Triggers ejecutados "en ThreadPool" en general | Solo los de código |
| 4 tipos de acción | 5: acción, sonido, **ambas**, C#, VB |
| No mencionaba | Triggers de comando `@`, F1-F9, Ctrl+E, Ctrl+F (hora), concatenación de dígitos en Ctrl+número, escritura directa desde cuadros de lectura, abrir URL con Enter, importar desde otro personaje, modo de desconexión, login automático, `StartCount`, `GetLastActivity`, ficheros `.oxf`, herencia de opciones por bloque |

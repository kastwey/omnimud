# OMnimud

Cliente de MUD para Windows **pensado desde el principio para usarse con lector de pantalla** (NVDA, JAWS o Narrador). Es la reescritura, en .NET 10 y WinForms, del OMnimud original de 2008-2017.

> English summary: OMnimud is an accessible MUD client for Windows, built for screen reader users. Interface in Spanish and English. See [docs/](docs/) (in Spanish) for the functional specification, architecture and Lua scripting reference.

## Qué ofrece

**Accesibilidad**
- Cada cuadro tiene etiqueta visible con mnemónico y nombre accesible; todo se maneja con teclado y todos los atajos están en los menús para poder descubrirlos.
- El texto del MUD se anuncia por **UI Automation** (cualquier lector), con las librerías de JAWS y NVDA como respaldo.
- El cursor del cuadro de texto recibido **no se mueve** cuando llega texto nuevo: puedes repasar lo anterior mientras el MUD sigue escribiendo.
- Lectura rápida de mensajes: `Ctrl+1`…`Ctrl+0` (con concatenación de dígitos), `Ctrl+º` para repasar hacia atrás, `Ctrl+F` para la hora.
- Interfaz en español e inglés.

**Juego**
- Telnet con TLS 1.2/1.3, codificación por MUD, proxy SOCKS5 y HTTP CONNECT (manual o el de Windows), reconexión.
- Colores ANSI de 16 y 256 colores y TrueColor.
- **Triggers** por línea o por bloque: texto literal, expresiones regulares o comodines (`%s`, `%d`, `%w`); pueden enviar comandos, reproducir sonidos, ocultar la línea o ejecutar un **script Lua** en sandbox. Triggers de comando (`@nombre`).
- **Alias**, **paths** con ida y vuelta y grabación, direcciones por MUD.
- **Modo movimiento** (`F2`): flechas, Re Pág, Av Pág y teclado numérico envían direcciones, con valores por defecto.
- **Sonido**: MSP (`!!SOUND`, `!!MUSIC`) con wav, mp3 y ogg, prioridades y descarga de sonidos.
- **GMCP**: canales (`Comm.Channel.Text`) al cuadro de Mensajes, y menú **Acciones** generado a partir de `Char.Inventory` y `Room.Info`.
- **Reglas de mensajes** por MUD, con patrones o con script Lua, para llevar las conversaciones al cuadro de Mensajes.
- Opciones en tres niveles (global, MUD, personaje) con herencia; registros de sesión; importación y exportación en ficheros `.omnimud`.

**Privacidad**
- Nada sale de tu equipo por su cuenta: sin telemetría, la comprobación de actualizaciones es manual (o al iniciar, solo si tú la activas) y anónima, y los informes de error o sugerencias se abren ya escritos en tu navegador (issue de GitHub) o en tu correo para que los revises y los envíes tú. Nunca incluyen contraseñas, texto del MUD, registros, direcciones de MUD ni nombres de personaje.

**Portable**
- Todo se guarda en SQLite dentro de una carpeta `data\` junto al ejecutable. No usa el Registro de Windows. Copiando la carpeta te llevas todo (salvo las contraseñas guardadas, que van cifradas y ligadas a tu usuario de Windows).

## Descargar

Las versiones publicadas están en **[Releases](https://github.com/kastwey/omnimud/releases)**. Cada una trae:

- `Omnimud-<versión>-win-x64-portable.zip`: descomprímelo en una carpeta donde puedas escribir y ejecuta `Omnimud.exe`. No necesita instalación ni .NET.
- `Omnimud-<versión>-win-x64-setup.exe`: instalador por usuario (en `%LocalAppData%\Programs\Omnimud`), que **no pide permisos de administrador**. Al desinstalar pregunta si quieres conservar tus datos.
- Un fichero `.sha256` por cada uno, para comprobar la descarga (`Get-FileHash fichero -Algorithm SHA256` en PowerShell).

En los dos casos tus datos quedan en la carpeta `data` junto al ejecutable. El manual de usuario se abre con `F1` y también está en [docs/manual/](docs/manual/) (español e inglés).

Omnimud **no se actualiza solo ni descarga nada**: desde el lanzador, Herramientas → «Comprobar actualizaciones ahora» consulta de forma anónima las releases de GitHub y, si hay una versión nueva, ofrece abrir su página en el navegador. La comprobación al iniciar está desactivada por defecto.

## Requisitos

- Windows 10 (1709) o posterior.
- Para compilar: [SDK de .NET 10](https://dotnet.microsoft.com/download).

## Compilar, probar y publicar

```bash
dotnet build Omnimud.sln
```

```bash
dotnet test Omnimud.sln
```

Ejecutable autocontenido y portable (no necesita .NET instalado), en `publish\win-x64\`:

```bash
dotnet publish src/Omnimud.UI -p:PublishProfile=portable-win-x64
```

Paquete completo de una versión (compila, prueba, publica en una carpeta limpia y genera el zip portable con su SHA-256 y, si está instalado [Inno Setup 6](https://jrsoftware.org/isinfo.php), también el instalador), en `artifacts\release\`:

```powershell
.\tools\release.ps1
```

### Publicar una versión

1. Cambia `<Version>` en `Directory.Build.props` (es la única fuente de la versión: la que muestra «Acerca de», la que llevan los informes y la que se compara al comprobar actualizaciones) y haz commit.
2. Opcional pero recomendable: ejecuta `.\tools\release.ps1` en local y prueba el zip.
3. Crea y empuja la etiqueta con el mismo número: `git tag v2.1.0` y `git push origin v2.1.0`.
4. El flujo `.github/workflows/release.yml` compila, ejecuta los tests, empaqueta y crea la release de GitHub con el zip, el instalador y sus hashes. Si la etiqueta no coincide con `Directory.Build.props`, falla sin publicar nada. Una etiqueta con guion (`v2.1.0-beta.1`) se publica como versión preliminar, que la comprobación de actualizaciones ignora.
5. Revisa las notas generadas en la página de la release: son las que verá el usuario en el aviso de versión nueva.

Cada push y cada pull request pasan por `.github/workflows/ci.yml` (compilación sin avisos y tests). Los pocos tests que necesitan un escritorio interactivo de verdad llevan el rasgo `Category=InteractiveDesktop` y solo se ejecutan en local.

## Organización del código

| Proyecto | Contenido |
|---|---|
| `src/Omnimud.Core` | Lógica sin interfaz ni base de datos: sesión, telnet, ANSI, triggers, Lua, alias, paths, sonido, GMCP, opciones |
| `src/Omnimud.Data` | SQLite con Dapper: migraciones, repositorios, opciones, importación y exportación |
| `src/Omnimud.UI` | WinForms: ventanas, presentadores, anuncios al lector, audio |
| `tests/` | Tests de los tres proyectos, incluidas auditorías automáticas de accesibilidad y pruebas de extremo a extremo con un MUD falso |

Una sesión (`MudSession`) por conexión es dueña de todos sus motores; la ventana es solo una vista. La lógica de los diálogos vive en presentadores sin WinForms para poder probarla sin ventanas.

## Documentación

En [docs/](docs/), en español:

- [Funcionalidad del OMnimud original](docs/01_FUNCIONALIDAD_ORIGINAL.md): el contrato de paridad de esta reescritura.
- [Plan de paridad](docs/02_PLAN_PARIDAD_V2.md) y [arquitectura](docs/03_ARQUITECTURA_Y_REPARTO.md).
- [Estado actual y pendientes](docs/04_ESTADO.md).
- [Referencia de scripts Lua](docs/API_LUA.md).
- Manual de usuario: [español](docs/manual/manual.es.html) e [inglés](docs/manual/manual.en.html).

## Componentes de terceros

- [MoonSharp](https://www.moonsharp.org/) (Lua), [NAudio](https://github.com/naudio/NAudio) y NAudio.Vorbis (audio), [Dapper](https://github.com/DapperLib/Dapper) y Microsoft.Data.Sqlite (datos).
- `dlls/`: `nvdaControllerClient.dll` de [NV Access](https://www.nvaccess.org/) (LGPL 2.1) y las librerías de la API de JAWS de Freedom Scientific (`FSAPI.dll`, `jfwapi.dll`), que pertenecen a sus respectivos autores y se incluyen solo para poder hablar con esos lectores.

## Licencia

Copyright © Juanjo Montiel.

OMnimud es software libre: puedes redistribuirlo y modificarlo bajo los términos de la **Licencia Pública General de GNU, versión 2 o (a tu elección) cualquier versión posterior**, publicada por la Free Software Foundation. Se distribuye con la esperanza de que sea útil, pero SIN NINGUNA GARANTÍA. El texto completo está en [LICENSE](LICENSE).

Como excepción especial, el titular de los derechos te permite enlazar y distribuir OMnimud junto con las librerías de comunicación con lectores de pantalla incluidas en `dlls/` (la API de JAWS de Freedom Scientific y el cliente del controlador de NVDA), sin que eso obligue a que dichas librerías estén bajo la GPL. Esas librerías conservan sus propias licencias.

SPDX: `GPL-2.0-or-later`

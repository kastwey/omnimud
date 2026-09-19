# Ronda 2 — Movimiento, opciones y diálogos de gestión

Reglas comunes para los cuatro bloques de esta ronda. Lectura previa obligatoria: [02_PLAN_PARIDAD_V2.md](02_PLAN_PARIDAD_V2.md) §4 (accesibilidad), [04_ESTADO.md](04_ESTADO.md), [03_ARQUITECTURA_Y_REPARTO.md](03_ARQUITECTURA_Y_REPARTO.md) §1, §8 y §9, y del original [01_FUNCIONALIDAD_ORIGINAL.md](01_FUNCIONALIDAD_ORIGINAL.md) más el anexo [A3](auditoria/A3_trunk_dialogos.md) para el diálogo que te toque.

## Exigencias del usuario (literales)

SOLID, accesibilidad desde el principio, tests a mansalva para probarlo todo, y desacoplamiento a muerte, sobre todo para los tests. Todo en base de datos, nada de Registro. Añadir sí, recortar no.

## Diseño

- **Un formulario no contiene lógica.** Validación, conflictos, importación, conversión y reglas viven en clases sin dependencia de WinForms (presentadores o servicios, en `Omnimud.UI/Presenters` o en Core/Data si procede), con constructor que recibe interfaces. El formulario solo enlaza controles y muestra mensajes. Esas clases se prueban sin ventana.
- Diálogos del sistema (abrir/guardar fichero, carpeta, fuente, MessageBox) detrás de una interfaz pequeña (`IUserPrompts` o similar; si ya existe una de otro bloque, reutilízala) para poder probar los flujos.
- Los formularios reciben repositorios y servicios por constructor, nunca `IServiceProvider`.
- Ningún `async void` sin `ErrorReporter.Run`. `DuplicateEntityException` se traduce a un mensaje y foco al campo, nunca tumba la aplicación.

## Accesibilidad (criterio de aceptación de cada formulario)

1. `AccessibilityAudit.Check(form, isDialog)` (en `tests/Omnimud.UI.Tests/Accessibility`) devuelve vacío **en español y en inglés**. Es de solo lectura para ti; si crees que una regla está mal, dilo en el informe.
2. Etiqueta visible con mnemónico justo antes (TabIndex n, n+1) de cada cuadro, combo, lista, árbol o numérico, más `AccessibleName` explícito. Sin mnemónicos duplicados en ningún idioma.
3. **Sin `AccessibleDescription` en controles que reciben foco a menudo**: NVDA la lee en cada foco y molesta. Úsala solo donde aporte algo imprescindible y breve.
4. `AcceptButton` y `CancelButton`. Escape cierra.
5. Listas: Supr quita, Insert añade, F2 o Intro edita, menú contextual, siempre un elemento seleccionado, y tras recargar se conservan selección y foco (tras borrar, el vecino).
6. Validación: mensaje y foco al campo erróneo (cambiando de pestaña si hace falta). Cuadros cargados con todo el texto seleccionado.
7. Cambios de estado sin reflejo en el foco (activar/desactivar trigger, etc.) se anuncian por `IAnnouncer`.
8. Todo texto visible sale de los `.resx` (inglés y español). **No uses `RichTextBox`** en diálogos (ver las trampas en 04_ESTADO.md); para texto multilínea usa `TextBox` con `Multiline`.
9. Nada depende del ratón ni del color.

## Cadenas localizadas

`tools/strings.ps1` (dot-source) y `Add-OmStrings UI @(@('Clave','English','Español'), ...)`. Está protegido con un cerrojo global: varios bloques pueden usarlo a la vez. **Nunca edites los `.resx` ni `Strings.Designer.cs` a mano.** Prefijo de claves por formulario (`Options_`, `Trigger_`, `Launcher_`…). Ojo con PowerShell: pon las cadenas entre comillas simples y evita secuencias como `\n` dentro; si el guardián de comandos rechaza una llamada larga, escribe un fichero `.ps1` en `tools/` con el lote, ejecútalo y bórralo.

## Trabajo en paralelo

- Respeta la propiedad de ficheros de tu encargo. `ServiceConfigurator.cs`, `SessionDialogs.cs`, `GameWindowFactory.cs` y `AccessibilityAudit.cs` no se tocan: el cableado final lo hace el integrador. Si cambias el constructor de un formulario existente, **conserva un constructor compatible con el anterior** para que la solución compile siempre.
- Compila y prueba con tu propia carpeta de artefactos y filtrando a tus tests:
  `dotnet test tests/Omnimud.UI.Tests --artifacts-path <scratch>/<bloque> --filter "FullyQualifiedName~<TuClaseDeTests>"`
- Si falla la compilación por un fichero ajeno, espera un minuto y reintenta; no lo arregles.
- Los tests de formularios se ejecutan con `Sta.Run(...)`. Nada de diálogos modales sin contestar en tests (cuelgan): para eso existe la interfaz de avisos.
- No hay git. No borres ficheros ajenos. No abras ventanas que roben el foco al usuario más de lo imprescindible.

## Entrega

Informe final en español, máximo 50 líneas: clases y constructores (para cablear la DI), decisiones, número de tests y resultado de la última ejecución, pendientes.

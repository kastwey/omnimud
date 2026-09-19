# Auditoría de diálogos secundarios — OmniMud legacy (`trunk\cliente`)

Ámbito: todos los formularios excepto la ventana de juego (FrmCliente/UcClient). Auditoría de solo lectura.
Rutas relativas a `C:\projects\OMnimud\trunk\cliente\`. Las citas son `fichero:línea`.
Los ficheros fuente están en Latin-1 (ISO-8859-1); los textos con tilde se han transcrito tras convertir a UTF-8.

## 0. Hechos generales previos

- **Ningún Designer usa `resources.ApplyResources`** (0 coincidencias). Todos los textos visibles están en el `.Designer.cs` o se asignan en el `.cs`. Los `.resx` solo contienen `$this.Icon` (13 formularios), `$this.BackgroundImage` (FrmCliente), `$this.Headers` (CSoundDownloader) y restos de plantilla (`Bitmap1/Color1/Icon1/Name1`). No hay localización.
- Todos los formularios de gestión usan fondo `Properties.Resources.fondograde2`, etiquetas en blanco y negrita sobre fondo transparente, `StartPosition = CenterScreen`.
- **Patrón de navegación "ocultar dueño / mostrar hijo"**: casi todos los diálogos se abren con `hijo.Owner = this; hijo.Show(); this.Hide();` y en `FormClosed` hacen `Owner.Show()` + `Dispose` + `GC.Collect()`. Es decir, NO son modales: solo hay una ventana visible a la vez. Excepciones modales (`ShowDialog`): FrmAddEditMud, FrmAddEditAlias, FrmPersonalInfo, FrmFindTextBox, InputBox.
- Carácter prohibido universal: **`æ`** (`CRegistro.REG_SEPARATOR`), porque se usa como separador en el almacenamiento.
- Diálogos de fichero comunes (`CXmlOperations.cs`):
  - Importar (`:23-40`): `OpenFileDialog`, `CheckFileExists`, `DefaultExt="*.oxf"`, filtro `"Ficheros xml de Omnimud (*.oxf)|*.oxf"`, sin multiselección.
  - Exportar (`:47-61`): `SaveFileDialog`, `OverwritePrompt=true`, filtro `"Ficheros xml de omnimud (*.oxf)|*.oxf"`, `DefaultExt="*.oxf"`, `AddExtension`, `ValidateNames`.
- Proyecto: `omnimud.csproj` compila todos los Frm* incluidos FrmMap y FrmPrueba; **`Form1.cs` no está en ningún csproj** (ni en `omnimud.csproj` ni en `tiflomud.csproj`): es código muerto del prototipo "TifloMud".

---

## 1. FrmPrincipal — pantalla de entrada

**Título**: `"OmniMud"` (`FrmPrincipal.Designer.cs:130`). `MaximizeBox=false`. Se lanza desde `Program.cs:47` (`Application.Run(new FrmPrincipal())`).

### Controles (orden de tabulación)
Dentro de `gbAdministracion` (GroupBox sin texto, `AccessibleName=" "`, `TabStop=false`, `Designer:104-117`):

| Tab | Control | Tipo | Texto | Accessible |
|---|---|---|---|---|
| 0 | BtnConectar | Button | `&Conectar con...` | Name/Description `"Conectar con..."` (`:42-52`) |
| 1 | BtnMuds | Button | `&Muds` | `"Muds"` (`:58-67`) |
| 2 | BtnPersonajes | Button | `&Personajes` (tiene `DialogResult.Cancel`, `:76`) | `"Personajes"` |
| 3 | BtnSalir | Button | `&Salir` | `"Salir"` (`:89-98`) |

No hay AcceptButton ni CancelButton.

### Menú principal (construido en código, `FrmPrincipal.cs:131-188`)
- `&Archivo` → `&Salir`.
- `&Herramientas` (orden real de inserción, `:177-184`): `&Comprobar actualizaciones`, `Comprobar act&ualizaciones al iniciar` (con marca de verificación), `&Muds` (se añade dos veces, `:179` y `:181`; WinForms lo recoloca, queda una vez), `&Conectar con...` (submenú con un ítem por personaje predeterminado `Nombre (Mud)`, o `"No hay personajes predeterminados"`, `:146-161`), `&Personajes`, `&Opciones globales`, `&Actualizar tu información personal...`.
- `Ay&uda` (`CHelpManager.CreateHelpMenu`, `CHelpManager.cs:21-49`): `&Acerca de...`, `&Manual de usuario` (**F1**, abre `omnimud.chm`; si falta: "No se encontró el fichero de ayuda."), `Omnimud en la web` → `Página principal de OMnimud` / `Lista de distribución` / `Enviar e-mail al autor`; `&Enviar errores o sugerencias` → `Enviar &sugerencia` / `Enviar &error` (abren FrmReports). "Acerca de" es un MessageBox: `"OmniMud, versión X.\nJuan José Montiel Pérez.\ninfo@omnimud.org\nwww.omnimud.org"` (`:127`).
- Al desplegar Herramientas (`MnuHerramientas_Popup`, `:279-293`): sincroniza la marca de "comprobar al iniciar"; deshabilita Personajes y Conectar si no hay personajes.

### Arranque (`FrmPrincipal_Load`, `:126-221`)
1. Construye menús.
2. `if (CCheckUpdates.GetCheckUpdates()) CCheckUpdates.CheckUpdates();` (`:189`). Ajuste guardado en `CRegistro.SaveSetting("", "CheckUpdates", ...)`; **por defecto false** (`CCheckUpdates.cs:111-123`). La comprobación (`CCheckUpdates.cs:38-96`): solo si existe `autoupdater.exe`; descarga `http://www.omnimud.org/omnimud.xml` (respetando proxy de opciones globales) a `omnupd.xml`, lee `//version` y `//priority`, compara versiones quitando puntos. Mensajes: `"¡Ya tienes la última versión disponible del Omnimud!"` (solo en comprobación manual), `"Existe una nueva versión de Omnimud (X). La prioridad de la actualización es P. ¿Deseas descargarla ahora?"` (Sí → lanza `autoupdater.exe` con argumentos y `Application.Exit()`), y en error `"Error al comprobar si existen actualizaciones. Error: ... Tipo: ..."`.
3. Información personal (`:191-203`): si no hay `PersonalInfo` y no existe el ajuste `PersonalInfo/DontAskmeAgain`, pregunta (Sí/No, icono Warning, título "Información para los informes de error"): "¡Atención! Para tu comodidad, se ha implementado un sistema que guarda automáticamente tu nombre y dirección de correo electrónico... ¿Deseas actualizar tu información personal ahora?". No → informa "Podrás actualizarla manualmente, desde herramientas / Actualizar mi información personal..." y guarda `DontAskMeAgain=yes`. Sí → abre FrmPersonalInfo modal.
4. Huevo de pascua personal (`:204-220`): solo si existe `dedicatoria.mp3` en la carpeta de la app; mensajes de cumpleaños (25/5) y aniversario (día 27) y reproduce el mp3. No debe portarse.

### `Activated` (`:60-98`)
Refresca el menú contextual del botón Conectar con los personajes predeterminados (uno por MUD); deshabilita el botón si no hay. Reasigna Text/AccessibleName/AccessibleDescription a "Conectar con..." (`:80-81`). Si no hay ítems: `"No existen personajes predeterminados"`.

### Acciones
- BtnConectar: muestra `ContextMenu` en la posición del botón (`:117-120`). Al elegir personaje: crea `FrmCliente(mud, character)`, `Show()`, `Connect()`, oculta principal (`:100-115`).
- BtnMuds → FrmMuds; BtnPersonajes → FrmCharacters; Opciones globales → `new FrmOptions()` (nivel Global) (`:261-267`).
- **DEFECTO EN TRUNK**: `BtnSalir_Click` (`:33-41`) dice por voz "¡Holaaa cuñao!" y hace `return` antes de `this.Close()`. En trunk **el botón y el menú Salir no cierran la aplicación** (código de prueba de NVDA olvidado). En `branches\1.2` y en el tag está correcto.

---

## 2. FrmMuds — "Lista de muds"

**Título**: `"Lista de muds"` (`FrmMuds.Designer.cs:185`). Se abre desde FrmPrincipal (botón `&Muds` o Herramientas → Muds), sin parámetros. `AcceptButton = BtnConectar` (`:167`), `CancelButton = BtnCerrar` (`:171`).

| Tab | Control | Tipo | Texto | Notas |
|---|---|---|---|---|
| 0 | LblMuds | Label | `&Muds:` | AccName/Desc "Muds:" |
| 1 | ListMuds | ListView (Details, `MultiSelect=false`, `ShowGroups=false`) | columnas "Nombre", "Dirección", "Puerto" (ancho 100, creadas en Load `FrmMuds.cs:122-139`) | AccName/Desc "Muds:" |
| 2 | BtnAgregar | Button | `&Agregar...` | |
| 3 | BtnEditar | Button | `&Editar...` | |
| 4 | BtnQuitar | Button | `&Quitar` | |
| 5 | BtnConectar | Button | `C&onectar` | por defecto (Enter) |
| 6 | BtnExportar | Button | `E&xportar` | abre menú contextual |
| 7 | BtnImportar | Button | `Impo&rtar...` | |
| 8 | BtnCerrar | Button | `&Cerrar` | Escape |

Todos los botones tienen AccessibleName = AccessibleDescription = texto sin `&`.

**Teclado / menús** (`FrmMuds.cs:19-36`): menú contextual de la lista con `&Editar` (**Ctrl+D**) y `&Quitar` (**Supr**). Doble clic de ratón = Conectar (`:281-285`). Enter = Conectar (AcceptButton). Clic en cabecera ordena (ListViewColumnSorter sin persistencia, `:34`, `:385-389`). No hay Insert.

**Estado**: sin muds → Editar, Quitar, Conectar, Exportar deshabilitados (`:64-71`). Tras rellenar selecciona y enfoca el primer ítem o el que tenía el ID indicado (`:87-112`).

**Borrado** (`:196-230`): `"¿Estás seguro de que deseas eliminar X?\n¡Importante! Si borras este mud de la lista de muds existentes, cualquier personaje asociado al mismo será también eliminado.\n"`, título "omnimud", Sí/No. Error: `"Error al borrar X.\n"`. Tras borrar selecciona el ítem de la misma posición (o el último).

**Conectar** (`:263-279`): `new FrmCliente(UnMud)` (sin personaje), Show, Hide, `Connect()`.

**Exportar** (menú del botón, `:140-158`): `&Mud seleccionado` → `Mud completo (incluyendo personajes)...` / `Mud &solo (sin personajes)...`; `&Todos los muds almacenados` → `Muds completos (incluyendo personajes)...` / `Muds &solos (sin personajes)...`. Títulos de diálogo: "Exportar mud completo", "Exportar mud simple", "Exportar muds completos" (este último se usa también para "solos", `:167`). Errores: "Error al exportar el mud completo: …", "Error al exportar el mud simple: …", "Error al exportar los muds: …".

**Importar** (`:287-323`): diálogo "Importar muds"; si el XML tiene raíz `/muds` importa colección, si no un único mud. Errores: `"El fichero de muds no es válido. Error: …."`, `"Error al importar el mud: …."`. **Duplicados** (`CMuds.cs:291-306`): "Ya existe un mud llamado X. ¿Deseas sobreescribirlo?"; si Sí y tiene personajes, segunda confirmación ("Si sobreescribes este mud … dichos personajes se perderán. ¿Seguro que deseas continuar?"); luego borra el existente y añade el nuevo con sus opciones, movimientos, diccionario de direcciones y personajes.

---

## 3. FrmAddEditMud

**Título**: "Agregar nuevo mud" / "Editando {nombre}" (`FrmAddEditMud.cs:33`, `:41`). Modal (`ShowDialog`) desde FrmMuds. `AcceptButton=BtnAceptar`, `CancelButton=BtnCancelar` (`Designer:256-260`).

| Tab | Control | Texto / etiqueta | MaxLength | Accessible |
|---|---|---|---|---|
| 0/1 | LblNombre / TxtNombre | `&Nombre (*):` | 255 | Name+Desc "Nombre (*):" |
| 2/3 | LblHost / TxtHost | `&Dirección (IP / dominio) (*):` | 255 | Name+Desc |
| 4/5 | LblPuerto / TxtPuerto | `&Puerto (*):` | 5 | Name+Desc |
| 6/7 | LblSaveCommand / TxtSaveCommand | `Comando para sa&lvar la partida:` | 255 | Name+Desc |
| 8/9 | LblQuitCommand / TxtQuitCommand | `Comando para &salir del mud` | 255 | Name+Desc |
| 10/11 | LblRules / CmbRules (DropDownList) | `D&irectivas de procesamiento` | — | solo Description |
| 12/13 | LblDirectory / TxtSound | `&Directorio de sonidos` | (sin límite) | solo Description |
| 14 | BtnExaminar | `&Examinar...` | | solo Description |
| 15 | BtnAceptar | `&Aceptar` | | |
| 16 | BtnCancelar | `&Cancelar` | | |

Problema: mnemónico **&D duplicado** (Dirección y Directorio de sonidos).

**Combo Directivas** (`:128-145`): "ninguna" + nombres de `CProcessRules.GetProcessRules()` (plugins IRule: Balzhur, Callandor, Simauria y en trunk Cyberlife). En edición selecciona la regla con inicial en mayúscula.

**Validación** (`:61-126`), errores acumulados en un solo MessageBox "¡Error!":
- "Debes introducir el name del mud." / "El name del mud debe tener entre 3 y 255 caracteres." (la palabra "name" es un reemplazo global accidental de "nombre" en el código original; aparece en muchos mensajes).
- "Debes introducir la IP o el name de dominio para este mud."
- "El número de puerto no es válido." / "El puerto debe estar comprendido entre 1 y 99999." (valida 0–99999 en realidad).
- Edición: "Ya existe un mud con el nuevo name especificado para X." (foco y SelectAll en nombre); "El directorio de sonidos especificado no existe. Asegúrate de haberlo escrito correctamente." (solo se valida al editar); "Error al actualizar la información sobre este mud."
- Alta: "Error al introducir el nuevo mud." **No se comprueba duplicado de nombre al agregar** en el formulario.
- Examinar: `FolderBrowserDialog` "Directorio de sonidos", raíz Escritorio, ruta inicial = carpeta de la app; "El directorio seleccionado no existe."
- En edición se hace `SelectAll()` de todos los TextBox (`:48-51`).

---

## 4. FrmCharacters — "Personajes"

**Título**: "Personajes" (`Designer:197`), AccessibleName/Description del formulario "Personajes". `MaximizeBox/MinimizeBox=false`. `AcceptButton=BtnConectar`, `CancelButton=BtnCerrar`. Desde FrmPrincipal.

| Tab | Control | Tipo | Texto |
|---|---|---|---|
| 6 | LblCharacters | Label | `&Personajes:` |
| 7 | ListCharacters | **TreeView** (nodos raíz = MUDs con inicial mayúscula; hijos = personajes; el predeterminado lleva sufijo `" (predeterminado)"`, `FrmCharacters.cs:74-93`) | Acc "Personajes" |
| 8 | BtnAgregar | Button | `&Agregar...` |
| 9 | BtnEditar | Button | `&Editar...` |
| 10 | BtnQuitar | Button | `&Quitar` |
| 11 | BtnPredeterminado | Button | `&Marcar como predeterminado` |
| 12 | BtnConectar | Button | `C&onectar` |
| 13 | BtnExportar | Button | `E&xportar...` |
| 14 | BtnImportar | Button | `Impo&rtar...` |
| 15 | BtnCerrar | Button | `&Cerrar` |

**Menú contextual del árbol** (`:116-131`): `&Agregar nuevo personaje`, `&Editar`, `&Quitar` (**Supr**), `E&xportar...`.
**Estado** (`CheckButtonsState` `:36-52`, `AfterSelect` `:210-228`): sin muds todo deshabilitado; en un nodo con hijos (MUD con personajes) se deshabilitan Editar/Quitar/Conectar/Exportar/Predeterminado; Predeterminado se deshabilita si el personaje ya lo es. Nota: un MUD sin personajes (nodo sin hijos) deja los botones activos; los manejadores se protegen con `it.Parent == null`.
**Doble clic**: el manejador `ListCharacters_MouseDoubleClick` existe (`:282-285`) pero **no está enganchado en el Designer** (solo `AfterSelect`, `Designer:170`) → no funciona; Enter sí conecta.
**Quitar** (`:177-208`): "¿Confirmas que deseas eliminar el personaje X del mud Y de la lista de personajes?" (título "Pregunta"). Error: "Error al borrar el personaje de la lista."
**Predeterminado** (`:230-259`): uno por MUD; actualiza el texto de los nodos.
**Conectar**: `new FrmCliente(mud, character)`, Show, Connect, Hide.
**Exportar**: diálogo "Exportar personaje", exporta completo (opciones, alias, paths, triggers, movimientos). Error: "Error al exportar el personaje X: …."
**Importar**: diálogo "Importar personaje". **Duplicados** (`CCharacters.cs:196-210`): si el MUD del fichero ya existe se reutiliza; si el personaje existe: "Ya existe un personaje llamado X en el mud especificado. ¿Estás seguro de que deseas sobreescribirlo? Importante: Al sobreescribirlo, se sobreescribirá tanto la información del personaje como las opciones, alias, paths, triggers, y teclas de movimiento." Si el MUD no existe y viene en el XML, se crea. Error: "Error al importar el personaje: …."
Agregar/Editar abren FrmAddEditCharacter **no modal** (Hide/Show).

---

## 5. FrmAddEditCharacter

**Título**: "Agregar personaje" / "Editando {nombre}". `AcceptButton=BtnAceptar`, `CancelButton=BtnCancelar`.

| Tab | Control | Texto | Notas |
|---|---|---|---|
| 0/1 | LblNombre / TxtNombre | `&Nombre del personaje:` | MaxLength 30; AccName del cuadro "Nombre:" |
| 2 | ChkRecordar | `&Recordar contraseña` | marcada por defecto; al desmarcar deshabilita etiqueta y cuadro de contraseña (`.cs:146-158`) |
| 3/4 | LblPassword / TxtPassword | `&Contraseña: ` | MaxLength 30, `UseSystemPasswordChar=true` |
| 5/6 | LblMuds / CmbMuds | `&Mud:` | DropDownList, `Sorted=true`, nombres de todos los muds |
| 7 | BtnAceptar | `&Aceptar` | (Designer le pone `DialogResult.Cancel`, `:126`) |
| 8 | BtnCancelar | `&Cancelar` | **&C duplicado** con Contraseña |

**Validación** (`.cs:70-144`): "Debes introducir el name del personaje."; "Debes introducir la contraseña del personaje, o bien, desverificar la casilla de recordar contraseña."; foco al campo erróneo. El nombre se pasa a **minúsculas** (`:99`). Duplicado: "El name de este personaje ya existe en {mud}." / "El name de este personaje ya existe." Errores de guardado: "Error al añadir al personaje." / "Error al actualizar los valores del personaje." Contraseña cifrada con `CEncriptar.CryptString`. Defecto: al editar con "recordar" desmarcado se guarda igualmente el texto del cuadro (`:135`), no cadena vacía.

---

## 6. FrmAliases — "Alias"

**Título**: "Alias". Desde FrmCliente → Herramientas → `&Alias` (**F5**, `FrmCliente.cs:378,392,884-893`); requiere personaje ("Debes haber iniciado sesión con un personaje para utilizar esta funcionalidad."). Parámetro: `CCharacter`. Sin AcceptButton; `CancelButton=BtnCerrar`. Min/Max deshabilitados.

| Tab | Control | Texto |
|---|---|---|
| 0 | LblAlias | `&Alias:` |
| 1 | LstAliases | ListView Details, columnas "Comando" y "Acción", `MultiSelect=false` |
| 2 | BtnAgregar | `A&gregar` |
| 3 | BtnQuitar | `&Quitar` |
| 4 | BtnEditar | `&Editar` |
| 5 | BtnExportar | `E&xportar...` |
| 6 | BtnImportar | `Im&portar...` (abre menú) |
| 7 | BtnCerrar | `&Cerrar` |

Aquí la mayoría de controles solo tienen `AccessibleDescription` (sin AccessibleName), salvo Exportar/Importar.

**Menú contextual** (`.cs:80-94`): `&Agregar nuevo alias`, `&Quitar` (**Supr**), `&Editar`. No hay Enter ni doble clic para editar.
**Ordenación**: clic en cabecera; **se persiste por personaje** en `characters\{ID}` valor `AliasOrder` como `"columna;orden"` (`.cs:122`, `ListViewColumnSorter.cs:38-73,129-164`).
**Quitar**: "¿Estás seguro de que deseas eliminar este alias?" (Pregunta, Sí/No). Error "No se pudo eliminar el alias."
**Exportar**: "Exportar alias" → `.oxf`. Error "Error al exportar los alias: …".
**Importar** (menú del botón): `Desde &fichero...` y `Desde otro & personaje` → submenú con todos los demás personajes `Nombre (Mud)` o "No hay más personajes".
 - Desde fichero (`CAlias.cs:88-125`): por cada duplicado "Ya existe un alias llamado X. ¿Deseas sobreescribirlo?"; resumen "N alias exportados." (sic). Errores: "Error al guardar los nuevos alias.", "Error el el fichero de alias: …", "Error: …".
 - Desde personaje (`.cs:246-290`): "X no tiene alias."; por duplicado: "Ya existe un alias con el name C. Acción del alias actual: A. Acción del nuevo alias: B. ¿Deseas sobreescribirlo?"; resumen "N alias importado(s)"; error "Imposible guardar los nuevos alias."
Al cerrar actualiza `FrmCliente.Aliases`.

## 7. FrmAddEditAlias

**Título**: "Crear nuevo alias" / "Editar alias". Modal. `AcceptButton=BtnAceptar`, `CancelButton=BtnCancelar`. Min/Max off.

| Tab | Control | Texto | MaxLength |
|---|---|---|---|
| 0/1 | LblComando / TxtComando | `&Comando` | 255 (una línea, scroll horizontal, sin WordWrap) |
| 2/3 | LblAccion / TxtAccion | `&Acción` | 500 |
| 4 | BtnAceptar | `&Aceptar` (**&A duplicado** con Acción) | |
| 5 | BtnCancelar | `Ca&ncelar` | |

Solo AccessibleDescription. **Validaciones** (`.cs:60-171`): "El campo del comando debe contener texto."; "No se admiten espacios en el comando para el alias."; "Has introducido el único carácter no válido para los alias en el OmniMud, el síbmolo de AE (æ)."; "El campo de la acción del alias no debe quedar vacío."; "El comando y la acción equivalente son iguales. NO es preciso crear un alias para esto."; "Ya existe un alias con el comando especificado."; aviso Sí/No: "Ya existe un alias para esta misma acción, asociado al comando X.\n¿Deseas añadirlo de todos modos?"; "Error al modificar el alias."; "No se ha podido agregar el alias." Foco al campo culpable. Sin cambios en edición → cierra como Cancel.

---

## 8. FrmTriggers — "Triggers"

**Título**: "Triggers" (AccessibleDescription del form "Triggers"). Desde FrmCliente → Herramientas → `&Triggers` (**F6**); requiere personaje ("¡No puedes utilizar esta funcionalidad si no has iniciado sesión con un personaje previamente almacenado!"). `CancelButton=BtnCerrar`, sin AcceptButton.

| Tab | Control | Texto |
|---|---|---|
| 0 | LblTriggers | `&Triggers:` |
| 1 | LstTriggers | ListView Details: columnas "Nombre", "Estado" ("Activado"/"Desactivado") |
| 2 | BtnAgregar | `&Agregar` |
| 3 | BtnQuitar | `&Quitar` |
| 4 | BtnEditar | `&Editar` |
| 5 | BtnEstado | `Desac&tivar` inicial; en ejecución alterna `Desact&ivar` / `Act&ivar` (y AccessibleDescription "Desactivar"/"Activar") (`.cs:296-297,368-369`) |
| 6 | BtnExportar | `E&xportar...` |
| 7 | BtnImportar | `&Importar` (menú) — **&I choca** con Act&ivar |
| 8 | BtnCerrar | `&Cerrar` |

**Teclado**: **Barra espaciadora** en la lista = activar/desactivar (`LstTriggers_KeyPress`, `.cs:372-376`), y **lo anuncia por voz**: `CSintetizer.SayText("Activado"/"Desactivado", false, _options.ScreenReader)` (`:298`). Menú contextual: `&Agregar trigger`, `&Quitar` (**Supr**), `&Editar`, y el ítem de estado (texto dinámico). Ordenación por columna persistida en `characters\{ID}` / `TriggersOrder`.
**Quitar** (`:318-356`): "¿Estás seguro de que deseas eliminar este trigger?"; si es C#/VB borra la DLL compilada `{GetUserTriggerPath}\{IDpersonaje}_{guid}.dll` ("Error al borrar el fichero dll de la compilación de este trigger. Deberás borrar a mano el fichero …"); "¡Error al eliminar el trigger!".
**Exportar**: "Exportar triggers". **Importar**: `Desde &fichero...` / `Desde otro &personaje` (submenú, o "No hay otros personajes actualmente").
 - Duplicados: "Ya existe un trigger llamado X. ¿Deseas sobreescribirlo?".
 - **Seguridad de triggers con código** (C#/VB), tanto desde fichero (`CTrigger.cs:155-168`) como desde personaje (`.cs:157-170`): sin firma → "El trigger X, no tiene la firma de autenticación de trigger seguro. Ejecuta triggers únicamente de fuentes de absoluta confianza… ¿Estás seguro de que deseas incluir este trigger?"; firma inválida → "¡El trigger X, tiene una firma de autentificación no válida! … ¿Deseas guardar este trigger?" (título "¡Atención! ¡Trigger manipulado!"), y si acepta se elimina la firma.
 - Resúmenes: "N triggers importados." / "trigger importado"; errores "Error al grabar los nuevos triggers.", "Error al importar los triggers desde el fichero OXF: …", "Error al almacenar los nuevos triggers.", "X (Mud), no tiene triggers almacenados actualmente."
Agregar/Editar abren FrmAddEditTrigger no modal. Defecto: `RefillList` hace cast directo `(FrmCliente)Owner` (`:222`).

## 9. FrmAddEditTrigger

**Título** = AccessibleDescription: "Agregar nuevo trigger" / "Editar trigger" (`.cs:37,46`). `FixedDialog`, Min/Max off. `AcceptButton=BtnAceptar`, `CancelButton=BtnCancelar`. Ningún TextBox tiene MaxLength.

| Tab | Control | Texto | Notas |
|---|---|---|---|
| 0/1 | LblName / TxtName | `&Nombre:` | una línea |
| 2/3 | LblHappen / TxtHappen | `&Texto que desencadenará el suceso` | Multiline, AcceptsReturn, scroll ambos; AccName "Texto que desencadenará el evento" |
| 4 | ChkCaseSensitive | `&Distinguir mayúsculas de minúsculas` | |
| 5 | ChkRegExp | `&Usar expresiones regulares en el suceso` | habilita CmbRegExpType |
| 6/7 | LblRegExpType / CmbRegExpType | `T&ipo de expresión regular` | DropDownList: "Expresión regular", "Expresión de reemplazo" |
| 8/9 | LblWhat / CmbWhat | `¿&Qué hará este trigger?` | DropDownList; **AccessibleDescription = "1"** (error, `Designer:115`) |
| 10/11 | LblAction / TxtAction | `Acc&ión:` (Designer) → en ejecución `&Acción:` / `&Código c#:` / `&Código Vb.Net:` | Multiline, AcceptsReturn |
| 12/13 | LblSound / TxtSound | `&Sonido:` | |
| 14 | BtnBrowse | `&Examinar...` | |
| 15 | BtnAceptar | `&Aceptar` | |
| 16 | BtnCancelar | `&Cancelar` | |

Como TxtHappen y TxtAction tienen `AcceptsReturn`, Enter dentro de ellos inserta salto de línea (no acepta).

**CmbWhat** (`.cs:59-63`): 0 "Realizar una acción", 1 "Reproducir un sonido", 2 "Ambas cosas", 3 "Código c#", 4 "Código Vb.Net". **Cambio de UI según tipo** (`:108-153`):
- 0: deshabilita Sonido+Examinar; habilita Acción.
- 1: deshabilita Acción; habilita Sonido+Examinar.
- 2: todo habilitado.
- 3/4: deshabilita Sonido; renombra etiqueta y AccessibleName/Description del cuadro a "Código c#:" / "Código Vb.Net:"; **quita "Expresión de reemplazo"** del combo de tipo de regex. Al volver a 0-2 la reañade y restaura "Acción:" (`CambiaANormal`). Defecto: en 3/4 no se re-habilita explícitamente TxtAction si se venía del tipo 1.
**Trigger de comando**: si el texto desencadenante empieza por `@` se deshabilita y desmarca "Usar expresiones regulares" (`:397-408`); validaciones: "Si deseas asociar este trigger a un comando, debes especificar el comando…" / "…dicho comando no puede contener espacios."
**Sonido**: `OpenFileDialog` "Seleccionar archivo de sonido para el trigger", filtro `"Ficheros wav (*.wav)|*.wav|Ficheros mp3 (*.mp3)|*.mp3|Todos los archivos (*.*)|*.*"` (`Designer:225-228`). Guarda la ruta completa. **No hay botón de probar sonido**.
**No hay botón probar/compilar ni ayudas**: la compilación ocurre al pulsar Aceptar (`CTrigger.CompileCode`, `:304-314`): "Error al compilar el código introducido:\n{errores}" (título "¡Errores al compilar!") y foco al código. La regex se valida con `Regex.Match("prueba", …)` o `Regex.Replace` : "La expresión regular no está bien construida. Error: …".
**Resto de validaciones** (`:165-373`): nombre vacío; carácter æ en nombre/suceso/acción/sonido/código; "El texto que desencadenará el trigger no puede estar vacío."; "La accióndel trigger no puede estar vacía."; "Debes escribir código c#/Vb.Net para este trigger."; "El sonido que reproducirá el trigger no puede estar vacío."; "Ya existe un trigger con el name especificado."; aviso Sí/No por suceso duplicado (solo en edición): "Ya existe un trigger llamado X, que responde al suceso desencadenante…"; al cambiar código firmado: "El código de este trigger ha cambiado, por lo que la firma de autentificación que poseía ya no es válida… ¿Estás seguro…?" (título "Cambios en el código del trigger original"); si deja de ser C#/VB borra la DLL antigua; "Error al insertar el nuevo trigger." Al guardar, el trigger queda siempre **habilitado** (`true`).

---

## 10. FrmPaths — "Paths"

**Título**: "Paths". Desde FrmCliente → Herramientas → `&Paths` (**F7**); requiere personaje ("Debes haber iniciado sesión con algún personaje para utilizar esta opción."). Parámetros: `CCharacter`, `CPathDictionaryCollection`. `CancelButton=BtnCerrar`.

| Tab | Control | Texto |
|---|---|---|
| 0 | LblPaths | `&Paths` |
| 1 | LstPaths | **ListBox** `Sorted=true` (solo nombres) |
| 2 | BtnAgregar | `&Agregar` |
| 3 | BtnQuitar | `&Quitar` |
| 4 | BtnEditar | `&Editar` |
| 5 | BtnExportar | `&Exportar...` (**&E duplicado** con Editar) |
| 6 | BtnImportar | `&Importar` (menú) |
| 7 | BtnCerrar | `&Cerrar` |

Menú contextual: `&Agregar path`, `&Editar`, `&Quitar` (**Supr**). Borrado: "¿Estás seguro de que deseas borrar el path X?"; "No se pudo borrar el path seleccionado."; tras borrar devuelve el foco a la lista y selecciona el vecino (`:182-192`).
Exportar: "Exportar paths" — **defecto**: compara con `DialogResult.No` (`:289`), así que al cancelar el diálogo intenta exportar igualmente. Importar: `Desde &fichero...` / `Desde otro &personaje`. Duplicados: "Ya existe un path llamado X. ¿Deseas sobreescribirlo?" (sobrescribe solo el camino). Resúmenes: "N paths importados." / "N path(s) importado(s) satisfactoriamente."; "X (Mud), no tiene paths asociados."; errores "Error al guardar los paths importados.", "Error al importar los paths: …", "Error al almacenar los nuevos paths".

## 11. FrmAddEditPath

**Título**: "Agregar nuevo path" / "Editando el path X". No modal. `AcceptButton/CancelButton` definidos. También lo abre el comando de consola **`paths detener`** con el camino grabado precargado (`FrmCliente.cs:2518-2528`).
Controles: `&Nombre` / TxtNombre (tab 0/1), `&Camino:` / TxtCamino (2/3, una línea con scroll horizontal), `&Aceptar` (4), `Ca&ncelar` (5). Sin MaxLength en Designer (se valida en código ≤ 200).
Validaciones (`.cs:90-202`): nombre vacío; ">200 caracteres"; carácter æ en nombre o camino; "No has especificado direccioens para este path."; "Ya existe un path con el name especificado."; **"No tienes definido aún un diccionario de direcciones para los paths. Créalo desde herramientas / direcciones para los paths."**; "El path introducido no es válido. Asegúrate de haberlo escrito correctamente." (`CPath.IsValid`); el camino se guarda colapsado (`CPath.CollapsePath`); avisos Sí/No: mismo camino que otro path; "…este path no tendrá disponible la opción de revertir. ¿Deseas continuar?" si no se puede calcular el inverso.

## 12. FrmPathsDictionary — direcciones por MUD

**Título**: "Direcciones de paths para {mud}". Desde FrmCliente → Herramientas → `&Direcciones para los paths` (`FrmCliente.cs:377,772-778`). Parámetro `CMud` (**ámbito MUD**). `CancelButton=BtnCerrar`.
Controles: `&Direcciones:` (0), LstDirecciones ListView Details con columnas "Dirección", "Abreviatura", "Contraria" (1), `&Agregar...` (2), `&Editar...` (3), `&Quitar` (4), `&Cerrar` (5). AccessibleName+Description en todos. Menú contextual `&Agregar...`, `&Editar...`, `&Quitar` (**Supr**). Sin ordenación por columna. Borrado: "¿Segur oque deseas eliminar la dirección X?" (sic); "Error al eliminar la dirección especificada." Al cerrar actualiza `FrmCliente.PtdCol` (cast directo).

## 13. FrmAddEditPathDictionary

**Título**: "Agregar nueva dirección" / "Editando dirección". `AcceptButton=BtnAceptar`; **sin CancelButton** (Escape no cierra).
Controles: `&Dirección` / TxtDireccion (0/1), `A&breviatura:` / TxtAbbr (2/3, **MaxLength 1**, sin AccessibleName), `&Dirección contraria` / TxtContraria (4/5, sin AccessibleName; **&D duplicado**), `&Aceptar` (6), `&Cancelar` (7).
Validaciones acumuladas (`.cs:87-161`): "Debes especificar la dirección a añadir."; "La dirección no puede contener el caracter æ."; "La dirección abreviada debe contener un carácter."; mensaje jocoso si la abreviatura es æ; "Ya existe una dirección con la abreviatura X asociada a la dirección real Y. Si quieres modificarla, edítala."; "La dirección contraria es obligatoria."; "Error al guardar la dirección." Foco al primer campo erróneo.

## 14. FrmMovements — "Configurar teclas de movimiento"

Desde FrmCliente → Herramientas → `Configurar &teclas de movimiento` (`FrmCliente.cs:780-799`). Parámetros `(ID, MovementDeepLevel)`: **nivel Personaje** si hay personaje, si no **nivel MUD**. Si el personaje no tiene movimientos propios carga los del MUD (`.cs:29-30`). Relacionado: Herramientas → `Modo Mo&vimiento con teclado numérico` (**F2**).
Controles: `&Teclas de movimiento` (0), LstKeys ListView Details "Tecla"/"Comando" con 10 filas fijas "1".."9","0" = NumPad1..9,0 (1), `C&omando:` / TxtComando MaxLength 50 (2/3), `&Aceptar` (4), `&Cancelar` (5). Accept/Cancel definidos.
Comportamiento: seleccionar fila carga su comando en el cuadro con SelectAll; escribir actualiza la celda en vivo (`:82-95`). Aceptar guarda solo las teclas con comando; error "Error al almacenar los movimientos." Riesgo: `TxtComando_TextChanged` accede a `SelectedItems[0]` sin comprobar.

---

## 15. FrmOptions — Opciones

### Selección de ámbito
**No hay selector de ámbito dentro del diálogo**: el ámbito lo decide el constructor (`FrmOptions.cs:31-66`):
- `new FrmOptions()` → **Global**, título "Opciones globales" (desde FrmPrincipal → Herramientas → `&Opciones globales`).
- `new FrmOptions(CMud)` → **MUD**, título "Opciones para el mud X" (FrmCliente → Herramientas → `&Opciones`, **F9**, cuando se conectó sin personaje).
- `new FrmOptions(CCharacter)` → **Personaje**, título "Opciones para X (Mud)" (mismo menú, con personaje) (`FrmCliente.cs:839-847`).
Se guardan con `SetGlobalOptions` / `SetMudOptions` / `SetCharacterOptions` (`:275-280`) y se actualiza `FrmCliente.Options`. No se puede editar las opciones de un MUD/personaje sin estar conectado a él. **No existe "restaurar valores por defecto"** ni indicación de herencia.

### Estructura
`TabControl TbPestanas` con 6 pestañas. **Cada pestaña repite sus propios botones** Exportar, Importar, Aceptar y Cancelar, todos enlazados a los mismos manejadores; al cambiar de pestaña se reasignan `AcceptButton`/`CancelButton` a los de la pestaña activa (`:324-356`). Aceptar valida y guarda TODO; Exportar/Importar operan sobre el conjunto completo de opciones, no por pestaña.

**Pestaña "General"** (`Designer:147-349`)
| Tab | Control | Texto |
|---|---|---|
| 0 | ChkConfirm | `&Confirmar al desconectar` |
| 1 | ChkTrySave | `&Intentar abandonar la partida antes de desconectar` |
| 2/3 | LblScreenReader / CmbScreenReaders | `&Lector de pantalla utilizado`: "Ninguno", "JAWS", "Window Eyes", "NVDA", "Autodetectar" |
| 4/5 | LblPositionReceived / CmbPositionReceived | `M&ovimiento del cursor al recibir datos en Recibidos`: "Ir siempre al final del texto", "Mantener donde esté", "Dependiendo de la posición del cursor" |
| 6/7 | LblPositionMessage / CmbPositionMessage | `Movimiento del c&ursor al recibir datos en Mensajes` (mismos 3 valores) |
| 8/10 | LblLastCommands (AccessibleRole=None) / TxtLastCommands (**NumericUpDown**, valor inicial 10, AccessibleDescription vacía) | `Nú&mero de últimos comandos almacenados` |
| 11 | ChkNegotiate | `&Habilitar la negociación Telnet` |
| 12 | BtnExportarGeneral | `E&xportar...` |
| 13 | BtnImportarGeneral | `Impo&rtar...` |
| 14 | btnAceptarGeneral | `&Ac&eptar` (errata: dos mnemónicos) |
| 15 | btnCancelarGeneral | `Ca&ncelar` |

**Pestaña "Logs"**: `&Guardar logs:` / CmbLogsType ("No guardar", "Por día", "Por partida") (0/1); `&Guardar logs en:` / TxtLogsDir (2/3) (**&G duplicado**); `&Examinar...` (4; FolderBrowserDialog "Directorio de almacenamiento de logs"); Exportar (5), Importar (6), `&Aceptar` (7), `Ca&ncelar` (8). "No guardar" deshabilita ruta y Examinar (`:358-362`).

**Pestaña "Sonidos"**: `Habilitar &sonidos (MSP)` (0), `Habilitar &música (MSP)` (1), `Reproducir s&onidos en segundo plano` (2), `Reproducir mús&ica en segundo plano` (3), `&Descargar sonidos desde internet` (4), Exportar (5), Importar (6), Aceptar (7), Cancelar (8).

**Pestaña "Conexión"**: `&Usar un servidor proxy:` / CmbUseProxy ("No usar", "Detectar automaticamente", "Introducir manualmente") (0/1); `&Host del proxy:` / TxtProxyHost MaxLength 255 (2/3); `&Puerto del proxy` / TxtProxyPort MaxLength 5, solo dígitos por KeyPress, **sin AccessibleName** (4/5); `E&xportar...` (6), `Impo&rtar` (7), `&Aceptar` (8), `&Cancelar` (9). Host/puerto solo habilitados en modo manual (`:387-397`).

**Pestaña "Apariencia"**: `Fuente:` (sin mnemónico) / TxtFuente **ReadOnly** con "Nombre: tamaño" (0/1); `&Cambiar...` (2; `FontDialog` sin color/efectos/script); `E&xportar` (3), `Impo&rtar` (4), `&Aceptar` (5), `&Cancelar` (6) (**&C duplicado**).

**Pestaña "Caracteres especiales"**: ChkCharConcat `&Usar carácter especial para concatenar comandos` (0); `Carácter de c&oncatenación:` / TxtCharConcat MaxLength 1 (1/2); ChkCharRepeat (3) — **su texto visible está mal**: dice `U&sar carácter especial para concatenar comandos` pero su AccessibleName es "Usar carácter especial para la repetición de comandos" (`Designer:952-958`); `Carácter de re&petición de comandos:` / TxtCharRepeat MaxLength 1 (4/5; si se teclea un dígito suena beep y se borra, `:468-481`); `E&xportar` (6), `Impo&rtar...` (7), `Aceptar` (8, sin mnemónico), `&Cancelar` (9).

Accesibilidad: todas las TabPage y controles llevan AccessibleDescription; AccessibleName solo desde "Conexión" en adelante y en botones Exportar/Importar. Hay **controles huérfanos** en el Designer (button1-8, textBox1-4, label1-6, comboBox1-2, `:1009-1234`) que no se añaden a ningún contenedor (restos de copiar la pestaña Conexión).

### Validaciones al Aceptar (`:196-289`)
"El número de comandos a almacenar en el histórico no puede ser menor a uno."; "Debes introducir un host para el proxi."; "Debes especificar un número de puerto válido."; "El rango de puerto no es válido. Debe estar comprendido entre 1 y 65535."; "No hay introducida ninguna ruta en la que guardar los logs." (intenta seleccionar la pestaña Logs); "El directorio de almacenamiento de logs no existe. ¿Deseas crearlo?" (Sí → lo crea; "Error al crear el directorio: …"); "Debes especificar el carácter de concatenación de comandos"; "Debes especificar el carácter para la repetición de comandos."; "El carácter de concatenación de comandos y de repetición no pueden ser el mismo."; "¡Error al guardar las opciones! Por favor, ponte en contacto conmigo en 'info@omnimud.org'…".
Defecto: los errores de otras pestañas ponen el foco en un control de una pestaña no visible (solo el de logs intenta cambiar de pestaña, y con `TPLogs.Select()` que no cambia la pestaña activa).

### Exportar / Importar (`:399-441`)
Exportar: "Exportar opciones" → `.oxf`; exporta las opciones **guardadas** del ámbito actual (no lo que hay en pantalla). Importar: "Importar fichero de opciones"; carga el fichero, conserva el nivel actual, y vuelve a llamar a `InicializaOpciones()` → rellena la pantalla (no guarda hasta Aceptar). **Defecto**: `InicializaOpciones` no vacía los combos, así que tras importar los ítems de los 5 combos se duplican. Errores: "Error al extraer las opciones del fichero OXF especificado. Error: …", "Error al exportar las opciones: …".

---

## 16. FrmPersonalInfo — "Ajustes de la información personal"

Modal desde FrmPrincipal (menú y pregunta de arranque). `&Nombre:` / TxtNombre MaxLength 255 (0/1); `Email:` (sin mnemónico) / TxtEmail (2/3); `&Aceptar` (4); `&Cancelar` (5). Accept/Cancel definidos; AccessibleName+Description completos. Validaciones: "Debes escribir el nombre."; "El nombre debe estar comprendido entre 3 y 255 caracteres."; "Debes introducir tu dirección de correo electrónico."; "La dirección de correo electrónico tiene un formato incorrecto."; "Error al almacenar tu información personal." Genera/conserva un GUID de usuario.

## 17. FrmReports — enviar error / sugerencia

Desde Ayuda → Enviar errores o sugerencias (en FrmPrincipal y FrmCliente). Parámetro `ReportTypes.Bug|Suggestion`; cambia título ("Enviar error"/"Enviar sugerencia"), etiqueta (`&Error (*):` / `&Sugerencia (*):`) y botón (`&Enviar error` / `&Enviar sugerencia`), con sus AccessibleName (`.cs:37-63`). No modal (método propio `Show(Form own)`).
Controles: `&Nombre (*):` / TxtNombre 255 (0/1), `&E-mail (*):` / TxtEmail 255 (2/3), LblReport / TxtReport multilinea con AcceptsReturn, **sin AccessibleName** (4/5), BtnEnviar (6), `&Cancelar` (7). Sin AcceptButton; CancelButton definido. **&E duplicado** (E-mail / Error / Enviar).
Foco inicial inteligente (`:205-225`): precarga nombre y e-mail de la info personal y pone el foco en el primer campo vacío (directamente en el informe si ambos existen).
Validaciones acumuladas: "El nombre es obligatorio.", "El e-mail es obligatorio.", "El e-mail tiene un formato incorrecto…", "Debes introducir el error/la sugerencia." Envío por servicio web SOAP `OmnimudReporting.Reporting` (SendError/SendSuggestion) con versión, fecha, GUID y SO. Resultado: "Error enviado satisfactoriamente. El identificador para tu error es: N…" / "Sugerencia enviada satisfactoriamente…"; errores de red. El servicio ya no existirá: a sustituir.

## 18. FrmFindTextBox — "Buscar"

Modal desde FrmCliente → Edición → `&Buscar` (**Ctrl+B**; "Buscar siguiente" Ctrl+S). Parámetro: el RichTextBox activo (Recibidos o Mensajes); el llamador cambia el título a "Buscar en {AccessibleDescription del cuadro}" y precarga la última búsqueda (`FrmCliente.cs:745-769`).
Controles: `&Buscar:` / TxtBuscar MaxLength 255 (0/1); `&Distinguir mayúsculas de minúsculas` (2); GroupBox "Dirección:" (3, TabStop=false) con RadioButtons `A&bajo` (marcado por defecto) y `A&rriba`; BtnBuscar `&Buscar` (4, AcceptButton); `&Cancelar` (5, CancelButton). **&B triplicado** (etiqueta, Abajo, botón). Texto vacío → beep y no cierra. No encontrado → "No se encontró el texto buscado" (Warning). Encontrado → selecciona el texto en el RichTextBox y cierra con OK.

## 19. FrmMap — "Mapa"  (ESQUELETO)

Solo un `DataGridView DtGrid` (AccessibleName "Mapa") al que en Load se le asigna como DataSource un array de 10 cadenas "col 1".."col 10" y `EditProgrammatically` (`FrmMap.cs:18-30`). **No tiene ninguna lógica de mapa, no se instancia en ningún sitio** (solo una línea comentada en `Program.cs:48`). Prototipo abandonado: no hay nada funcional que portar.

## 20. FrmPrueba, Form1, CSoundDownloader, CInputBox, ListViewColumnSorter

- **FrmPrueba**: formulario vacío con un TabControl `TbPestanas`; Load vacío; solo referenciado en comentario (`Program.cs:46`). Banco de pruebas.
- **Form1** (namespace `tiflomud`, título "TifloMud"): prototipo inicial: `&Texto a enviar:` + TextBox, `&Recibido:` + RichTextBox ReadOnly; conecta a `localhost:8081`, lee lo recibido con `CSintetizer.JFWSayString` solo si la ventana está activa, Enter envía. No está en ningún csproj. Ya muestra el patrón etiqueta+mnemónico+AccessibleName.
- **CSoundDownloader.cs**: **no es un formulario**. Define la clase `s : WebClient` (nombre ofuscado/accidental) con metadatos de sonido MSP (SoundType, FileName, SoundPath, Name, Volume, Loop, Priority, Contin, Type, Sueno) y `CSoundDownloaderCollection.IsDownloading(name)`. El `.resx` solo guarda `$this.Headers`. Sin UI.
- **CInputBox.cs** (`InputBox.Show(title, prompt, posicion)`): diálogo construido a mano (200x120, FixedDialog, sin icono, `KeyPreview`), Label + TextBox + botones "Aceptar"/"Cancelar" **sin mnemónicos, sin AccessibleName, sin AcceptButton/CancelButton** (Enter/Escape por KeyPress). Devuelve null al cancelar. Solo se usa en `Program.SendError` para pedir e-mail y comentario del informe de error (`Program.cs:69-70`). Es el diálogo menos accesible del programa (la etiqueta se añade antes que el cuadro, eso sí).
- **ListViewColumnSorter**: comparador sin distinción de mayúsculas por la columna elegida; `ColumnClick` alterna ascendente/descendente en la misma columna o pasa a ascendente en otra; el constructor con `(sección, valor)` lee/guarda `"columna;orden"` en el registro. Usado en Muds (sin persistencia), Alias (`AliasOrder`) y Triggers (`TriggersOrder`). La ordenación solo es accesible con ratón (clic en cabecera): **no hay equivalente de teclado**.

## 21. Manejo global de errores (Program.cs)

`ThreadException` y `UnhandledException` → `SendError` (`:54-90`): pregunta si enviar informe anónimo, si añadir comentario (InputBox), envía por el servicio web; si falla muestra el texto completo con instrucciones específicas para JAWS ("control + insert + W para copiar este texto al cursor virtual"). Luego "¿Deseas que la aplicación intente continuar ejecutándose?". Comprobación de instancia única comentada.

---

## 22. Patrones de accesibilidad aplicados de forma consistente

1. **Etiqueta visible inmediatamente antes de cada control** en el orden de tabulación (Label TabIndex n, control n+1), con mnemónico `&X` en la etiqueta para saltar al control.
2. **`AccessibleDescription` (y casi siempre `AccessibleName`) igual al texto de la etiqueta, sin `&`**, en el control y en la propia etiqueta; en botones, el texto del botón. Cuando el texto cambia en ejecución se actualizan también las propiedades accesibles (BtnEstado de triggers, LblAction/TxtAction, FrmReports, BtnConectar, título de FrmFindTextBox).
3. **Mnemónicos en todos los botones** y en menús. Hay colisiones no resueltas (listadas por formulario).
4. **AcceptButton / CancelButton**: Enter = acción principal (Conectar en listas de muds/personajes, Aceptar en edición), Escape = Cerrar/Cancelar. En FrmOptions se reasignan por pestaña.
5. **Supr** para borrar vía `Shortcut.Del` en el menú contextual de cada lista (Muds, Personajes, Alias, Triggers, Paths, Direcciones); menú contextual con las mismas acciones que los botones (accesible con tecla Aplicaciones). Ctrl+D = Editar solo en Muds. Espacio = activar/desactivar en Triggers. No hay Insert para agregar ni Enter/doble clic para editar.
6. **Confirmación Sí/No** antes de cada borrado y sobrescritura; mensajes de error con título "¡Error!" e icono de error; resumen numérico tras importaciones.
7. **Gestión de foco**: tras validar, foco al campo erróneo; `SelectAll()` en los cuadros al editar; tras borrar, selección y foco al elemento vecino; al rellenar listas siempre queda un ítem seleccionado y enfocado (evita listas "mudas" al lector).
8. **Botones deshabilitados según selección/estado** (lista vacía, nodo MUD vs personaje, tipo de trigger, proxy manual, logs desactivados).
9. **Botones con menú desplegable** (Exportar en Muds, Importar en Alias/Triggers/Paths, Conectar en Principal) mediante `ContextMenu.Show` → navegable con flechas.
10. **Una sola ventana visible a la vez** (Hide del dueño / Show al cerrar): evita que el lector se pierda entre ventanas.
11. **Voz directa** solo puntual en diálogos: `CSintetizer.SayText` al cambiar el estado de un trigger. No hay lectura por voz al abrir diálogos: se confía en el título de ventana y el foco inicial (TabIndex 0/1).
12. Selección de lector (Ninguno/JAWS/Window Eyes/NVDA/Autodetectar) como opción por ámbito.
13. Listas monoselección (`MultiSelect=false`), vista Details con cabeceras textuales.

Carencias detectadas: mnemónicos duplicados; controles sin AccessibleName (TxtAbbr, TxtContraria, TxtProxyPort, TxtLastCommands, TxtReport, CmbWhat con "1", InputBox); texto erróneo de ChkCharRepeat; ordenación solo con ratón; doble clic de personajes sin enganchar; FrmAddEditPathDictionary sin CancelButton; foco a pestañas ocultas en FrmOptions; Salir roto en trunk.

---

## 23. Comparación trunk / branches\1.2 / tags\rel-1.1.0.1

Misma estructura de carpetas en los tres (AutoUpdater, chm, cliente, CloseOmnimuds, dlls, instalador, OmnimudCommonRules, ProcessRules, rdl, SignCode, sounds). Trunk añade `Backup\`, `UpgradeLog.htm`, y los documentos recientes `DOCUMENTACION_OMNIMUD.md` y `PLAN_REESCRITURA_OMNIMUD.md`.

**Cliente (`cliente\*.cs`, 95 ficheros): trunk y branches\1.2 son idénticos byte a byte salvo:**
- `FrmPrincipal.cs` (12758 vs 12636 bytes): trunk contiene el código de prueba que rompe "Salir" (`CSintetizer.SayText("¡Holaaa cuñao!"…); return;`). **La rama 1.2 tiene la versión correcta.**
- `omnimud.csproj`: trunk ToolsVersion 15.0 (VS2017, modificado 24/12/2017); 1.2 ToolsVersion 14.0 y además `<PlatformTarget>x86</PlatformTarget>` (necesario para las DLL de 32 bits: nvdaControllerClient32, jfwapi, irrKlang).
- `Properties\AssemblyInfo.cs`: **trunk = 1.1.0.1**, **rama 1.2 = 1.2.0.0**. Ficheros generados (Resources/Settings.Designer, Reference.cs) difieren por versión de herramienta.
- Solo en trunk: `CNegotiation.cs.txt` (copia) y `ProcessRules\CCyberlife.cs` (regla de MUD nueva, 25/12/2017).
- Fechas: casi todo 13/08/2016 (fecha de checkout/copia) en las tres ramas; lo único posterior está en trunk (dic. 2017).

**Tag rel-1.1.0.1 frente a trunk/1.2** — el tag es más antiguo y le falta:
- Soporte **NVDA y Autodetectar** (`CSintetizer.cs` 2849 vs 4730 bytes; enum en `COptions.cs`; ítems del combo en `FrmOptions.cs:111-112,119-120`).
- Protecciones `it.Parent == null` en FrmCharacters (evita operar sobre nodos MUD), `String.IsNullOrEmpty(mud.ProcessRule)` en FrmAddEditMud, cambios menores en FrmCliente (97 bytes), `CData.cs`.

**Conclusión**: en cuanto a diálogos, **trunk y la rama 1.2 tienen exactamente la misma funcionalidad**; la rama 1.2 no aporta nada funcional que trunk no tenga, pero es el **estado "publicable"** (versión 1.2.0.0, x86, botón Salir correcto). Trunk = 1.2 + regla Cyberlife + migración a VS2017 + un residuo de depuración en FrmPrincipal, con el número de versión sin actualizar. La referencia funcional recomendada es trunk, tomando `FrmPrincipal.BtnSalir_Click` de la rama 1.2. El tag 1.1.0.1 está superado por ambos.

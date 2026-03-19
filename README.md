# Automatizar HU Azure DevOps

Solución compuesta por:

- Backend en C# (.NET 9) con API mínima en `Pruebas/`.
- Frontend en React + Vite en `front/`.

El flujo actual permite:

1. Armar un prompt basado en plantilla y personalizaciones.
2. Enviar ese prompt al backend para que consulte OpenAI (la API key vive en `.env` del backend).
3. Recibir JSON, previsualizar HU + Test Cases automáticamente.
4. Decidir si crear o no la HU en Azure DevOps.
5. Ver un panel de logs de éxito/error para diagnóstico.

---

## Arquitectura actual

### Backend (`Pruebas/`)

Expone endpoints HTTP:

- `POST /api/generate`
: Recibe `prompt`, llama a OpenAI y devuelve el JSON generado.
- `POST /api/create`
: Recibe el JSON HU final y crea HU + Test Cases + vínculos + suite en Azure DevOps.

### Frontend (`front/`)

Módulos principales:

- `Armar Prompt`.
- `IA + Preview JSON`.

Características implementadas:

- Persistencia de configuración y previsualización con `localStorage`.
- Navegación entre módulos sin perder estado.
- Panel de logs en UI para eventos de éxito, error e información.

---

## Requisitos previos

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Acceso a la organización de Azure DevOps con un PAT (*Personal Access Token*) con permisos de lectura/escritura en Work Items y Test Plans.

---

## Configuración inicial

Crea un archivo `.env` en la raíz del proyecto con tus credenciales:

```
AZDO_ORG=nombre-de-tu-organizacion
AZDO_PROJECT=nombre-del-proyecto
AZDO_PAT=tu-personal-access-token
OPENAI_API_KEY=sk-tu-api-key
```

### ¿Dónde encuentro cada valor?

| Variable | Descripción | Cómo obtenerla |
|---|---|---|
| `AZDO_ORG` | Nombre de la organización | Segmento que aparece en `https://dev.azure.com/**nombre-org**/` |
| `AZDO_PROJECT` | Nombre del proyecto | Aparece en la URL justo después de la organización: `https://dev.azure.com/org/**nombre-proyecto**/` |
| `AZDO_PAT` | Token de acceso personal | En Azure DevOps: esquina superior derecha  **User Settings  Personal access tokens  New Token**. Activa permisos: `Work Items (Read & Write)` y `Test Management (Read & Write)` |

> `.env` y `hu.json` están en el `.gitignore`  **nunca se suben al repositorio**.

---

## Ejecución local

### 1) Backend

```bash
cd Pruebas
dotnet run
```

El backend queda en `http://localhost:5000`.

### 2) Frontend

```bash
cd front
npm install
npm run dev
```

Vite usa proxy para `/api` hacia `http://localhost:5000`.

## Uso por flujo

1. En `Armar Prompt`, configura campos y genera el prompt.
2. Entra a `IA + Preview JSON` y genera JSON con IA.
3. La previsualización se carga automáticamente.
4. Decide:
   - Crear HU en Azure DevOps.
   - Volver a empezar con otra necesidad.

## API backend (detalle)

### `POST /api/generate`

Request:

```json
{
  "prompt": "...",
  "model": "gpt-4.1-mini"
}
```

Response (éxito):

```json
{
  "json": "{ ... }",
  "raw": "respuesta cruda del modelo"
}
```

### `POST /api/create`

Request:

- JSON HU final con `hu`, `testCases`, `testSuite`, etc.

Response (éxito):

```json
{
  "huId": 12345,
  "huUrl": "https://dev.azure.com/...",
  "testCases": [],
  "suiteId": 999,
  "message": "HU #12345 creada con N Test Cases."
}
```

## Panel de logs (UI)

La app incluye un panel de logs en la parte inferior del contenido principal.

Registra eventos como:

- Generación de prompt.
- Generación de JSON con IA (inicio, éxito, error).
- Creación en Azure DevOps (inicio, éxito, error).
- Copias al portapapeles.
- Limpieza/reset de estados.

Funcionalidades:

- `Copiar logs`: exporta eventos para soporte.
- `Limpiar logs`: borra historial en pantalla y almacenamiento local.

Cada evento muestra:

- Nivel (`SUCCESS`, `ERROR`, `INFO`).
- Fecha/hora.
- Módulo y acción.
- Mensaje y detalle opcional.

## Persistencia local

Se guarda en `localStorage`:

- Última configuración de `Armar Prompt`.
- Último estado de `IA + Preview JSON`.
- Historial de logs.

Esto evita pérdida de información al cambiar de módulo o refrescar el navegador.

## Troubleshooting rápido

### Error proxy de Vite (`ECONNREFUSED /api/...`)

- Verifica backend activo en `http://localhost:5000`.
- Arranca backend con `dotnet run` en `Pruebas/`.

### `Unexpected end of JSON input`

- El backend devolvió cuerpo vacío o error no JSON.
- Revisa el panel de logs UI y la consola de backend.

### Error OpenAI

- Revisa `OPENAI_API_KEY` en `.env` de `Pruebas/`.
- Confirma salida a internet desde el servidor.

### Error Azure DevOps

- Verifica `AZDO_ORG`, `AZDO_PROJECT`, `AZDO_PAT`.
- Revisa permisos del PAT para Work Items y Test Management.

## Uso CLI (opcional)

Se conserva ejecución por CLI para procesos manuales/lote:

```bash
# Usa hu.json por defecto
dotnet run --project Pruebas/Pruebas.csproj

# Usa otro archivo JSON
dotnet run --project Pruebas/Pruebas.csproj -- mi_historia.json
```

---

## Frontend (HU Workbench)

Aplicación en React + Vite + TypeScript (carpeta `front/`). Es **opcional** y no llama a Azure DevOps: se enfoca en ayudarte a armar/validar el JSON y el prompt.

### Requisitos previos

- Node.js (recomendado: 18+)
- npm

### Ejecutar localmente

```bash
cd front
npm install
npm run dev
```

Vite te mostrará la URL local (típicamente `http://localhost:5173`).

### Comandos útiles

```bash
cd front

# tests
npm test

# build producción
npm run build

# preview del build
npm run preview

# lint
npm run lint
```

### Uso (módulos)

La UI tiene una **sidebar** para alternar módulos.

**Módulo 1: Armar Prompt**

- Completa/edita los campos permitidos (por ejemplo: necesidad, cantidad/estado de test cases, `iterationPath`, `areaPath`, `hu.*`, `testSuite.*`).
- Acciones:
  - Generar prompt
  - Reset
  - Copiar prompt / Copiar JSON
  - Descargar JSON (`hu.generated.json`)

**Módulo 2: Preview JSON**

- Pega un JSON que incluya al menos `hu` y `testCases`.
- La pantalla renderiza una previsualización de HU y la lista de Test Cases.
- Los campos HTML `description` y `acceptanceCriteria` se sanitizan antes de renderizarse.

---

## Estructura del JSON (`hu.json`)

```json
{
  "iterationPath": "Pruebas\\Soporte SDI\\2026\\Febrero",
  "areaPath": "Pruebas",

  "hu": {
    "title": "Título de la Historia de Usuario",
    "description": "<p><strong>Rol:</strong> ...</p>",
    "acceptanceCriteria": "<ul><li><strong>AC1  Nombre:</strong><br>Descripción.</li></ul>",
    "priority": 2,
    "risk": "2 - Medium",
    "startDate": "2026-02-01",
    "finishDate": "2026-02-28",
    "valueArea": "Business",
    "tipoHU": "Funcional",
    "frenteDeTrabajo": "Mejoras",
    "assignedTo": "nombre@empresa.com"
  },

  "testCases": [
    {
      "title": "TC-001  Descripción corta",
      "action": "Pasos que realiza el usuario.",
      "expected": "Resultado esperado del sistema.",
      "state": "Ready"
    }
  ],

  "testSuite": {
    "planId": 153,
    "planName": "Nombre del Test Plan"
  }
}
```

---

### Cómo completar `iterationPath` y `areaPath`

Ambos valores deben coincidir exactamente con los configurados en tu proyecto de Azure DevOps. Puedes consultarlos así:

- **Desde un work item existente:** abre cualquier HU en AzDO y revisa los campos *Area* e *Iteration*  el valor que aparece ahí es exactamente el que debes poner.
- **Desde la configuración:** ve a **Project Settings  Boards  Team configuration  Iterations / Areas**.
- **Formato:** usa `\\` como separador de niveles en el JSON. Por ejemplo, si la ruta es `Proyecto > Equipo > 2026 > Marzo`, escribe `"Proyecto\\Equipo\\2026\\Marzo"`.

---

### Cómo completar `testSuite`

El bloque `testSuite` indica en qué Test Plan se creará el Requirement Based Suite. Tienes dos opciones:

**Opción A  Por ID (recomendada):** pon el ID numérico del plan en `planId`. Lo encuentras en la URL al abrir el plan en Azure DevOps: `.../_testPlans/execute?planId=`**`153`**.

**Opción B  Por nombre:** deja `planId` en `0` y pon el nombre exacto del plan en `planName`. El programa buscará el plan automáticamente recorriendo todos los planes del proyecto.

```json
"testSuite": {
  "planId": 153,
  "planName": "Soporte SDI_Stories_Febrero"
}
```

> Si omites la sección `testSuite` por completo, el programa crea la HU y los TCs normalmente pero no crea el suite.

---

### Valores válidos por campo

| Campo | Valores aceptados |
|---|---|
| `priority` | `1` = Critical  `2` = High  `3` = Medium  `4` = Low |
| `risk` | `1 - High`  `2 - Medium`  `3 - Low` |
| `valueArea` | `Business`  `Architectural` |
| `tipoHU` | `Funcional`  `Técnica` |
| `assignedTo` | Correo del responsable, ej: `juan.perez@empresa.com`  *(vacío = sin asignar)* |
| `frenteDeTrabajo` | `ControlCambios`  `Mejoras`  `OptimizacionBackEnd`  `OptimizacionFrontEnd`  `Proyecto`  `Seguridad` |
| `state` (TCs) | `Design`  `Ready`  `Closed`  *(vacío = predeterminado del proceso)* |
| `startDate` / `finishDate` | Formato `yyyy-MM-dd` |

---

## Generar el JSON con IA

La forma más rápida de crear el `hu.json` es usar una IA (ChatGPT, Copilot, etc.) con el siguiente prompt.

**Instrucciones:**
1. Copia el bloque completo de abajo.
2. Reemplaza `[PEGA AQUÍ TU hu.json ACTUAL]` con el contenido de tu `hu.json` actual (sirve como plantilla  la IA conservará `iterationPath`, `areaPath` y `testSuite` sin tocarlos).
3. Reemplaza `[DESCRIBE AQUÍ LA NUEVA HU]` con la descripción de tu historia.
4. La IA devolverá el JSON listo para usar  solo copia y pega en tu `hu.json`.

---

````
Actúa como analista funcional experto en Azure DevOps.

Te proporcionaré:

1. Un JSON de ejemplo.
2. La descripción de una nueva Historia de Usuario (HU).

Tu tarea es MODIFICAR únicamente lo necesario dentro del JSON para adaptarlo a la nueva HU.

Debes devolver el JSON final dentro de un bloque de código.

---

# REGLAS IMPORTANTES

## 1. Campos que NO debes cambiar

NO cambies los siguientes campos ni sus valores:

* iterationPath
* areaPath
* testSuite
* planId
* planName

---

## 2. Campos que SÍ puedes modificar

Solo puedes modificar los siguientes campos:

* hu.title
* hu.description
* hu.acceptanceCriteria
* hu.priority
* hu.risk
* hu.startDate
* hu.finishDate
* hu.valueArea
* hu.tipoHU
* hu.frenteDeTrabajo
* hu.assignedTo
* testCases

---

## 3. Restricciones del JSON

* Mantén EXACTAMENTE la misma estructura del JSON.
* NO agregues campos nuevos.
* NO elimines campos existentes.

---

## 4. Formato de la descripción

La descripción debe estar en HTML bien formateado usando:

* `<p>`
* `<br>`
* `<strong>`

Debe seguir exactamente esta estructura visual:

`<p><strong>Rol:</strong> ...</p>`

`<p><strong>Necesito:</strong> ...</p>`

`<p><strong>Para que:</strong> ...</p>`

---

## 5. Formato de criterios de aceptación

Los criterios de aceptación deben estar en HTML claro y legible:

```html
<ul>
<li><strong>AC1  Título corto:</strong><br>Descripción clara del criterio.</li>
<li><strong>AC2  Título corto:</strong><br>Descripción clara del criterio.</li>
</ul>
```

---

## 6. Test Cases

Debes generar entre 3 y 6 test cases usando esta estructura:

```json
{
  "title": "TC-001  descripción corta",
  "action": "paso a paso que ejecuta el usuario",
  "expected": "resultado esperado del sistema",
  "state": "Ready"
}
```

Los test cases deben cubrir:

* flujo positivo
* flujo negativo
* caso borde

---

## 7. Reglas adicionales

* NO uses emojis.
* Devuelve ÚNICAMENTE el JSON final válido.
* NO agregues explicaciones.
* NO agregues texto antes ni después del JSON.
* NO uses markdown fuera del bloque de código.

---

# JSON DE PLANTILLA

[PEGA AQUÍ TU hu.json ACTUAL]

---

# NUEVA HISTORIA DE USUARIO

[DESCRIBE AQUÍ LA NUEVA HU CON EL MAYOR DETALLE POSIBLE]
````

---

## Estructura del proyecto

```
/
 Pruebas/
    Program.cs          # Lógica principal
    Pruebas.csproj      # Proyecto .NET
    hu.json             # Tu archivo de parámetros (NO se sube al repo)
 front/                 # Frontend React (HU Workbench)
   src/App.tsx         # Layout + módulos
   src/lib/template.ts # Plantilla base
   src/utils/download.ts # Copiar/descargar
 .env                    # Credenciales (NO se sube al repo)
 .gitignore
 README.md
```

---

## Autores

Área de Soluciones TI
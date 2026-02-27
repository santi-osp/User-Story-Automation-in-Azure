# Automatizar HU — Azure DevOps

Herramienta de línea de comandos en C# (.NET 9) que lee un archivo JSON con los parámetros de una Historia de Usuario y la crea automáticamente en Azure DevOps, junto con sus Test Cases vinculados (relación *Tested By*).

---

## ¿Cómo funciona?

1. Cada integrante del área crea su propio archivo `hu.json` con los datos de la HU que desea crear.
2. Ejecuta el programa.
3. El programa llama a la API de Azure DevOps y crea la HU + los TCs, vinculándolos automáticamente.

---

## Requisitos previos

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Acceso a la organización de Azure DevOps con un PAT (*Personal Access Token*) con permisos de lectura/escritura en Work Items.

---

## Configuración inicial

Crea un archivo `.env` en la raíz del proyecto con tus credenciales:

```
AZDO_ORG=nombre-de-tu-organizacion
AZDO_PROJECT=nombre-del-proyecto
AZDO_PAT=tu-personal-access-token
```

> `.env` y `hu.json` están en el `.gitignore` — **nunca se suben al repositorio**.

---

## Uso

```bash
# Usa hu.json por defecto
dotnet run --project Pruebas/Pruebas.csproj

# Usa otro archivo JSON
dotnet run --project Pruebas/Pruebas.csproj -- mi_historia.json
```

---

## ⚠️ Paso adicional para visualizar los Test Cases en AzDO

Debido a una limitación de Azure DevOps, los Test Cases creados por el script no aparecen
visualmente en la pestaña de tests de la HU hasta que se realiza el siguiente procedimiento
**una sola vez por HU**:

1. Ejecuta el programa — la HU y los Test Cases se crean y vinculan correctamente.
2. Haz clic en los **tres puntos (⋯)** del HU y selecciona **Add test**.
5. Escribe cualquier nombre para el test case temporal y dale Enter *(el nombre no importa)*.
6. Dale clic en los **tres puntos (⋯)** del test case que acabas de crear y selecciona **Remove Test** para borrarlo.
7. **Recarga la página** (F5).

Los Test Cases creados por el script aparecerán correctamente listados.

---

## Estructura del JSON (`hu.json`)

> **⚠️ Importante:** Los valores de `iterationPath` y `areaPath` que aparecen a continuación son **solo ejemplos**.
> Debes reemplazarlos con la ruta real de tu iteración y área dentro de tu proyecto en Azure DevOps.
> Puedes consultarlos en **Boards → Project Settings → Team configuration** o revisando la URL de cualquier work item existente.

```json
{
  "iterationPath": "Pruebas\\Soporte SDI\\2026\\Febrero",
  "areaPath": "Pruebas\\Soporte SDI",

  "hu": {
    "title": "Título de la Historia de Usuario",
    "description": "Descripción detallada de la HU.",
    "acceptanceCriteria": "<ul><li>Criterio 1</li><li>Criterio 2</li></ul>",
    "priority": 2,
    "risk": "2 - Medium",
    "startDate": "2026-02-01",
    "finishDate": "2026-02-28",
    "valueArea": "Business",
    "tipoHU": "Funcional",
    "frenteDeTrabajo": "Mejoras",
    "assignedTo": "nombre@postobon.com.co"
  },

  "testCases": [
    {
      "title": "TC-001 — Descripción corta",
      "action": "Pasos que realiza el usuario.",
      "expected": "Resultado esperado del sistema.",
      "state": "Ready"
    }
  ]
}
```

### Valores válidos por campo

| Campo | Valores aceptados |
|---|---|
| `priority` | `1` = Critical · `2` = High · `3` = Medium · `4` = Low |
| `risk` | `1 - High` · `2 - Medium` · `3 - Low` |
| `valueArea` | `Business` · `Architectural` |
| `tipoHU` | `Funcional` · `Técnica` |
| `assignedTo` | Correo del responsable, ej: `juan.perez@empresa.com` · *(vacío = sin asignar)* |
| `frenteDeTrabajo` | `ControlCambios` · `Mejoras` · `OptimizacionBackEnd` · `OptimizacionFrontEnd` · `Proyecto` · `Seguridad` |
| `state` (TCs) | `Design` · `Ready` · `Closed` · *(vacío = predeterminado del proceso)* |
| `acceptanceCriteria` | Texto plano o HTML (`<ul>`, `<li>`, `<b>`, etc.) |
| `startDate` / `finishDate` | Formato `yyyy-MM-dd` |

---

## Generar el JSON con IA

Puedes pedirle a cualquier IA (ChatGPT, Copilot, etc.) que genere el `hu.json` por ti.  
Copia el siguiente prompt, rellena la sección **"Lo que necesito"** con tu historia y pégalo en el chat:

---

````
Eres un analista funcional experto en Azure DevOps.
Necesito que generes un archivo hu.json con la siguiente estructura exacta para crear
una Historia de Usuario y sus Test Cases automáticamente mediante un script.

════════════════════════════════════════════
ESTRUCTURA DEL JSON (no cambies los nombres de los campos):
════════════════════════════════════════════

{
  "iterationPath": "<ruta de iteración en AzDO, ej: Pruebas\\Soporte SDI\\2026\\Marzo>",
  "areaPath": "<área del proyecto, ej: Pruebas\\Soporte SDI>",

  "hu": {
    "title": "<título claro y descriptivo de la HU>",
    "description": "<descripción funcional de la HU>",
    "acceptanceCriteria": "<criterios de aceptación en formato HTML con <ul> y <li>>",
    "priority": <1=Critical | 2=High | 3=Medium | 4=Low>,
    "risk": "<1 - High | 2 - Medium | 3 - Low>",
    "startDate": "<yyyy-MM-dd>",
    "finishDate": "<yyyy-MM-dd>",
    "valueArea": "<Business | Architectural>",
    "tipoHU": "<Funcional | Técnica>",
    "frenteDeTrabajo": "<ControlCambios | Mejoras | OptimizacionBackEnd | OptimizacionFrontEnd | Proyecto | Seguridad>",
    "assignedTo": "<correo del responsable, ej: juan.perez@empresa.com | dejar vacío para sin asignar>"
  },

  "testCases": [
    {
      "title": "<TC-001 — descripción corta>",
      "action": "<paso a paso que ejecuta el usuario>",
      "expected": "<resultado esperado del sistema>",
      "state": "Ready"
    }
    // ... más test cases según corresponda
  ]
}

════════════════════════════════════════════
LO QUE NECESITO:
════════════════════════════════════════════

[DESCRIBE AQUÍ TU HISTORIA DE USUARIO CON EL MAYOR DETALLE POSIBLE]

Ejemplo:
"Quiero una HU para agregar validación de campos obligatorios en el formulario de
 creación de clientes. El usuario no debe poder guardar si el nombre o el correo
 están vacíos. Pertenece al frente de Mejoras, es funcional, prioridad alta,
 para la iteración de Marzo 2026."

════════════════════════════════════════════
INSTRUCCIONES ADICIONALES:
════════════════════════════════════════════
- Genera entre 3 y 6 Test Cases que cubran flujos positivos, negativos y casos borde.
- Los criterios de aceptación deben ser claros, medibles y verificables.
- Devuelve ÚNICAMENTE el JSON válido, sin texto adicional ni bloques de código markdown.
````

---

## Estructura del proyecto

```
/
├── Pruebas/
│   ├── Program.cs          # Lógica principal
│   ├── Pruebas.csproj      # Proyecto .NET
│   └── hu.json             # Tu archivo de parámetros (NO se sube al repo)
├── .env                    # Credenciales (NO se sube al repo)
├── .gitignore
└── README.md
```

---

## Autores

Área de Soluciones TI

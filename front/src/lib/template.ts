import type { FormValues, HuPayload, TcFields } from "../types";

export const EXACT_PROMPT_TEMPLATE = String.raw`{
  "iterationPath": "Sistema digital de integración (SDI)\\Soporte SDI\\2026\\Marzo",
  "areaPath": "Sistema digital de integración (SDI)\\Soporte SDI",
  "hu": {
    "title": "🚀 HU — Migración: dejar solo SAP en USP_CargarSaldoEnvase (eliminar AS400 y asegurar auditoría)",
    "description": "<p><strong>Rol:</strong> Como Sistema SDI / equipo de integración de datos,</p><p><strong>Necesito:</strong> Refactorizar el stored procedure [SDI].[USP_CargarSaldoEnvase] para que solo procese SAP y garantice registro de auditoría y manejo de errores.</p><p><strong>Para que:</strong> La carga del canónico TBL_SaldoEnvase sea consistente con la nueva arquitectura (solo SAP), elimine código muerto/condicional de AS400.</p>",
    "acceptanceCriteria": "<ul><li><strong>AC1 🔁 Eliminar dependencias AS400</strong><br>No debe quedar ninguna referencia ejecutable a AS400: tablas staging (Staging.TBL_TMP_AS400SaldoEnvase), variables específicas (@rowsConsultedAS400, @rowsErrorAS400), deletes/merges o mapeos por SPlantaAS400Id. El procedimiento compila y no toca datos AS400 en ningún flujo.</li><li><strong>AC2 ✅ Procesamiento SAP activo e ininterrumpido</strong><br>Cuando se invoque el SP con @Erp = 'SAP' (o con @Erp = NULL), se debe ejecutar el MERGE/flujo SAP normalmente (sin insertar mensaje de error ni hacer GOTO que impida la carga). Los registros SAP válidos (BInconsistente = 0) deben insertarse/actualizarse según la lógica actual.</li><li><strong>AC3 🔒 Comprobación funcionalidad</strong><br>Cuando se ejecuta con NULL, el SP debe: truncar el canónico (si ese es el comportamiento esperado), cargar SAP y registrar la bitácora final con conteos correctos.</li></ul>",
    "priority": 1,
    "risk": "",
    "startDate": "",
    "finishDate": "",
    "valueArea": "Architectural",
    "tipoHU": "Técnica",
    "frenteDeTrabajo": "Mejoras",
    "assignedTo": "practiofc@postobon.com.co"
  },
  "testCases": [
    {
      "title": "TC001 — Ejecución estándar con parámetros NULL (Cargue Canónico)",
      "action": "Ejecución desde el OMS",
      "expected": "El procedimiento procesa correctamente los registros provenientes de SAP e inserta o actualiza la información en la tabla destino sin errores.",
      "state": "Ready"
    },
    {
      "title": "TC-002 — Validación de ausencia de lógica AS400 en el SP",
      "action": "1. Revisar el código del Stored Procedure actualizado. 2. Ejecutar el proceso. 3. Verificar que no existan consultas, joins o merges contra tablas staging o estructuras relacionadas con AS400.",
      "expected": "El SP no contiene ni ejecuta lógica relacionada con AS400 durante el proceso.",
      "state": "Ready"
    }
  ],
  "testSuite": {
    "planId": 69984,
    "planName": "Soporte SDI_Stories_Marzo"
  }
}`;

export const PROMPT_TEMPLATE_WITH_PLACEHOLDERS = `Actúa como analista funcional experto en Azure DevOps.

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

* <p>
* <br>
* <strong>

Debe seguir exactamente esta estructura visual:

<p><strong>Rol:</strong> ...</p>

<p><strong>Necesito:</strong> ...</p>

<p><strong>Para que:</strong> ...</p>

---

## 5. Formato de criterios de aceptación

Los criterios de aceptación deben estar en HTML claro y legible usando:

<ul>
<li><strong>AC1 — Título corto:</strong>
Descripción clara del criterio.</li>
<li><strong>AC2 — Título corto:</strong>
Descripción clara del criterio.</li>
</ul>

---

## 6. Test Cases

Debes generar los test cases que consideres necesarios, maximo 5, usando esta estructura:

{
"title": "TC-001 — descripción corta",
"action": "paso a paso que ejecuta el usuario",
"expected": "resultado esperado del sistema",
"state": "Ready"
}

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

${"```json"}
{{jsonTemplate}}
${"```"}

---

# NUEVA HISTORIA DE USUARIO
{{need}}`;

export const TEMPLATE_PAYLOAD: HuPayload = JSON.parse(EXACT_PROMPT_TEMPLATE);

const cloneTc = (tc: TcFields): TcFields => ({ ...tc });

export function payloadToFormValues(payload: HuPayload): FormValues {
  return {
    need: "",
    tcCount: payload.testCases.length,
    tcState: "Ready",
    iterationPath: payload.iterationPath,
    areaPath: payload.areaPath,
    hu: { ...payload.hu },
    testCases: payload.testCases.map(cloneTc),
    testSuite: { ...payload.testSuite }
  };
}

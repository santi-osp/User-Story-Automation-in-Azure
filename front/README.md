# Frontend HU Workbench

Frontend en React + Vite + TypeScript para trabajar con Historias de Usuario:

- módulo 1: armar prompt final desde campos controlados,
- módulo 2: pegar JSON y ver previsualización de HU + Test Cases.

## Stack

- React 18
- Vite
- TypeScript
- Tailwind CSS
- react-hook-form
- Vitest
- ESLint + Prettier

## Ejecutar localmente

1. Instalar dependencias:

```bash
npm install
```

2. Iniciar modo desarrollo:

```bash
npm run dev
```

3. Validar proyecto:

```bash
npm run test
npm run build
npm run lint
```

## Módulo 1: Armar Prompt

Campos editables:

- necesidad
- cantidad de test cases
- estado de test cases (Ready, Closed, Desing)
- iterationPath
- areaPath
- hu.assignedTo
- hu.startDate
- hu.finishDate
- hu.valueArea
- hu.tipoHU
- hu.frenteDeTrabajo
- hu.risk
- hu.priority
- testSuite.planId
- testSuite.planName

Salida:

- prompt final completo (texto)
- JSON final
- copiar prompt / copiar JSON / descargar JSON

## Módulo 2: Preview JSON

- pegar JSON manualmente,
- parsear y validar estructura mínima,
- mostrar preview de HU y lista de test cases,
- render HTML sanitizado para description y acceptanceCriteria.

## Estructura relevante

- `src/App.tsx`: layout principal + módulos
- `src/components/PromptGenerator.ts`: construcción de JSON/prompt
- `src/lib/template.ts`: plantilla base
- `src/utils/download.ts`: copiar/descargar

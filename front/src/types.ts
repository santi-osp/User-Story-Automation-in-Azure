export type RiskLevel = "Baja" | "Media" | "Alta";
export type TcState = "Ready" | "Design" | "Desing" | "Closed";

export interface HuFields {
  title: string;
  description: string;
  acceptanceCriteria: string;
  priority: number;
  risk: RiskLevel;
  startDate: string;
  finishDate: string;
  valueArea: string;
  tipoHU: string;
  frenteDeTrabajo: string;
  assignedTo: string;
}

export interface TcFields {
  title: string;
  action: string;
  expected: string;
  state: TcState;
}

export interface TestSuiteConfig {
  planId: number;
  planName: string;
}

export interface HuPayload {
  iterationPath: string;
  areaPath: string;
  hu: HuFields;
  testCases: TcFields[];
  testSuite: TestSuiteConfig;
}

export interface FormValues {
  need: string;
  tcCount: number;
  tcState: TcState;
  iterationPath: string;
  areaPath: string;
  hu: HuFields;
  testCases: TcFields[];
  testSuite: TestSuiteConfig;
}

export interface PromptResult {
  prompt: string;
  finalJson: HuPayload;
}

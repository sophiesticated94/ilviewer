export interface IlViewerConfiguration {
  dotnetPath: string;
  buildConfiguration: string;
  targetFramework?: string;
  projectPath?: string;
  autoOpen: boolean;
}

export interface AnalyzeRequest {
  projectPath: string;
  documentPath: string;
  line: number;
  endLine: number;
  configuration: string;
  targetFramework?: string;
  startColumn: number;
  endColumn: number;
}

export interface AnalysisResult {
  success: boolean;
  error?: string;
  message?: string;
  projectPath?: string;
  assemblyPath?: string;
  pdbPath?: string;
  targetFramework?: string;
  configuration?: string;
  assemblyLastWriteTimeUtc?: string;
  pdbLastWriteTimeUtc?: string;
  documentPath?: string;
  line?: number;
  endLine?: number;
  isApproximate: boolean;
  fragment?: MethodIl;
  context?: MethodIl;
  scopes: IlScope[];
  sourceRegions: SourceRegion[];
  instructionHighlights: InstructionHighlight[];
  instructionExplanations: InstructionExplanation[];
  selectedRegionId?: string;
}

export interface HoverOverlay {
  instructionIds: string[];
  regionId?: string;
  sourceRange?: SourceRange;
  statusText: string;
  isApproximate: boolean;
}

export interface MethodIl {
  typeName: string;
  methodName: string;
  fullName: string;
  sourceRange: SourceRange;
  instructions: IlInstruction[];
  activeInstructionOffsets: number[];
}

export interface IlInstruction {
  id: string;
  offset: number;
  offsetLabel: string;
  text: string;
  isActive: boolean;
  sourceRange?: SourceRange;
  opcode: string;
  operand?: string;
  operandKind?: string;
  operandDisplay?: string;
  resolvedSignature?: string;
  stackBehaviourPop?: string;
  stackBehaviourPush?: string;
  flowControl?: string;
  description?: string;
  tooltip?: string;
}

export interface IlScope {
  id: string;
  kind: IlScopeKind;
  displayName: string;
  assemblyName?: string;
  typeName?: string;
  methodName?: string;
  fullName?: string;
  sourceRange?: SourceRange;
  methods: IlMethodBlock[];
  instructions: IlInstruction[];
  activeInstructionIds: string[];
  activeHighlightIds: string[];
}

export type IlScopeKind = "fragment" | "function" | "class" | "typeWithNested" | "project" | "application";

export interface IlMethodBlock {
  id: string;
  assemblyName: string;
  typeName: string;
  methodName: string;
  fullName: string;
  sourceRange?: SourceRange;
  instructions: IlInstruction[];
  containsActiveInstruction: boolean;
}

export interface SourceRegion {
  id: string;
  kind: string;
  depth: number;
  sourceRange: SourceRange;
  parentId?: string;
  displayName: string;
  isSelected: boolean;
  isExact: boolean;
  language: string;
}

export interface InstructionHighlight {
  id: string;
  regionId: string;
  depth: number;
  instructionIds: string[];
  isApproximate: boolean;
}

export interface InstructionExplanation {
  opcode: string;
  title: string;
  description: string;
  operandKind: string;
  stackBehaviourPop: string;
  stackBehaviourPush: string;
  flowControl: string;
}

export interface SourceRange {
  startLine: number;
  endLine: number;
  startColumn: number;
  endColumn: number;
}

export interface PanelState {
  status: "idle" | "loading" | "ready" | "error";
  statusText: string;
  projectPath?: string;
  documentPath?: string;
  line?: number;
  endLine?: number;
  result?: AnalysisResult;
  hoverOverlay?: HoverOverlay;
}

export interface ProcessResult {
  exitCode: number | null;
  stdout: string;
  stderr: string;
}

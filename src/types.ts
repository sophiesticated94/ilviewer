export interface IlViewerConfiguration {
  dotnetPath: string;
  buildConfiguration: string;
  targetFramework?: string;
  projectPath?: string;
  autoOpen: boolean;
  graphPageSize: number;
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
  navigationTargets?: IlNavigationTarget[];
}

export interface IlNavigationTarget {
  id: string;
  kind: "source" | "il" | "decompiled" | "graphNode";
  label: string;
  assemblyName?: string;
  assemblyPath?: string;
  assemblyKind?: AssemblyKind;
  typeName?: string;
  methodName?: string;
  signature?: string;
  metadataToken?: string;
  ilOffset?: number;
  targetInstructionId?: string;
  sourcePath?: string;
  sourceRange?: SourceRange;
  language?: string;
  isExternal: boolean;
  decompileAvailable: boolean;
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

export type AssemblyKind = "project" | "projectReference" | "nuget" | "framework" | "runtime" | "externalUnknown";

export interface GraphRequest {
  projectPath: string;
  configuration: string;
  targetFramework?: string;
  nodeId?: string;
  pageSize: number;
  continuationToken?: string;
}

export interface GraphNode {
  id: string;
  kind: "assembly" | "type" | "method" | "external";
  label: string;
  assemblyName: string;
  assemblyKind: AssemblyKind;
  assemblyPath?: string;
  typeName?: string;
  methodName?: string;
  metadataToken?: string;
  signature?: string;
  sourceRange?: SourceRange;
  hasChildren: boolean;
  decompileAvailable: boolean;
  isExternal: boolean;
}

export interface GraphEdge {
  from: string;
  to: string;
  kind: "contains" | "call" | "field" | "type" | "branch" | "override" | "interfaceImpl";
  label: string;
}

export interface GraphExpandResult {
  success: boolean;
  error?: string;
  rootAssembly?: string;
  nodes: GraphNode[];
  edges: GraphEdge[];
  continuationToken?: string;
  diagnostics: string[];
}

export interface DecompileRequest {
  projectPath: string;
  configuration: string;
  targetFramework?: string;
  assemblyPath?: string;
  assemblyName?: string;
  typeName?: string;
  methodName?: string;
  metadataToken?: string;
  language?: string;
}

export interface DecompileResult {
  success: boolean;
  error?: string;
  language: string;
  title: string;
  content: string;
  sourceAvailable: boolean;
  diagnostics: string[];
}

export interface ProcessResult {
  exitCode: number | null;
  stdout: string;
  stderr: string;
}

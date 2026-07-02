import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";
import { getIlViewerConfiguration } from "./configuration";
import { runProcess } from "./processRunner";
import { AnalysisResult, AnalyzeRequest, DecompileRequest, DecompileResult, GraphExpandResult, GraphRequest } from "./types";

interface CachedAnalysis {
  result: AnalysisResult;
  assemblyPath: string;
  pdbPath: string;
  assemblyMtimeMs: number;
  pdbMtimeMs: number;
}

export class WorkerClient {
  private readonly cache = new Map<string, CachedAnalysis>();

  public constructor(
    private readonly extensionContext: vscode.ExtensionContext,
    private readonly outputChannel: vscode.OutputChannel
  ) {}

  private lastMethodResult?: MethodCache;

  public clearCache(): void {
    this.cache.clear();
    this.lastMethodResult = undefined;
  }

  public async analyze(request: AnalyzeRequest): Promise<AnalysisResult> {
    const cached = this.getCached(request);
    if (cached) {
      return cached;
    }

    if (this.lastMethodResult) {
      const lm = this.lastMethodResult;
      if (lm.projectPath === request.projectPath && lm.documentPath === request.documentPath) {
        const sourceRange = lm.result.context?.sourceRange;
        if (sourceRange && request.line >= sourceRange.startLine && request.line <= sourceRange.endLine) {
          const assemblyMtimeMs = statMtimeMs(lm.assemblyPath);
          const pdbMtimeMs = statMtimeMs(lm.pdbPath);
          if (assemblyMtimeMs === lm.assemblyMtimeMs && pdbMtimeMs === lm.pdbMtimeMs) {
            const localResult = this.reconstructLocalResult(request, lm.result);
            this.setCached(request, localResult);
            return localResult;
          }
        }
      }
    }

    const configuration = getIlViewerConfiguration();
    const workerPath = await this.ensureWorkerBuilt(configuration.dotnetPath);
    const args = [
      workerPath,
      "analyze",
      "--project",
      request.projectPath,
      "--document",
      request.documentPath,
      "--line",
      request.line.toString(),
      "--end-line",
      request.endLine.toString(),
      "--start-column",
      request.startColumn.toString(),
      "--end-column",
      request.endColumn.toString(),
      "--configuration",
      request.configuration
    ];

    if (request.targetFramework) {
      args.push("--target-framework", request.targetFramework);
    }

    const result = await runProcess(configuration.dotnetPath, args, path.dirname(request.projectPath));
    if (result.stderr.trim()) {
      this.outputChannel.append(result.stderr);
    }

    const parsed = parseWorkerResult<AnalysisResult>(result.stdout);
    if (!parsed && result.exitCode !== 0) {
      throw new Error(result.stderr.trim() || result.stdout.trim() || "IL Viewer worker failed.");
    }

    if (!parsed) {
      throw new Error("IL Viewer worker returned empty or invalid JSON.");
    }

    if (parsed.success && parsed.assemblyPath && parsed.pdbPath) {
      this.setCached(request, parsed);

      if (parsed.context?.sourceRange) {
        this.lastMethodResult = {
          projectPath: request.projectPath,
          documentPath: request.documentPath,
          result: parsed,
          assemblyPath: parsed.assemblyPath,
          pdbPath: parsed.pdbPath,
          assemblyMtimeMs: statMtimeMs(parsed.assemblyPath),
          pdbMtimeMs: statMtimeMs(parsed.pdbPath)
        };
      }
    }

    return parsed;
  }

  public async analyzeOverlay(request: AnalyzeRequest): Promise<AnalysisResult> {
    if (this.lastMethodResult) {
      const lm = this.lastMethodResult;
      if (lm.projectPath === request.projectPath && lm.documentPath === request.documentPath) {
        const sourceRange = lm.result.context?.sourceRange;
        if (sourceRange && request.line >= sourceRange.startLine && request.line <= sourceRange.endLine) {
          return this.analyze(request);
        }
      }
    }

    return {
      success: true,
      isApproximate: false,
      scopes: [],
      sourceRegions: [],
      instructionHighlights: [],
      instructionExplanations: []
    };
  }

  public async graphRoot(request: GraphRequest): Promise<GraphExpandResult> {
    return this.runWorkerCommand<GraphExpandResult>([
      "graph-root",
      "--project",
      request.projectPath,
      "--configuration",
      request.configuration,
      "--page-size",
      request.pageSize.toString(),
      ...optionalArg("--target-framework", request.targetFramework)
    ], request.projectPath);
  }

  public async graphExpand(request: GraphRequest): Promise<GraphExpandResult> {
    return this.runWorkerCommand<GraphExpandResult>([
      "graph-expand",
      "--project",
      request.projectPath,
      "--configuration",
      request.configuration,
      "--node-id",
      request.nodeId ?? "",
      "--page-size",
      request.pageSize.toString(),
      ...optionalArg("--target-framework", request.targetFramework),
      ...optionalArg("--continuation-token", request.continuationToken)
    ], request.projectPath);
  }

  public async decompile(request: DecompileRequest): Promise<DecompileResult> {
    return this.runWorkerCommand<DecompileResult>([
      "decompile",
      "--project",
      request.projectPath,
      "--configuration",
      request.configuration,
      ...optionalArg("--target-framework", request.targetFramework),
      ...optionalArg("--assembly-path", request.assemblyPath),
      ...optionalArg("--assembly-name", request.assemblyName),
      ...optionalArg("--type-name", request.typeName),
      ...optionalArg("--method-name", request.methodName),
      ...optionalArg("--metadata-token", request.metadataToken),
      ...optionalArg("--language", request.language)
    ], request.projectPath);
  }

  private async runWorkerCommand<T>(args: string[], projectPath: string): Promise<T> {
    const configuration = getIlViewerConfiguration();
    const workerPath = await this.ensureWorkerBuilt(configuration.dotnetPath);
    const result = await runProcess(configuration.dotnetPath, [workerPath, ...args], path.dirname(projectPath));
    if (result.stderr.trim()) {
      this.outputChannel.append(result.stderr);
    }

    const parsed = parseWorkerResult<T>(result.stdout);
    if (!parsed && result.exitCode !== 0) {
      throw new Error(result.stderr.trim() || result.stdout.trim() || "IL Viewer worker failed.");
    }

    if (!parsed) {
      throw new Error("IL Viewer worker returned empty or invalid JSON.");
    }

    return parsed;
  }

  private async ensureWorkerBuilt(dotnetPath: string): Promise<string> {
    const releasePath = this.getWorkerDllPath("Release");
    if (fs.existsSync(releasePath)) {
      return releasePath;
    }

    const debugPath = this.getWorkerDllPath("Debug");
    if (fs.existsSync(debugPath)) {
      return debugPath;
    }

    const projectPath = path.join(this.extensionContext.extensionPath, "worker", "IlViewer.Worker", "IlViewer.Worker.csproj");
    if (!fs.existsSync(projectPath)) {
      throw new Error("IL Viewer worker project was not found in the extension directory.");
    }

    this.outputChannel.appendLine("IL Viewer: building worker...");
    const result = await runProcess(dotnetPath, ["build", projectPath, "-c", "Release"], this.extensionContext.extensionPath);
    this.outputChannel.append(result.stdout);
    this.outputChannel.append(result.stderr);

    if (result.exitCode !== 0 || !fs.existsSync(releasePath)) {
      throw new Error(result.stderr.trim() || "Could not build IL Viewer worker.");
    }

    return releasePath;
  }

  private getCached(request: AnalyzeRequest): AnalysisResult | undefined {
    const key = cacheKey(request);
    const cached = this.cache.get(key);
    if (!cached) {
      return undefined;
    }

    const assemblyMtimeMs = statMtimeMs(cached.assemblyPath);
    const pdbMtimeMs = statMtimeMs(cached.pdbPath);
    if (assemblyMtimeMs === cached.assemblyMtimeMs && pdbMtimeMs === cached.pdbMtimeMs) {
      return cached.result;
    }

    this.cache.delete(key);
    return undefined;
  }

  private setCached(request: AnalyzeRequest, result: AnalysisResult): void {
    if (!result.assemblyPath || !result.pdbPath) {
      return;
    }

    this.cache.set(cacheKey(request), {
      result,
      assemblyPath: result.assemblyPath,
      pdbPath: result.pdbPath,
      assemblyMtimeMs: statMtimeMs(result.assemblyPath),
      pdbMtimeMs: statMtimeMs(result.pdbPath)
    });
  }

  private getWorkerDllPath(configuration: "Debug" | "Release"): string {
    return path.join(
      this.extensionContext.extensionPath,
      "worker",
      "IlViewer.Worker",
      "bin",
      configuration,
      "net8.0",
      "IlViewer.Worker.dll"
    );
  }

  private reconstructLocalResult(request: AnalyzeRequest, cachedResult: AnalysisResult): AnalysisResult {
    const overlaps = (left: any, rightOrStart: any, end?: number) => {
      if (typeof rightOrStart === "number") {
        return left.startLine <= end! && left.endLine >= rightOrStart;
      }
      return left.startLine <= rightOrStart.endLine && left.endLine >= rightOrStart.startLine;
    };

    const activeInstructions = (cachedResult.context?.instructions ?? []).map(inst => {
      const isActive = inst.sourceRange ? overlaps(inst.sourceRange, request.line, request.endLine) : false;
      return { ...inst, isActive };
    });

    let activeInsts = activeInstructions.filter(i => i.isActive);
    let isApproximate = false;

    if (activeInsts.length === 0 && (cachedResult.context?.instructions ?? []).length > 0) {
      let nearestInst: any = null;
      let minDistance = Infinity;
      for (const inst of (cachedResult.context?.instructions ?? [])) {
        if (inst.sourceRange) {
          const dist = Math.abs(inst.sourceRange.startLine - request.line);
          if (dist < minDistance) {
            minDistance = dist;
            nearestInst = inst;
          }
        }
      }
      if (nearestInst) {
        activeInsts = [{ ...nearestInst, isActive: true }];
      }
      isApproximate = true;
    }

    const fragment = cachedResult.context ? {
      ...cachedResult.context,
      instructions: activeInsts,
      activeInstructionOffsets: activeInsts.map(i => i.offset)
    } : undefined;

    const contextInstructions = (cachedResult.context?.instructions ?? []).map(inst => {
      const isActive = activeInsts.some(ai => ai.id === inst.id);
      return { ...inst, isActive };
    });

    const context = cachedResult.context ? {
      ...cachedResult.context,
      instructions: contextInstructions,
      activeInstructionOffsets: activeInsts.map(i => i.offset)
    } : undefined;

    let selectedRegion: any = null;
    let maxDepth = -1;
    const nonSelectionRegions = cachedResult.sourceRegions.filter(r => r.kind !== "selection");
    for (const region of nonSelectionRegions) {
      if (request.line >= region.sourceRange.startLine && request.line <= region.sourceRange.endLine) {
        if (region.depth > maxDepth) {
          maxDepth = region.depth;
          selectedRegion = region;
        }
      }
    }
    const selectedRegionId = selectedRegion ? selectedRegion.id : undefined;

    const oldSelectionRegion = cachedResult.sourceRegions.find(r => r.kind === "selection");
    const oldSelectionId = oldSelectionRegion ? oldSelectionRegion.id : "region-selection";

    const selectionRegion: any = {
      id: oldSelectionId,
      kind: "selection",
      depth: 0,
      sourceRange: {
        startLine: request.line,
        endLine: request.endLine,
        startColumn: request.startColumn,
        endColumn: request.endColumn
      },
      displayName: "Zaznaczenie",
      isSelected: true,
      isExact: true,
      language: cachedResult.sourceRegions[0]?.language ?? "csharp"
    };

    const sourceRegions = [
      selectionRegion,
      ...nonSelectionRegions.map(region => ({
        ...region,
        isSelected: region.id === selectedRegionId
      }))
    ];

    const activeInstIds = new Set(activeInsts.map(i => i.id));
    const activeHighlightIds = new Set(
      cachedResult.instructionHighlights
        .filter(h => h.regionId === selectedRegionId)
        .map(h => h.id)
    );

    const scopes = cachedResult.scopes.map(scope => {
      const scopeInstructions = scope.instructions.map(inst => ({
        ...inst,
        isActive: activeInstIds.has(inst.id)
      }));

      const scopeMethods = scope.methods.map(mb => {
        const mbInstructions = mb.instructions.map(inst => ({
          ...inst,
          isActive: activeInstIds.has(inst.id)
        }));
        return {
          ...mb,
          instructions: mbInstructions,
          containsActiveInstruction: mbInstructions.some(i => i.isActive)
        };
      });

      return {
        ...scope,
        instructions: scope.kind === "fragment" ? scopeInstructions.filter(i => i.isActive) : scopeInstructions,
        methods: scopeMethods,
        activeInstructionIds: scopeInstructions.filter(i => i.isActive).map(i => i.id),
        activeHighlightIds: cachedResult.instructionHighlights
          .filter(h => activeHighlightIds.has(h.id))
          .map(h => h.id)
      };
    });

    const highlights = cachedResult.instructionHighlights.map(h => {
      const isRegionApprox = isApproximate;
      const targetRegion = sourceRegions.find(r => r.id === h.regionId);
      let regionInsts: string[] = [];
      if (targetRegion) {
        regionInsts = contextInstructions
          .filter(i => i.sourceRange && overlaps(i.sourceRange, targetRegion.sourceRange))
          .map(i => i.id);
      }
      if (regionInsts.length === 0 && activeInsts.length > 0) {
        regionInsts = activeInsts.map(i => i.id);
      }
      return {
        ...h,
        instructionIds: regionInsts,
        isApproximate: isRegionApprox
      };
    });

    return {
      ...cachedResult,
      line: request.line,
      endLine: request.endLine,
      isApproximate,
      fragment,
      context,
      sourceRegions,
      scopes,
      instructionHighlights: highlights,
      selectedRegionId,
      message: isApproximate ? "This line has no direct sequence point. Showing the nearest generated IL in the surrounding method." : undefined
    };
  }
}

interface MethodCache {
  projectPath: string;
  documentPath: string;
  result: AnalysisResult;
  assemblyPath: string;
  pdbPath: string;
  assemblyMtimeMs: number;
  pdbMtimeMs: number;
}

function cacheKey(request: AnalyzeRequest): string {
  return [
    request.projectPath,
    request.documentPath,
    request.line,
    request.endLine,
    request.startColumn,
    request.endColumn,
    request.configuration,
    request.targetFramework ?? ""
  ].join("|");
}

function statMtimeMs(filePath: string): number {
  try {
    return fs.statSync(filePath).mtimeMs;
  } catch {
    return -1;
  }
}

function optionalArg(name: string, value: string | undefined): string[] {
  return value ? [name, value] : [];
}

function parseWorkerResult<T>(stdout: string): T | undefined {
  const trimmed = stdout.trim();
  if (!trimmed) {
    return undefined;
  }

  const lastLine = trimmed.split(/\r?\n/).at(-1);
  if (!lastLine) {
    return undefined;
  }

  return JSON.parse(lastLine) as T;
}

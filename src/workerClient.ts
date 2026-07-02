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

  public clearCache(): void {
    this.cache.clear();
  }

  public async analyze(request: AnalyzeRequest): Promise<AnalysisResult> {
    const cached = this.getCached(request);
    if (cached) {
      return cached;
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
    }

    return parsed;
  }

  public async analyzeOverlay(request: AnalyzeRequest): Promise<AnalysisResult> {
    return this.analyze(request);
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

import * as vscode from "vscode";
import { createAnalyzeRequest } from "./analysisRequestFactory";
import { getIlViewerConfiguration } from "./configuration";
import { DecompiledDocumentProvider } from "./decompiledDocumentProvider";
import { DotnetBuildService } from "./dotnetBuildService";
import { toHoverOverlay } from "./hoverOverlayMapper";
import { IlWebviewPanel } from "./ilWebviewPanel";
import { ProjectDiscovery } from "./projectDiscovery";
import { SourceContext, SourceContextStore } from "./sourceContextStore";
import { WorkerClient } from "./workerClient";
import { GraphExpandResult, IlNavigationTarget, PanelState, SourceRange } from "./types";

const SupportedLanguageIds = new Set(["csharp", "fsharp", "vb"]);

export class ExtensionController {
  private readonly outputChannel = vscode.window.createOutputChannel("IL Viewer");
  private readonly projectDiscovery: ProjectDiscovery;
  private readonly buildService: DotnetBuildService;
  private readonly workerClient: WorkerClient;
  private readonly decompiledDocumentProvider = new DecompiledDocumentProvider();
  private readonly sourceContextStore = new SourceContextStore();
  private readonly panel: IlWebviewPanel;
  private debounceTimer?: NodeJS.Timeout;
  private updateVersion = 0;
  private hoverVersion = 0;

  public constructor(private readonly context: vscode.ExtensionContext) {
    this.projectDiscovery = new ProjectDiscovery(context);
    this.buildService = new DotnetBuildService(this.outputChannel);
    this.workerClient = new WorkerClient(context, this.outputChannel);
    this.panel = new IlWebviewPanel(context, {
      rebuild: () => void this.rebuild(),
      refresh: () => void this.refresh(),
      selectProject: () => void this.selectProject(),
      openGraph: () => void this.openGraph(),
      expandGraph: (nodeId, continuationToken) => void this.expandGraph(nodeId, continuationToken),
      navigateTarget: target => void this.navigateTarget(target)
    });
  }

  public activate(): void {
    this.decompiledDocumentProvider.register(this.context);
    this.context.subscriptions.push(
      this.outputChannel,
      vscode.commands.registerCommand("ilviewer.open", () => this.open()),
      vscode.commands.registerCommand("ilviewer.rebuild", () => this.rebuild()),
      vscode.commands.registerCommand("ilviewer.refresh", () => this.refresh()),
      vscode.commands.registerCommand("ilviewer.selectProject", () => this.selectProject()),
      vscode.commands.registerCommand("ilviewer.openApplicationGraph", () => this.openGraph()),
      vscode.window.onDidChangeTextEditorSelection(event => this.onSelectionChanged(event)),
      vscode.window.onDidChangeActiveTextEditor(editor => this.onActiveEditorChanged(editor)),
      vscode.workspace.onDidChangeConfiguration(event => {
        if (event.affectsConfiguration("ilviewer")) {
          this.workerClient.clearCache();
        }
      }),
      vscode.languages.registerHoverProvider(
        [...SupportedLanguageIds].map(language => ({ language, scheme: "file" })),
        {
          provideHover: (document, position) => {
            void this.updateHoverOverlay(document, position);
            return undefined;
          }
        })
    );

    if (getIlViewerConfiguration().autoOpen && vscode.window.activeTextEditor && isSupportedDocument(vscode.window.activeTextEditor.document)) {
      void this.open();
    }
  }

  public async open(): Promise<void> {
    this.panel.show();
    const editor = vscode.window.activeTextEditor;
    if (editor && isSupportedDocument(editor.document)) {
      await this.updateForEditor(editor, true);
      return;
    }

    const sourceContext = this.sourceContextStore.get();
    if (sourceContext) {
      await this.updateForSourceContext(sourceContext, true);
      return;
    }

    this.panel.update(idleState("Otwórz plik C#, F# lub VB.NET."));
  }

  public async rebuild(): Promise<void> {
    const sourceContext = this.getActiveOrStoredSourceContext();
    if (!sourceContext) {
      vscode.window.showWarningMessage("IL Viewer: otwórz plik C#, F# lub VB.NET przed przebudowaniem.");
      return;
    }

    const projectPath = sourceContext.projectPath
      ?? await this.projectDiscovery.findProjectForDocument(sourceContext.document, true);
    if (!projectPath) {
      this.panel.update(errorState("Nie znaleziono projektu .NET."));
      return;
    }
    this.sourceContextStore.updateProject(projectPath);

    this.panel.show();
    this.panel.update(loadingState("Przebudowuję projekt...", projectPath, sourceContext.document.uri.fsPath));

    const result = await vscode.window.withProgress(
      {
        location: vscode.ProgressLocation.Notification,
        title: "IL Viewer: przebudowywanie projektu",
        cancellable: false
      },
      () => this.buildService.build(projectPath));

    if (result.exitCode !== 0) {
      this.panel.update(errorState("Build nie powiódł się. Szczegóły są w Output: IL Viewer.", projectPath, sourceContext.document.uri.fsPath));
      this.outputChannel.show(true);
      return;
    }

    this.workerClient.clearCache();
    await this.updateForSourceContext({ ...sourceContext, projectPath }, false);
  }

  public async refresh(): Promise<void> {
    this.workerClient.clearCache();
    const sourceContext = this.getActiveOrStoredSourceContext();
    if (!sourceContext) {
      this.panel.update(idleState("Otwórz plik C#, F# lub VB.NET."));
      return;
    }

    this.panel.show();
    await this.updateForSourceContext(sourceContext, true);
  }

  public async selectProject(): Promise<void> {
    const selected = await this.projectDiscovery.selectProject();
    if (!selected) {
      return;
    }

    this.workerClient.clearCache();
    const sourceContext = this.getActiveOrStoredSourceContext();
    if (sourceContext) {
      await this.updateForSourceContext({ ...sourceContext, projectPath: selected }, false);
    } else {
      this.panel.update(idleState("Wybrano projekt. Otwórz plik źródłowy .NET.", selected));
    }
  }

  public async openGraph(): Promise<void> {
    const graphContext = await this.resolveGraphContext(false);
    if (!graphContext) {
      this.panel.updateGraph(graphFailure("Otwórz plik C#, F# lub VB.NET przed otwarciem grafu."), false);
      return;
    }

    this.panel.show();
    try {
      const result = await this.workerClient.graphRoot({
        projectPath: graphContext.projectPath,
        configuration: graphContext.configuration.buildConfiguration,
        targetFramework: graphContext.configuration.targetFramework,
        pageSize: graphContext.configuration.graphPageSize
      });
      this.panel.updateGraph(result, false);
    } catch (error) {
      this.panel.updateGraph(graphFailure(error instanceof Error ? error.message : String(error)), false);
    }
  }

  public async expandGraph(nodeId: string, continuationToken?: string): Promise<void> {
    const graphContext = await this.resolveGraphContext(false);
    if (!graphContext) {
      this.panel.updateGraph(graphFailure("Nie ma aktywnego projektu dla grafu."), true);
      return;
    }

    try {
      const result = await this.workerClient.graphExpand({
        projectPath: graphContext.projectPath,
        configuration: graphContext.configuration.buildConfiguration,
        targetFramework: graphContext.configuration.targetFramework,
        nodeId,
        pageSize: graphContext.configuration.graphPageSize,
        continuationToken
      });
      this.panel.updateGraph(result, true, nodeId);
    } catch (error) {
      this.panel.updateGraph(graphFailure(error instanceof Error ? error.message : String(error)), true);
    }
  }

  public async navigateTarget(target: IlNavigationTarget): Promise<void> {
    if (target.sourcePath && target.sourceRange) {
      await this.openSourceRange(target.sourcePath, target.sourceRange);
      return;
    }

    if (target.id && target.kind === "graphNode") {
      void this.expandGraph(target.id);
    }

    if (!target.decompileAvailable && !target.assemblyPath && !target.assemblyName) {
      vscode.window.showInformationMessage("IL Viewer: ten target nie ma źródła ani dostępnej dekompilacji.");
      return;
    }

    const graphContext = await this.resolveGraphContext(false);
    if (!graphContext) {
      vscode.window.showWarningMessage("IL Viewer: nie ma aktywnego projektu do dekompilacji.");
      return;
    }

    try {
      const result = await this.workerClient.decompile({
        projectPath: graphContext.projectPath,
        configuration: graphContext.configuration.buildConfiguration,
        targetFramework: graphContext.configuration.targetFramework,
        assemblyPath: target.assemblyPath,
        assemblyName: target.assemblyName,
        typeName: target.typeName,
        methodName: target.methodName,
        metadataToken: target.metadataToken,
        language: target.language ?? languageForDocument(graphContext.sourceContext.document)
      });
      if (!result.success) {
        vscode.window.showWarningMessage(`IL Viewer: ${result.error ?? "dekompilacja nie powiodła się."}`);
        return;
      }

      await this.decompiledDocumentProvider.open(result.title, result.content, result.language);
    } catch (error) {
      vscode.window.showWarningMessage(`IL Viewer: ${error instanceof Error ? error.message : String(error)}`);
    }
  }

  private onSelectionChanged(event: vscode.TextEditorSelectionChangeEvent): void {
    if (isSupportedDocument(event.textEditor.document)) {
      this.sourceContextStore.update(event.textEditor.document, event.textEditor.selection.start, event.textEditor.selection.end);
    }

    if (!this.panel.isVisible && !getIlViewerConfiguration().autoOpen) {
      return;
    }

    if (!isSupportedDocument(event.textEditor.document)) {
      return;
    }

    if (!this.panel.isVisible) {
      this.panel.show();
    }

    if (this.debounceTimer) {
      clearTimeout(this.debounceTimer);
    }

    this.debounceTimer = setTimeout(() => {
      void this.updateForEditor(event.textEditor, false);
    }, 150);
  }

  private onActiveEditorChanged(editor: vscode.TextEditor | undefined): void {
    if (!editor || !isSupportedDocument(editor.document)) {
      return;
    }

    this.sourceContextStore.update(editor.document, editor.selection.start, editor.selection.end);

    if (this.panel.isVisible || getIlViewerConfiguration().autoOpen) {
      if (!this.panel.isVisible) {
        this.panel.show();
      }

      void this.updateForEditor(editor, false);
    }
  }

  private async updateForEditor(editor: vscode.TextEditor, allowPrompt: boolean): Promise<void> {
    const selection = editor.selection;
    await this.updateForDocumentPosition(editor.document, selection.start, selection.end, allowPrompt);
  }

  private async updateForSourceContext(sourceContext: SourceContext, allowPrompt: boolean): Promise<void> {
    await this.updateForDocumentPosition(sourceContext.document, sourceContext.start, sourceContext.end, allowPrompt);
  }

  private async updateForDocumentPosition(
    document: vscode.TextDocument,
    start: vscode.Position,
    end: vscode.Position,
    allowPrompt: boolean): Promise<void> {
    if (!isSupportedDocument(document)) {
      return;
    }

    this.sourceContextStore.update(document, start, end);

    if (!this.panel.isVisible && !getIlViewerConfiguration().autoOpen) {
      return;
    }

    if (!this.panel.isVisible) {
      this.panel.show();
    }

    const version = ++this.updateVersion;
    const configuration = getIlViewerConfiguration();
    const projectPath = await this.projectDiscovery.findProjectForDocument(document, allowPrompt);
    if (version !== this.updateVersion) {
      return;
    }

    if (!projectPath) {
      this.panel.update(errorState("Nie znaleziono projektu .NET dla tego pliku.", undefined, document.uri.fsPath));
      return;
    }
    this.sourceContextStore.update(document, start, end, projectPath);

    const request = createAnalyzeRequest(
      document,
      start,
      end,
      projectPath,
      configuration.buildConfiguration,
      configuration.targetFramework);

    this.panel.update(loadingState("Analizuję IL...", projectPath, document.uri.fsPath, request.line, request.endLine));

    try {
      const result = await this.workerClient.analyze(request);
      if (version !== this.updateVersion) {
        return;
      }

      const statusText = result.success
        ? statusForResult(result.message, projectPath, request.line, request.endLine)
        : result.error ?? "Nie udało się znaleźć IL dla tej lokalizacji.";

      this.panel.update({
        status: result.success ? "ready" : "error",
        statusText,
        projectPath,
        documentPath: document.uri.fsPath,
        line: request.line,
        endLine: request.endLine,
        result,
        hoverOverlay: undefined
      });
    } catch (error) {
      if (version !== this.updateVersion) {
        return;
      }

      this.panel.update(errorState(error instanceof Error ? error.message : String(error), projectPath, document.uri.fsPath, request.line, request.endLine));
    }
  }

  private async resolveGraphContext(allowPrompt: boolean): Promise<{ sourceContext: SourceContext; projectPath: string; configuration: ReturnType<typeof getIlViewerConfiguration> } | undefined> {
    const sourceContext = this.getActiveOrStoredSourceContext();
    if (!sourceContext) {
      return undefined;
    }

    const configuration = getIlViewerConfiguration();
    const projectPath = sourceContext.projectPath
      ?? await this.projectDiscovery.findProjectForDocument(sourceContext.document, allowPrompt);
    if (!projectPath) {
      return undefined;
    }

    this.sourceContextStore.update(sourceContext.document, sourceContext.start, sourceContext.end, projectPath);
    return { sourceContext, projectPath, configuration };
  }

  private async openSourceRange(sourcePath: string, sourceRange: SourceRange): Promise<void> {
    const document = await vscode.workspace.openTextDocument(vscode.Uri.file(sourcePath));
    const editor = await vscode.window.showTextDocument(document, vscode.ViewColumn.One, false);
    const range = toVsCodeRange(sourceRange);
    editor.selection = new vscode.Selection(range.start, range.end);
    editor.revealRange(range, vscode.TextEditorRevealType.InCenter);
  }

  private getActiveOrStoredSourceContext(): SourceContext | undefined {
    const editor = vscode.window.activeTextEditor;
    if (editor && isSupportedDocument(editor.document)) {
      return this.sourceContextStore.update(editor.document, editor.selection.start, editor.selection.end);
    }

    return this.sourceContextStore.get();
  }

  private async updateHoverOverlay(document: vscode.TextDocument, position: vscode.Position): Promise<void> {
    if (!this.panel.isVisible || !isSupportedDocument(document)) {
      return;
    }

    const sourceContext = this.sourceContextStore.get();
    const configuration = getIlViewerConfiguration();
    const projectPath = sourceContext?.document.uri.fsPath === document.uri.fsPath && sourceContext.projectPath
      ? sourceContext.projectPath
      : await this.projectDiscovery.findProjectForDocument(document, false);
    if (!projectPath) {
      return;
    }

    const version = ++this.hoverVersion;
    const request = createAnalyzeRequest(
      document,
      position,
      new vscode.Position(position.line, position.character + 1),
      projectPath,
      configuration.buildConfiguration,
      configuration.targetFramework);

    try {
      const result = await this.workerClient.analyzeOverlay(request);
      if (version !== this.hoverVersion || !result.success) {
        return;
      }

      this.panel.updateHoverOverlay(toHoverOverlay(result));
    } catch {
      // Hover is an optional overlay; avoid noisy notifications while the user moves the mouse.
    }
  }

}

function isSupportedDocument(document: vscode.TextDocument): boolean {
  return document.uri.scheme === "file" && SupportedLanguageIds.has(document.languageId);
}

function idleState(statusText: string, projectPath?: string): PanelState {
  return {
    status: "idle",
    statusText,
    projectPath
  };
}

function loadingState(statusText: string, projectPath?: string, documentPath?: string, line?: number, endLine?: number): PanelState {
  return {
    status: "loading",
    statusText,
    projectPath,
    documentPath,
    line,
    endLine
  };
}

function errorState(statusText: string, projectPath?: string, documentPath?: string, line?: number, endLine?: number): PanelState {
  return {
    status: "error",
    statusText,
    projectPath,
    documentPath,
    line,
    endLine
  };
}

function statusForResult(message: string | undefined, projectPath: string, line: number, endLine: number): string {
  const range = line === endLine ? `linia ${line}` : `linie ${line}-${endLine}`;
  const base = `Gotowe: ${range}, ${vscode.workspace.asRelativePath(projectPath)}`;
  return message ? `${base}. ${message}` : base;
}

function graphFailure(error: string): GraphExpandResult {
  return {
    success: false,
    error,
    nodes: [],
    edges: [],
    diagnostics: []
  };
}

function toVsCodeRange(sourceRange: SourceRange): vscode.Range {
  const start = new vscode.Position(Math.max(sourceRange.startLine - 1, 0), Math.max(sourceRange.startColumn - 1, 0));
  const end = new vscode.Position(Math.max(sourceRange.endLine - 1, 0), Math.max(sourceRange.endColumn - 1, 0));
  return new vscode.Range(start, end);
}

function languageForDocument(document: vscode.TextDocument): string {
  if (document.languageId === "vb") {
    return "vb";
  }

  return "csharp";
}

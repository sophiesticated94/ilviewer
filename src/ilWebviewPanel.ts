import * as vscode from "vscode";
import { GraphExpandResult, HoverOverlay, IlNavigationTarget, PanelState } from "./types";
import { getIlWebviewHtml } from "./webview/ilWebviewHtml";

export interface PanelCallbacks {
  rebuild(): void;
  refresh(): void;
  selectProject(): void;
  openGraph(): void;
  expandGraph(nodeId: string, continuationToken?: string): void;
  navigateTarget(target: IlNavigationTarget): void;
}

export class IlWebviewPanel {
  private panel?: vscode.WebviewPanel;
  private isReady = false;
  private pendingHoverOverlay?: HoverOverlay;
  private lastState: PanelState = {
    status: "idle",
    statusText: "Otwórz plik C#, F# lub VB.NET i uruchom Przebuduj."
  };

  public constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly callbacks: PanelCallbacks
  ) {}

  public get isVisible(): boolean {
    return this.panel !== undefined;
  }

  public show(): void {
    if (this.panel) {
      this.panel.reveal(vscode.ViewColumn.Beside, true);
      this.postCurrentState();
      return;
    }

    this.isReady = false;
    this.panel = vscode.window.createWebviewPanel(
      "ilviewer.panel",
      "IL Viewer",
      vscode.ViewColumn.Beside,
      {
        enableScripts: true,
        retainContextWhenHidden: true
      });

    this.panel.webview.html = getIlWebviewHtml(this.panel.webview, createNonce());
    this.panel.webview.onDidReceiveMessage(message => this.handleMessage(message));
    this.panel.onDidDispose(() => {
      this.panel = undefined;
      this.isReady = false;
      this.pendingHoverOverlay = undefined;
    }, undefined, this.context.subscriptions);

    this.postCurrentState();
  }

  public update(state: PanelState): void {
    this.lastState = state;
    this.pendingHoverOverlay = state.hoverOverlay;
    this.postCurrentState();
  }

  public updateHoverOverlay(overlay: HoverOverlay): void {
    this.pendingHoverOverlay = overlay;
    if (!this.panel || !this.isReady) {
      return;
    }

    this.panel?.webview.postMessage({
      type: "hoverOverlay",
      overlay
    });
  }

  public updateGraph(result: GraphExpandResult, append: boolean, nodeId?: string): void {
    this.panel?.webview.postMessage({
      type: "graph",
      result,
      append,
      nodeId
    });
  }

  private handleMessage(message: { command?: string; nodeId?: string; continuationToken?: string; target?: IlNavigationTarget }): void {
    switch (message?.command) {
      case "ready":
        this.isReady = true;
        this.postCurrentState();
        break;
      case "rebuild":
        this.callbacks.rebuild();
        break;
      case "refresh":
        this.callbacks.refresh();
        break;
      case "selectProject":
        this.callbacks.selectProject();
        break;
      case "openGraph":
        this.callbacks.openGraph();
        break;
      case "expandGraph":
        if (message.nodeId) {
          this.callbacks.expandGraph(message.nodeId, message.continuationToken);
        }
        break;
      case "navigateTarget":
        if (message.target) {
          this.callbacks.navigateTarget(message.target);
        }
        break;
    }
  }

  private postCurrentState(): void {
    if (!this.panel || !this.isReady) {
      return;
    }

    this.panel.webview.postMessage({
      type: "state",
      state: this.lastState
    });

    if (this.pendingHoverOverlay) {
      this.panel.webview.postMessage({
        type: "hoverOverlay",
        overlay: this.pendingHoverOverlay
      });
    }
  }
}

function createNonce(): string {
  const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
  let value = "";
  for (let index = 0; index < 32; index++) {
    value += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return value;
}

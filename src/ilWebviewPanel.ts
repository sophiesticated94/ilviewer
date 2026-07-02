import * as vscode from "vscode";
import { HoverOverlay, PanelState } from "./types";
import { getIlWebviewHtml } from "./webview/ilWebviewHtml";

export interface PanelCallbacks {
  rebuild(): void;
  refresh(): void;
  selectProject(): void;
}

export class IlWebviewPanel {
  private panel?: vscode.WebviewPanel;
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
      this.update(this.lastState);
      return;
    }

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
    }, undefined, this.context.subscriptions);

    this.update(this.lastState);
  }

  public update(state: PanelState): void {
    this.lastState = state;
    this.panel?.webview.postMessage({
      type: "state",
      state
    });
  }

  public updateHoverOverlay(overlay: HoverOverlay): void {
    this.panel?.webview.postMessage({
      type: "hoverOverlay",
      overlay
    });
  }

  private handleMessage(message: { command?: string }): void {
    switch (message?.command) {
      case "rebuild":
        this.callbacks.rebuild();
        break;
      case "refresh":
        this.callbacks.refresh();
        break;
      case "selectProject":
        this.callbacks.selectProject();
        break;
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

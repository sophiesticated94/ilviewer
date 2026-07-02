import * as vscode from "vscode";

export interface SourceContext {
  document: vscode.TextDocument;
  start: vscode.Position;
  end: vscode.Position;
  projectPath?: string;
  updatedAt: number;
}

export class SourceContextStore {
  private current?: SourceContext;

  public update(document: vscode.TextDocument, start: vscode.Position, end: vscode.Position, projectPath?: string): SourceContext {
    this.current = {
      document,
      start,
      end,
      projectPath: projectPath ?? this.current?.projectPath,
      updatedAt: Date.now()
    };

    return this.current;
  }

  public updateProject(projectPath: string): void {
    if (!this.current) {
      return;
    }

    this.current = {
      ...this.current,
      projectPath,
      updatedAt: Date.now()
    };
  }

  public get(): SourceContext | undefined {
    return this.current;
  }
}

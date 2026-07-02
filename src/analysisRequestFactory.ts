import * as vscode from "vscode";
import { AnalyzeRequest } from "./types";

export function createAnalyzeRequest(
  document: vscode.TextDocument,
  start: vscode.Position,
  end: vscode.Position,
  projectPath: string,
  configuration: string,
  targetFramework: string | undefined): AnalyzeRequest {
  const line = start.line + 1;
  const endLine = end.line + 1;
  const startColumn = start.character + 1;
  const endColumn = end.character + 1;

  return {
    projectPath,
    documentPath: document.uri.fsPath,
    line: Math.min(line, endLine),
    endLine: Math.max(line, endLine),
    configuration,
    targetFramework,
    startColumn: line <= endLine ? startColumn : endColumn,
    endColumn: line <= endLine ? endColumn : startColumn
  };
}

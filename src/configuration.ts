import * as path from "path";
import * as vscode from "vscode";
import { IlViewerConfiguration } from "./types";

export function getIlViewerConfiguration(): IlViewerConfiguration {
  const configuration = vscode.workspace.getConfiguration("ilviewer");
  const targetFramework = normalizeOptional(configuration.get<string>("targetFramework"));
  const configuredProjectPath = normalizeOptional(configuration.get<string>("projectPath"));

  return {
    dotnetPath: configuration.get<string>("dotnetPath")?.trim() || "dotnet",
    buildConfiguration: configuration.get<string>("buildConfiguration")?.trim() || "Debug",
    targetFramework,
    projectPath: configuredProjectPath ? resolveWorkspacePath(configuredProjectPath) : undefined,
    autoOpen: configuration.get<boolean>("autoOpen") ?? false,
    graphPageSize: Math.max(configuration.get<number>("graphPageSize") ?? 250, 25)
  };
}

function normalizeOptional(value: string | undefined): string | undefined {
  const normalized = value?.trim();
  return normalized ? normalized : undefined;
}

function resolveWorkspacePath(value: string): string {
  if (path.isAbsolute(value)) {
    return value;
  }

  const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
  return workspaceFolder ? path.join(workspaceFolder.uri.fsPath, value) : value;
}

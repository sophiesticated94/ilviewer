import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";
import { getIlViewerConfiguration } from "./configuration";

const ProjectGlobPatterns = ["**/*.csproj", "**/*.fsproj", "**/*.vbproj"];
const ProjectExcludePattern = "**/{bin,obj,node_modules,.git}/**";

export class ProjectDiscovery {
  private readonly selectedProjectKey = "ilviewer.selectedProjectPath";

  public constructor(private readonly context: vscode.ExtensionContext) {}

  public async findProjectForDocument(document: vscode.TextDocument, allowPrompt: boolean): Promise<string | undefined> {
    const configuredProjectPath = getIlViewerConfiguration().projectPath;
    if (configuredProjectPath && fs.existsSync(configuredProjectPath)) {
      return configuredProjectPath;
    }

    const selectedProjectPath = this.context.workspaceState.get<string>(this.selectedProjectKey);
    if (selectedProjectPath && fs.existsSync(selectedProjectPath)) {
      return selectedProjectPath;
    }

    const projects = await this.findProjects();
    if (projects.length === 0) {
      return undefined;
    }

    const documentPath = document.uri.fsPath;
    const rankedProjects = projects
      .map(projectPath => ({
        projectPath,
        score: scoreProject(documentPath, projectPath)
      }))
      .sort((left, right) => right.score - left.score || left.projectPath.localeCompare(right.projectPath));

    const best = rankedProjects[0];
    const second = rankedProjects[1];
    if (best && best.score >= 0 && (!second || best.score > second.score)) {
      await this.context.workspaceState.update(this.selectedProjectKey, best.projectPath);
      return best.projectPath;
    }

    return allowPrompt ? this.selectProject(projects) : undefined;
  }

  public async selectProject(projects?: string[]): Promise<string | undefined> {
    const availableProjects = projects ?? await this.findProjects();
    if (availableProjects.length === 0) {
      vscode.window.showWarningMessage("IL Viewer: nie znaleziono projektu .NET w workspace.");
      return undefined;
    }

    const selected = await vscode.window.showQuickPick(
      availableProjects.map(projectPath => ({
        label: path.basename(projectPath),
        description: vscode.workspace.asRelativePath(projectPath),
        projectPath
      })),
      {
        title: "IL Viewer: wybierz projekt .NET",
        placeHolder: "Projekt używany do builda i mapowania IL"
      });

    if (!selected) {
      return undefined;
    }

    await this.context.workspaceState.update(this.selectedProjectKey, selected.projectPath);
    return selected.projectPath;
  }

  private async findProjects(): Promise<string[]> {
    const found = await Promise.all(
      ProjectGlobPatterns.map(pattern => vscode.workspace.findFiles(pattern, ProjectExcludePattern))
    );

    return [...new Set(found.flat().map(uri => uri.fsPath))]
      .filter(projectPath => fs.existsSync(projectPath))
      .sort((left, right) => left.localeCompare(right));
  }
}

function scoreProject(documentPath: string, projectPath: string): number {
  const projectDirectory = path.dirname(projectPath);
  const relative = path.relative(projectDirectory, documentPath);

  if (!relative.startsWith("..") && !path.isAbsolute(relative)) {
    return projectDirectory.length;
  }

  return -1;
}

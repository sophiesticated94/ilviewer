import * as path from "path";
import * as vscode from "vscode";
import { getIlViewerConfiguration } from "./configuration";
import { runProcess } from "./processRunner";
import { ProcessResult } from "./types";

export class DotnetBuildService {
  public constructor(private readonly outputChannel: vscode.OutputChannel) {}

  public async build(projectPath: string): Promise<ProcessResult> {
    const configuration = getIlViewerConfiguration();
    const args = ["build", projectPath, "-c", configuration.buildConfiguration];

    if (configuration.targetFramework) {
      args.push("-f", configuration.targetFramework);
    }

    this.outputChannel.appendLine(`> ${configuration.dotnetPath} ${args.join(" ")}`);
    const result = await runProcess(configuration.dotnetPath, args, path.dirname(projectPath));
    this.outputChannel.append(result.stdout);
    this.outputChannel.append(result.stderr);
    return result;
  }
}

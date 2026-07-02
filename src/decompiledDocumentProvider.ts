import * as vscode from "vscode";

const Scheme = "ilviewer-decompiled";

export class DecompiledDocumentProvider implements vscode.TextDocumentContentProvider {
  private readonly documents = new Map<string, string>();
  private readonly changeEmitter = new vscode.EventEmitter<vscode.Uri>();

  public readonly onDidChange = this.changeEmitter.event;

  public register(context: vscode.ExtensionContext): void {
    context.subscriptions.push(vscode.workspace.registerTextDocumentContentProvider(Scheme, this));
  }

  public provideTextDocumentContent(uri: vscode.Uri): string {
    return this.documents.get(uri.toString()) ?? "";
  }

  public async open(title: string, content: string, language: string): Promise<void> {
    const uri = vscode.Uri.from({
      scheme: Scheme,
      path: "/" + sanitizePath(title) + extensionForLanguage(language),
      query: Date.now().toString()
    });
    this.documents.set(uri.toString(), content);
    this.changeEmitter.fire(uri);
    const document = await vscode.workspace.openTextDocument(uri);
    const typedDocument = await vscode.languages.setTextDocumentLanguage(document, normalizeLanguage(language));
    await vscode.window.showTextDocument(typedDocument, vscode.ViewColumn.Beside, false);
  }
}

function sanitizePath(value: string): string {
  return value.replace(/[\\/:*?"<>|]+/g, "_").slice(0, 120) || "decompiled";
}

function normalizeLanguage(language: string): string {
  if (language === "plaintext") {
    return "plaintext";
  }

  if (language === "vb") {
    return "vb";
  }

  return "csharp";
}

function extensionForLanguage(language: string): string {
  if (language === "vb") {
    return ".vb";
  }

  if (language === "plaintext") {
    return ".txt";
  }

  return ".cs";
}

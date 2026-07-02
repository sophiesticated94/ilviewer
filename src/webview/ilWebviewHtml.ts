import * as vscode from "vscode";
import { getIlWebviewScript } from "./ilWebviewScript";
import { ilWebviewStyles } from "./ilWebviewStyles";

export function getIlWebviewHtml(webview: vscode.Webview, nonce: string): string {
  const csp = [
    "default-src 'none'",
    `style-src ${webview.cspSource} 'unsafe-inline'`,
    `script-src 'nonce-${nonce}'`
  ].join("; ");

  return /* html */ `<!DOCTYPE html>
<html lang="pl">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="${csp}">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>IL Viewer</title>
  <style>${ilWebviewStyles}</style>
</head>
<body>
  <div class="toolbar">
    <button data-command="rebuild" title="Uruchom dotnet build dla wybranego projektu">Przebuduj</button>
    <button class="secondary" data-command="refresh" title="Wyczyść cache i ponownie przeanalizuj aktualne zaznaczenie">Odśwież</button>
    <button class="secondary" data-command="selectProject" title="Wybierz projekt .NET">Projekt</button>
    <button class="secondary" data-command="openGraph" title="Pokaż lazy graf aplikacji z referencjami NuGet i framework">Graf</button>
    <button id="explainButton" class="secondary" title="Pokaż wyjaśnienia instrukcji IL w aktywnym kontekście">Wyjaśnij IL</button>
    <div id="status" class="status"></div>
  </div>
  <main>
    <section>
      <h2>Fragment</h2>
      <div id="fragmentMeta" class="meta"></div>
      <div id="regions" class="regions"></div>
      <pre id="fragment"></pre>
    </section>
    <section class="context-section">
      <h2>Kontekst IL</h2>
      <div id="contextMeta" class="meta"></div>
      <div id="tabs" class="tabs"></div>
      <div id="tree" class="tree"></div>
      <div id="graph" class="graph"></div>
      <pre id="context"></pre>
    </section>
  </main>
  <div id="explainModal" class="modal" role="dialog" aria-modal="true">
    <div class="modal-card">
      <div class="modal-header">
        <div class="modal-title">Wyjaśnij IL</div>
        <button id="closeExplain" class="secondary">Zamknij</button>
      </div>
      <div id="explainBody" class="modal-body"></div>
    </div>
  </div>
  <script nonce="${nonce}">${getIlWebviewScript()}</script>
</body>
</html>`;
}

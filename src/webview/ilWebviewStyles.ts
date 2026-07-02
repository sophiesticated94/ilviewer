export const ilWebviewStyles = String.raw`
:root {
  color-scheme: light dark;
  --gap: 10px;
  --depth-0: rgba(88, 166, 255, 0.28);
  --depth-1: rgba(63, 185, 80, 0.26);
  --depth-2: rgba(210, 153, 34, 0.28);
  --depth-3: rgba(248, 81, 73, 0.24);
  --depth-4: rgba(163, 113, 247, 0.25);
  --hover-il: rgba(255, 214, 102, 0.34);
  --hover-il-border: rgba(255, 214, 102, 0.96);
}

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  color: var(--vscode-foreground);
  background: var(--vscode-editor-background);
  font-family: var(--vscode-font-family);
  font-size: var(--vscode-font-size);
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 38px;
  padding: 6px 8px;
  border-bottom: 1px solid var(--vscode-panel-border);
  background: var(--vscode-sideBar-background);
}

button {
  min-height: 26px;
  padding: 3px 9px;
  color: var(--vscode-button-foreground);
  background: var(--vscode-button-background);
  border: 1px solid transparent;
  border-radius: 3px;
  font: inherit;
  cursor: pointer;
}

button.secondary,
.tab,
.tree-button,
.region-chip {
  color: var(--vscode-button-secondaryForeground);
  background: var(--vscode-button-secondaryBackground);
}

button:hover,
.tab:hover,
.tree-button:hover,
.region-chip:hover {
  background: var(--vscode-button-hoverBackground);
}

.status {
  min-width: 0;
  margin-left: auto;
  overflow: hidden;
  color: var(--vscode-descriptionForeground);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.status .hover-status {
  color: var(--hover-il-border);
}

.tabs {
  display: flex;
  gap: 4px;
  padding: 8px 10px 0;
  overflow-x: auto;
}

.tab {
  flex: 0 0 auto;
  min-height: 24px;
  padding: 2px 8px;
  border: 1px solid var(--vscode-panel-border);
  border-radius: 3px;
}

.tab.active {
  color: var(--vscode-button-foreground);
  background: var(--vscode-button-background);
}

main {
  display: grid;
  grid-template-columns: minmax(280px, 0.9fr) minmax(440px, 1.6fr);
  gap: var(--gap);
  height: calc(100vh - 38px);
  padding: var(--gap);
}

section {
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-rows: auto auto minmax(0, 1fr);
  border: 1px solid var(--vscode-panel-border);
  background: var(--vscode-editor-background);
}

.context-section {
  grid-template-rows: auto auto auto auto minmax(0, 1fr);
}

h2 {
  margin: 0;
  padding: 8px 10px 3px;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0;
}

.meta {
  padding: 0 10px 7px;
  color: var(--vscode-descriptionForeground);
  font-size: 11px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.regions {
  display: flex;
  flex-wrap: wrap;
  gap: 5px;
  max-height: 90px;
  overflow: auto;
  padding: 0 10px 8px;
  border-bottom: 1px solid var(--vscode-panel-border);
}

.region-chip {
  min-height: 22px;
  max-width: 100%;
  padding: 2px 7px;
  border: 1px solid var(--vscode-panel-border);
  border-left-width: 4px;
  border-radius: 3px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.region-chip.active {
  outline: 1px solid var(--vscode-focusBorder);
}

.tree {
  display: flex;
  gap: 5px;
  max-height: 112px;
  overflow: auto;
  padding: 0 10px 8px;
  border-bottom: 1px solid var(--vscode-panel-border);
}

.tree-column {
  min-width: 180px;
  max-width: 260px;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.tree-title {
  color: var(--vscode-descriptionForeground);
  font-size: 11px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tree-button {
  min-height: 23px;
  border: 1px solid var(--vscode-panel-border);
  border-radius: 3px;
  overflow: hidden;
  text-align: left;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tree-button.active {
  color: var(--vscode-button-foreground);
  background: var(--vscode-button-background);
}

.graph {
  display: flex;
  gap: 5px;
  max-height: 142px;
  overflow: auto;
  padding: 0 10px 8px;
  border-bottom: 1px solid var(--vscode-panel-border);
}

.graph-column {
  min-width: 210px;
  max-width: 360px;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.graph-title {
  color: var(--vscode-descriptionForeground);
  font-size: 11px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.graph-button,
.graph-more {
  min-height: 23px;
  border: 1px solid var(--vscode-panel-border);
  border-radius: 3px;
  color: var(--vscode-button-secondaryForeground);
  background: var(--vscode-button-secondaryBackground);
  overflow: hidden;
  text-align: left;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.graph-button:hover,
.graph-more:hover {
  background: var(--vscode-button-hoverBackground);
}

.graph-kind {
  color: var(--vscode-descriptionForeground);
}

pre {
  min-height: 0;
  margin: 0;
  padding: 8px 0;
  overflow: auto;
  font-family: var(--vscode-editor-font-family);
  font-size: var(--vscode-editor-font-size);
  line-height: 1.45;
  user-select: text;
}

.line {
  display: block;
  width: max-content;
  min-width: 100%;
  min-height: 1.45em;
  padding: 0 10px;
  border-left: 3px solid transparent;
  white-space: pre;
}

.line.clickable {
  cursor: pointer;
}

.line.active,
.line.depth-0 {
  background: var(--depth-0);
  border-left-color: rgba(88, 166, 255, 0.92);
}

.line.depth-1 {
  background: var(--depth-1);
  border-left-color: rgba(63, 185, 80, 0.92);
}

.line.depth-2 {
  background: var(--depth-2);
  border-left-color: rgba(210, 153, 34, 0.92);
}

.line.depth-3 {
  background: var(--depth-3);
  border-left-color: rgba(248, 81, 73, 0.86);
}

.line.depth-4,
.line.depth-more {
  background: var(--depth-4);
  border-left-color: rgba(163, 113, 247, 0.88);
}

.line.hover {
  background: var(--hover-il);
  border-left-color: var(--hover-il-border);
  outline: 1px solid var(--hover-il-border);
  outline-offset: -1px;
}

.token.offset {
  color: var(--vscode-symbolIcon-numberForeground);
}

.token.opcode {
  color: var(--vscode-symbolIcon-functionForeground);
  font-weight: 600;
}

.token.operand {
  color: var(--vscode-editor-foreground);
}

.token.literal {
  color: var(--vscode-symbolIcon-stringForeground);
}

.token.number,
.token.target {
  color: var(--vscode-symbolIcon-numberForeground);
}

.token.signature {
  color: var(--vscode-textLink-foreground);
}

.token.punctuation {
  color: var(--vscode-descriptionForeground);
}

.nav-target {
  display: inline-block;
  margin-left: 8px;
  padding: 0 5px;
  color: var(--vscode-textLink-foreground);
  background: transparent;
  border: 1px solid var(--vscode-textLink-foreground);
  border-radius: 3px;
  font-size: 0.88em;
  line-height: 1.2;
  cursor: pointer;
}

.empty {
  padding: 14px 10px;
  color: var(--vscode-descriptionForeground);
  white-space: normal;
}

.error {
  color: var(--vscode-errorForeground);
}

.modal {
  position: fixed;
  inset: 0;
  display: none;
  background: rgba(0, 0, 0, 0.38);
  z-index: 10;
}

.modal.open {
  display: grid;
  place-items: center;
}

.modal-card {
  width: min(860px, calc(100vw - 36px));
  max-height: min(720px, calc(100vh - 36px));
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  border: 1px solid var(--vscode-panel-border);
  background: var(--vscode-editor-background);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.32);
}

.modal-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  border-bottom: 1px solid var(--vscode-panel-border);
}

.modal-title {
  font-weight: 600;
}

.modal-body {
  overflow: auto;
  padding: 8px 10px 12px;
}

.explanation {
  padding: 8px 0;
  border-bottom: 1px solid var(--vscode-panel-border);
}

.explanation code {
  color: var(--vscode-textLink-foreground);
}

@media (max-width: 820px) {
  main {
    grid-template-columns: minmax(0, 1fr);
    grid-template-rows: minmax(260px, 1fr) minmax(320px, 1.2fr);
  }
}
`;

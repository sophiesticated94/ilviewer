import { tokenizeIlText } from "./ilTokenizer";

export function getIlWebviewScript(): string {
  return String.raw`
const tokenizeIlText = ${tokenizeIlText.toString()};
const vscode = acquireVsCodeApi();
const scopeOrder = ["fragment", "function", "class", "typeWithNested", "project", "application"];
const scopeLabels = {
  fragment: "Fragment",
  function: "Funkcja",
  class: "Klasa",
  typeWithNested: "Typ + zagnieżdżone",
  project: "Projekt",
  application: "Aplikacja"
};
let currentState;
let hoverOverlay;
let selectedScopeKind = "function";
let selectedMethodId;
let selectedRegionId;
let graphNodes = new Map();
let graphEdges = [];

const statusEl = document.getElementById("status");
const fragmentEl = document.getElementById("fragment");
const contextEl = document.getElementById("context");
const fragmentMetaEl = document.getElementById("fragmentMeta");
const contextMetaEl = document.getElementById("contextMeta");
const regionsEl = document.getElementById("regions");
const tabsEl = document.getElementById("tabs");
const treeEl = document.getElementById("tree");
const graphEl = document.getElementById("graph");
const explainButton = document.getElementById("explainButton");
const explainModal = document.getElementById("explainModal");
const explainBody = document.getElementById("explainBody");
const closeExplain = document.getElementById("closeExplain");

document.querySelectorAll("button[data-command]").forEach(button => {
  button.addEventListener("click", () => {
    vscode.postMessage({ command: button.dataset.command });
  });
});

explainButton.addEventListener("click", () => {
  renderExplanations();
  explainModal.classList.add("open");
});

closeExplain.addEventListener("click", () => explainModal.classList.remove("open"));
explainModal.addEventListener("click", event => {
  if (event.target === explainModal) {
    explainModal.classList.remove("open");
  }
});

window.addEventListener("message", event => {
  if (event.data?.type === "state") {
    currentState = event.data.state;
    hoverOverlay = currentState.hoverOverlay;
    render(currentState);
  }

  if (event.data?.type === "hoverOverlay") {
    hoverOverlay = event.data.overlay;
    render(currentState);
  }

  if (event.data?.type === "graph") {
    renderGraph(event.data.result, event.data.append, event.data.nodeId);
  }
});

vscode.postMessage({ command: "ready" });

function render(state) {
  renderStatus(state);
  const result = normalizeResult(state?.result);
  if (!result) {
    clearPanel("Brak IL dla aktywnego fragmentu.", "Brak kontekstu IL.");
    return;
  }

  if (!selectedRegionId) {
    selectedRegionId = result.selectedRegionId;
  }

  const fragmentScope = findScope(result, "fragment") || findScope(result, "function");
  const activeScope = findScope(result, selectedScopeKind) || findScope(result, "function") || fragmentScope;
  selectedScopeKind = activeScope?.kind || "function";

  renderTabs(result);
  renderRegions(result);
  renderTree(activeScope);

  const location = state.line ? "Linia " + state.line + (state.endLine && state.endLine !== state.line ? "-" + state.endLine : "") : "";
  fragmentMetaEl.textContent = fragmentScope ? [location, fragmentScope.fullName || fragmentScope.displayName].filter(Boolean).join(" | ") : "";
  contextMetaEl.textContent = activeScope ? [activeScope.displayName, activeScope.fullName, sourceRangeText(activeScope.sourceRange)].filter(Boolean).join(" | ") : "";

  renderScope(fragmentEl, fragmentScope, true);
  renderScope(contextEl, activeScope, false);
  renderStatus(state);
}

function renderStatus(state) {
  statusEl.replaceChildren();
  const base = document.createElement("span");
  base.textContent = state?.statusText || "";
  base.className = state?.status === "error" ? "error" : "";
  statusEl.appendChild(base);

  const visibleHoverCount = countVisibleHoverInstructions();
  if (hoverOverlay && hoverOverlay.instructionIds?.length > 0) {
    const hover = document.createElement("span");
    hover.className = "hover-status";
    hover.textContent = visibleHoverCount > 0
      ? " | " + hoverOverlay.statusText
      : " | Hover poza aktywnym kontekstem";
    statusEl.appendChild(hover);
  }
}

function clearPanel(fragmentText, contextText) {
  renderEmpty(fragmentEl, fragmentText);
  renderEmpty(contextEl, contextText);
  fragmentMetaEl.textContent = "";
  contextMetaEl.textContent = "";
  regionsEl.replaceChildren();
  tabsEl.replaceChildren();
  treeEl.replaceChildren();
  graphEl.replaceChildren();
  graphNodes = new Map();
  graphEdges = [];
}

function normalizeResult(result) {
  if (!result) {
    return undefined;
  }

  if (Array.isArray(result.scopes) && result.scopes.length > 0) {
    return result;
  }

  const scopes = [];
  if (result.fragment) {
    scopes.push({
      id: "fragment",
      kind: "fragment",
      displayName: "Fragment",
      fullName: result.fragment.fullName,
      methods: [{
        id: "fragment-method",
        assemblyName: "",
        typeName: result.fragment.typeName,
        methodName: result.fragment.methodName,
        fullName: result.fragment.fullName,
        sourceRange: result.fragment.sourceRange,
        instructions: result.fragment.instructions,
        containsActiveInstruction: true
      }],
      instructions: result.fragment.instructions,
      activeInstructionIds: result.fragment.instructions.filter(i => i.isActive).map(i => i.id),
      activeHighlightIds: []
    });
  }
  if (result.context) {
    scopes.push({
      id: "function",
      kind: "function",
      displayName: "Funkcja",
      fullName: result.context.fullName,
      methods: [{
        id: "context-method",
        assemblyName: "",
        typeName: result.context.typeName,
        methodName: result.context.methodName,
        fullName: result.context.fullName,
        sourceRange: result.context.sourceRange,
        instructions: result.context.instructions,
        containsActiveInstruction: true
      }],
      instructions: result.context.instructions,
      activeInstructionIds: result.context.instructions.filter(i => i.isActive).map(i => i.id),
      activeHighlightIds: []
    });
  }

  return { ...result, scopes, sourceRegions: [], instructionHighlights: [], instructionExplanations: [] };
}

function renderTabs(result) {
  tabsEl.replaceChildren();
  const available = new Set(result.scopes.map(scope => scope.kind));
  for (const kind of scopeOrder) {
    if (!available.has(kind)) {
      continue;
    }
    const tab = document.createElement("button");
    tab.className = kind === selectedScopeKind ? "tab active" : "tab";
    tab.textContent = scopeLabels[kind] || kind;
    tab.addEventListener("click", () => {
      selectedScopeKind = kind;
      selectedMethodId = undefined;
      render(currentState);
    });
    tabsEl.appendChild(tab);
  }
}

function renderRegions(result) {
  regionsEl.replaceChildren();
  if (!result.sourceRegions || result.sourceRegions.length === 0) {
    const empty = document.createElement("span");
    empty.className = "empty";
    empty.textContent = "Brak regionów składniowych dla tego języka lub zaznaczenia.";
    regionsEl.appendChild(empty);
    return;
  }

  for (const region of result.sourceRegions) {
    const chip = document.createElement("button");
    chip.className = "region-chip" + (region.id === selectedRegionId ? " active" : "");
    chip.style.borderLeftColor = depthColor(region.depth);
    chip.title = sourceRangeText(region.sourceRange);
    chip.textContent = region.displayName;
    chip.addEventListener("click", () => {
      selectedRegionId = region.id;
      render(currentState);
    });
    regionsEl.appendChild(chip);
  }
}

function renderTree(scope) {
  treeEl.replaceChildren();
  if (!scope || !scope.methods || scope.methods.length <= 1) {
    return;
  }

  const byType = new Map();
  for (const method of scope.methods) {
    const key = method.assemblyName + " | " + method.typeName;
    if (!byType.has(key)) {
      byType.set(key, []);
    }
    byType.get(key).push(method);
  }

  for (const [key, methods] of byType) {
    const column = document.createElement("div");
    column.className = "tree-column";
    const title = document.createElement("div");
    title.className = "tree-title";
    title.textContent = key;
    column.appendChild(title);

    for (const method of methods) {
      const button = document.createElement("button");
      const active = getSelectedMethod(scope)?.id === method.id;
      button.className = active ? "tree-button active" : "tree-button";
      button.textContent = method.methodName;
      button.title = method.fullName;
      button.addEventListener("click", () => {
        selectedMethodId = method.id;
        render(currentState);
      });
      column.appendChild(button);
    }

    treeEl.appendChild(column);
  }
}

function renderScope(container, scope, renderAllMethods) {
  if (!scope) {
    renderEmpty(container, "Brak danych IL.");
    return;
  }

  const methods = renderAllMethods ? scope.methods : [getSelectedMethod(scope)].filter(Boolean);
  const instructions = methods.flatMap(method => method.instructions || []);
  renderInstructions(container, instructions, "Brak instrukcji IL w aktywnym kontekście.");
}

function getSelectedMethod(scope) {
  if (!scope || !scope.methods || scope.methods.length === 0) {
    return undefined;
  }

  if (selectedMethodId) {
    const selected = scope.methods.find(method => method.id === selectedMethodId);
    if (selected) {
      return selected;
    }
  }

  return scope.methods.find(method => method.containsActiveInstruction) || scope.methods[0];
}

function renderInstructions(container, instructions, emptyText) {
  container.replaceChildren();
  if (!instructions || instructions.length === 0) {
    renderEmpty(container, emptyText);
    return;
  }

  const highlightMap = buildHighlightMap();
  const hoverSet = new Set(hoverOverlay?.instructionIds || []);
  let firstHighlighted;
  for (const instruction of instructions) {
    const line = document.createElement("span");
    const depth = highlightMap.get(instruction.id);
    const isHover = hoverSet.has(instruction.id);
    line.className = buildLineClass(instruction, depth, isHover);
    line.dataset.instructionId = instruction.id;
    line.title = buildInstructionTitle(instruction);
    appendInstructionTokens(line, instruction.text);
    appendNavigationTargets(line, instruction.navigationTargets || []);
    container.appendChild(line);
    if ((isHover || instruction.isActive || depth !== undefined) && !firstHighlighted) {
      firstHighlighted = line;
    }
  }

  if (firstHighlighted) {
    requestAnimationFrame(() => firstHighlighted.scrollIntoView({ block: "center", inline: "nearest" }));
  }
}

function appendInstructionTokens(line, text) {
  for (const token of tokenizeIlText(text)) {
    const span = document.createElement("span");
    span.className = "token " + token.kind;
    span.textContent = token.text;
    line.appendChild(span);
  }
}

function appendNavigationTargets(line, targets) {
  if (!targets || targets.length === 0) {
    return;
  }

  line.classList.add("clickable");
  line.addEventListener("click", event => {
    if (event.target?.classList?.contains("nav-target")) {
      return;
    }
    handleNavigationTarget(targets[0]);
  });

  const primary = targets[0];
  const button = document.createElement("button");
  button.className = "nav-target";
  button.textContent = targetLabel(primary);
  button.title = targets.map(target => target.label).join("\\n");
  button.addEventListener("click", event => {
    event.stopPropagation();
    handleNavigationTarget(primary);
  });
  line.appendChild(button);
}

function handleNavigationTarget(target) {
  if (target.kind === "il" && target.targetInstructionId) {
    scrollToInstruction(target.targetInstructionId);
    return;
  }

  vscode.postMessage({ command: "navigateTarget", target });
}

function scrollToInstruction(instructionId) {
  const lines = document.querySelectorAll("[data-instruction-id]");
  for (const line of lines) {
    if (line.dataset.instructionId === instructionId) {
      line.classList.add("hover");
      line.scrollIntoView({ block: "center", inline: "nearest" });
      setTimeout(() => line.classList.remove("hover"), 1200);
      return;
    }
  }
}

function targetLabel(target) {
  if (target.kind === "source") {
    return "źródło";
  }
  if (target.kind === "il") {
    return "IL";
  }
  if (target.assemblyKind === "nuget") {
    return "NuGet";
  }
  if (target.assemblyKind === "framework" || target.assemblyKind === "runtime") {
    return "runtime";
  }
  return "kod";
}

function buildLineClass(instruction, depth, isHover) {
  const classes = ["line"];
  if (instruction.isActive) {
    classes.push("active");
  }
  if (depth !== undefined) {
    classes.push(depth > 4 ? "depth-more" : "depth-" + depth);
  }
  if (isHover) {
    classes.push("hover");
  }
  if (instruction.navigationTargets?.length > 0) {
    classes.push("clickable");
  }
  return classes.join(" ");
}

function buildHighlightMap() {
  const result = normalizeResult(currentState?.result);
  const map = new Map();
  if (!result?.instructionHighlights) {
    return map;
  }

  const allowedRegions = buildAllowedRegionSet(result);
  for (const highlight of result.instructionHighlights) {
    if (allowedRegions.size > 0 && !allowedRegions.has(highlight.regionId)) {
      continue;
    }
    for (const instructionId of highlight.instructionIds || []) {
      const current = map.get(instructionId);
      if (current === undefined || highlight.depth > current) {
        map.set(instructionId, highlight.depth);
      }
    }
  }

  return map;
}

function buildAllowedRegionSet(result) {
  if (!selectedRegionId) {
    return new Set(result.sourceRegions?.map(region => region.id) || []);
  }

  const regions = result.sourceRegions || [];
  const allowed = new Set([selectedRegionId]);
  let changed = true;
  while (changed) {
    changed = false;
    for (const region of regions) {
      if (region.parentId && allowed.has(region.parentId) && !allowed.has(region.id)) {
        allowed.add(region.id);
        changed = true;
      }
    }
  }
  return allowed;
}

function renderEmpty(container, text) {
  container.replaceChildren();
  const empty = document.createElement("div");
  empty.className = "empty";
  empty.textContent = text;
  container.appendChild(empty);
}

function renderExplanations() {
  explainBody.replaceChildren();
  const result = normalizeResult(currentState?.result);
  const scope = findScope(result, selectedScopeKind) || findScope(result, "function");
  const method = getSelectedMethod(scope);
  const opcodes = new Set((method?.instructions || scope?.instructions || []).map(instruction => instruction.opcode).filter(Boolean));
  const explanations = (result?.instructionExplanations || []).filter(explanation => opcodes.has(explanation.opcode));

  if (explanations.length === 0) {
    renderEmpty(explainBody, "Brak wyjaśnień dla aktywnego kontekstu.");
    return;
  }

  for (const explanation of explanations) {
    const item = document.createElement("div");
    item.className = "explanation";
    const title = document.createElement("div");
    title.innerHTML = "<code></code> " + escapeHtml(explanation.description);
    title.querySelector("code").textContent = explanation.opcode;
    const details = document.createElement("div");
    details.className = "meta";
    details.textContent = "Operand: " + explanation.operandKind + " | Pop: " + explanation.stackBehaviourPop + " | Push: " + explanation.stackBehaviourPush + " | Flow: " + explanation.flowControl;
    item.appendChild(title);
    item.appendChild(details);
    explainBody.appendChild(item);
  }
}

function countVisibleHoverInstructions() {
  if (!hoverOverlay?.instructionIds?.length) {
    return 0;
  }
  const result = normalizeResult(currentState?.result);
  const scope = findScope(result, selectedScopeKind) || findScope(result, "function");
  const method = getSelectedMethod(scope);
  const visibleIds = new Set((method?.instructions || scope?.instructions || []).map(instruction => instruction.id));
  return hoverOverlay.instructionIds.filter(id => visibleIds.has(id)).length;
}

function findScope(result, kind) {
  return result?.scopes?.find(scope => scope.kind === kind);
}

function buildInstructionTitle(instruction) {
  const parts = [];
  if (instruction.tooltip) {
    parts.push(instruction.tooltip);
  }
  if (instruction.sourceRange) {
    parts.push(sourceRangeText(instruction.sourceRange));
  }
  if (instruction.navigationTargets?.length > 0) {
    parts.push("Nawigacja: " + instruction.navigationTargets.map(target => target.label).join(" | "));
  }
  return parts.join("\\n");
}

function renderGraph(result, append, parentNodeId) {
  if (!result) {
    return;
  }

  if (!append) {
    graphNodes = new Map();
    graphEdges = [];
  }

  if (!result.success) {
    graphEl.replaceChildren();
    renderEmpty(graphEl, result.error || "Nie udało się załadować grafu.");
    return;
  }

  for (const node of result.nodes || []) {
    graphNodes.set(node.id, node);
  }
  graphEdges = graphEdges.concat(result.edges || []);

  graphEl.replaceChildren();
  if (graphNodes.size === 0) {
    renderEmpty(graphEl, "Graf aplikacji jest pusty.");
    return;
  }

  const column = document.createElement("div");
  column.className = "graph-column";
  const title = document.createElement("div");
  title.className = "graph-title";
  title.textContent = "Graf aplikacji" + (result.rootAssembly ? " | " + result.rootAssembly : "");
  column.appendChild(title);

  for (const node of graphNodes.values()) {
    const button = document.createElement("button");
    button.className = "graph-button";
    button.title = [node.label, node.assemblyKind, node.signature].filter(Boolean).join("\\n");
    const kind = document.createElement("span");
    kind.className = "graph-kind";
    kind.textContent = "[" + node.assemblyKind + "/" + node.kind + "]";
    button.appendChild(kind);
    button.appendChild(document.createTextNode(" " + compactGraphLabel(node.label)));
    button.addEventListener("click", () => {
      if (node.hasChildren) {
        vscode.postMessage({ command: "expandGraph", nodeId: node.id });
      } else if (node.decompileAvailable) {
        vscode.postMessage({ command: "navigateTarget", target: graphNodeToTarget(node) });
      }
    });
    column.appendChild(button);
  }

  if (result.continuationToken && parentNodeId) {
    const more = document.createElement("button");
    more.className = "graph-more";
    more.textContent = "Załaduj więcej";
    more.addEventListener("click", () => {
      vscode.postMessage({ command: "expandGraph", nodeId: parentNodeId, continuationToken: result.continuationToken });
    });
    column.appendChild(more);
  }

  graphEl.appendChild(column);
}

function graphNodeToTarget(node) {
  return {
    id: node.id,
    kind: "decompiled",
    label: node.label,
    assemblyName: node.assemblyName,
    assemblyPath: node.assemblyPath,
    assemblyKind: node.assemblyKind,
    typeName: node.typeName,
    methodName: node.methodName,
    signature: node.signature,
    metadataToken: node.metadataToken,
    sourceRange: node.sourceRange,
    isExternal: node.isExternal,
    decompileAvailable: node.decompileAvailable
  };
}

function compactGraphLabel(value) {
  const text = String(value || "");
  return text.length > 120 ? text.slice(0, 117) + "..." : text;
}

function sourceRangeText(range) {
  if (!range) {
    return "";
  }
  return "źródło: " + range.startLine + ":" + range.startColumn + "-" + range.endLine + ":" + range.endColumn;
}

function depthColor(depth) {
  const colors = ["rgba(88, 166, 255, 0.92)", "rgba(63, 185, 80, 0.92)", "rgba(210, 153, 34, 0.92)", "rgba(248, 81, 73, 0.86)", "rgba(163, 113, 247, 0.88)"];
  return colors[Math.min(depth, colors.length - 1)];
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, char => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#039;"
  }[char]));
}
`;
}

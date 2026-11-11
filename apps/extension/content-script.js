(() => {
  if (window.__voiceCompanionContentScriptLoaded) {
    return;
  }
  window.__voiceCompanionContentScriptLoaded = true;

  const state = {
    observer: null,
    lastSent: new Map(),
    elementIds: new WeakMap(),
    nextElementId: 1
  };

  async function postEvent(payload) {
    chrome.runtime.sendMessage({ type: "capture-event", payload }, (response) => {
      if (chrome.runtime.lastError) {
        console.warn("[VoiceCompanion] Failed to post capture event", chrome.runtime.lastError.message);
        return;
      }

      if (!response?.ok) {
        console.warn("[VoiceCompanion] Failed to post capture event", response);
      }
    });
  }

  function buildPayload(messageEl, role, text, sourceId) {
    if (!messageEl || typeof messageEl.getAttribute !== "function") {
      return {
        role,
        text,
        textHash: null,
        sourceMessageId: sourceId,
        conversationId: null,
        site: "chatgpt"
      };
    }

    const conversationId =
      document.querySelector("main")?.getAttribute("data-conversation-id") ||
      null;

    return {
      role,
      text,
      textHash: null,
      sourceMessageId: sourceId,
      conversationId,
      site: "chatgpt"
    };
  }

  function getOrCreateElementId(element) {
    const existing = state.elementIds.get(element);
    if (existing) {
      return existing;
    }

    const created = `el-${state.nextElementId++}`;
    state.elementIds.set(element, created);
    return created;
  }

  function resolveSourceId(node, index, role) {
    const directId =
      node.getAttribute("data-message-id") ||
      node.getAttribute("data-testid");
    if (directId) {
      return directId;
    }

    const turn = node.closest("article[data-testid*='conversation-turn'], [data-testid*='conversation-turn']");
    const turnId =
      turn?.getAttribute("data-message-id") ||
      turn?.getAttribute("data-testid");
    if (turnId) {
      return turnId;
    }

    const identityTarget = turn || node;
    return `${role}-fallback-${index}-${getOrCreateElementId(identityTarget)}`;
  }

  async function scanAndSend() {
    const { nodes, set } = window.ChatGptDomAdapter.detectMessages();
    nodes.forEach((node, index) => {
      if (!(node instanceof Element)) {
        return;
      }

      const role =
        node.getAttribute(set.roleAttribute) ||
        node.closest("[data-message-author-role]")?.getAttribute("data-message-author-role") ||
        (node.innerText.includes("You") ? "user" : "assistant");

      const text = window.ChatGptDomAdapter.extractText(node);
      if (!text) {
        return;
      }

      const sourceId = resolveSourceId(node, index, role);
      const key = `${role}-${sourceId}`;
      const last = state.lastSent.get(key);
      if (last === text) {
        return;
      }

      state.lastSent.set(key, text);
      postEvent(buildPayload(node, role, text, sourceId));
    });
  }

  function startObserver() {
    if (state.observer) {
      return;
    }

    state.observer = new MutationObserver(() => {
      scanAndSend();
    });

    state.observer.observe(document.body, {
      childList: true,
      subtree: true,
      characterData: true
    });

    scanAndSend();
  }

  startObserver();
})();

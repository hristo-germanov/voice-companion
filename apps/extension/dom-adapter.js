(() => {
  const selectors = [
    {
      messageContainer: "main [data-message-author-role]",
      roleAttribute: "data-message-author-role"
    },
    {
      messageContainer: "main article[data-testid*='conversation-turn']",
      roleAttribute: "data-message-author-role"
    },
    {
      messageContainer: "main [data-testid*='conversation-turn']",
      roleAttribute: "data-message-author-role"
    }
  ];

  function extractText(messageEl) {
    const parts = [];
    messageEl.querySelectorAll("pre, li, p, code").forEach((node) => {
      const tag = node.tagName.toLowerCase();

      // Avoid overlapping captures:
      // - <code> inside <pre> is already captured by <pre>
      // - <p> inside <li> would duplicate list text
      if (tag === "code" && node.closest("pre") !== null) {
        return;
      }

      if (tag === "p" && node.closest("li") !== null) {
        return;
      }

      if (tag === "pre") {
        parts.push("```");
        parts.push(node.innerText.trim());
        parts.push("```");
      } else if (tag === "code") {
        parts.push(node.innerText.trim());
      } else {
        parts.push(node.textContent.trim());
      }
    });

    // Collapse identical lines that can still occur from DOM mirrors.
    const cleanedLines = parts
      .filter(Boolean)
      .map((line) => line.replace(/\s+/g, " ").trim())
      .filter(Boolean);

    const dedupedLines = [];
    for (let i = 0; i < cleanedLines.length; i += 1) {
      if (cleanedLines[i] !== cleanedLines[i - 1]) {
        dedupedLines.push(cleanedLines[i]);
      }
    }

    const parsed = dedupedLines.join("\n");
    if (parsed) {
      return parsed;
    }

    return (messageEl.innerText || "").trim();
  }

  function detectMessages() {
    for (const set of selectors) {
      const nodes = document.querySelectorAll(set.messageContainer);
      if (nodes.length > 0) {
        return { nodes, set };
      }
    }
    return { nodes: [], set: selectors[0] };
  }

  window.ChatGptDomAdapter = {
    detectMessages,
    extractText
  };
})();

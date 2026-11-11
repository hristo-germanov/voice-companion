chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.sync.get(["port", "apiKey", "enabled"], (items) => {
    const updates = {};

    if (typeof items.port !== "number" || Number.isNaN(items.port)) {
      updates.port = 17832;
    }

    if (typeof items.apiKey !== "string" || !items.apiKey.trim()) {
      updates.apiKey = "change-me";
    }

    if (typeof items.enabled !== "boolean") {
      updates.enabled = true;
    }

    if (Object.keys(updates).length > 0) {
      chrome.storage.sync.set(updates);
    }
  });

  attachCaptureScriptsToExistingTabs().catch(() => {});
});

chrome.runtime.onStartup.addListener(() => {
  attachCaptureScriptsToExistingTabs().catch(() => {});
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status !== "complete") {
    return;
  }

  if (!isChatGptUrl(tab?.url)) {
    return;
  }

  attachScriptsToTab(tabId).catch(() => {});
});

function isChatGptUrl(url) {
  if (!url) {
    return false;
  }

  return url.startsWith("https://chatgpt.com/") || url.startsWith("https://chat.openai.com/");
}

async function attachCaptureScriptsToExistingTabs() {
  const tabs = await chrome.tabs.query({
    url: ["https://chatgpt.com/*", "https://chat.openai.com/*"]
  });

  for (const tab of tabs) {
    if (tab.id !== undefined) {
      await attachScriptsToTab(tab.id);
    }
  }
}

async function attachScriptsToTab(tabId) {
  const probe = await chrome.scripting.executeScript({
    target: { tabId },
    func: () => Boolean(window.__voiceCompanionContentScriptLoaded)
  });

  if (probe?.[0]?.result === true) {
    return;
  }

  await chrome.scripting.executeScript({
    target: { tabId },
    files: ["dom-adapter.js", "content-script.js"]
  });
}

async function postCaptureEvent(payload) {
  const settings = await chrome.storage.sync.get({
    port: 17832,
    apiKey: "change-me",
    enabled: true
  });

  if (!settings.enabled) {
    return { ok: true, skipped: true };
  }

  const url = `http://127.0.0.1:${settings.port}/api/capture/event`;
  const response = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Reader-Token": settings.apiKey
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    const text = await response.text().catch(() => "");
    return { ok: false, status: response.status, body: text };
  }

  return { ok: true };
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type !== "capture-event") {
    return false;
  }

  postCaptureEvent(message.payload)
    .then((result) => sendResponse(result))
    .catch((err) =>
      sendResponse({
        ok: false,
        error: err?.message || String(err)
      })
    );

  return true;
});

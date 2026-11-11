const portInput = document.getElementById("port");
const apiKeyInput = document.getElementById("apiKey");
const enabledInput = document.getElementById("enabled");
const status = document.getElementById("status");

chrome.storage.sync.get(
  { port: 17832, apiKey: "change-me", enabled: true },
  (items) => {
    if (chrome.runtime.lastError) {
      status.textContent = `Load failed: ${chrome.runtime.lastError.message}`;
      return;
    }

    portInput.value = items.port;
    apiKeyInput.value = items.apiKey;
    enabledInput.checked = items.enabled;
  }
);

document.getElementById("save").addEventListener("click", () => {
  const parsedPort = Number(portInput.value);
  const port = Number.isFinite(parsedPort) && parsedPort > 0 ? parsedPort : 17832;
  const apiKey = apiKeyInput.value.trim();
  const enabled = enabledInput.checked;

  chrome.storage.sync.set({ port, apiKey, enabled }, () => {
    if (chrome.runtime.lastError) {
      status.textContent = `Save failed: ${chrome.runtime.lastError.message}`;
      return;
    }

    status.textContent = "Saved.";
    setTimeout(() => {
      status.textContent = "";
    }, 1500);
  });
});

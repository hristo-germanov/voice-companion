const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { createRequire } = require('node:module');
const path = require('node:path');
const { test } = require('node:test');
const { setImmediate } = require('node:timers/promises');

const root = path.join(__dirname, '..');
const requireWeb = createRequire(path.join(root, 'apps', 'web', 'package.json'));
const { JSDOM } = requireWeb('jsdom');

function extensionWindow(t, content = '') {
  const { window } = new JSDOM(content, { runScripts: 'outside-only', url: 'https://chatgpt.com/' });
  let observer;
  window.MutationObserver = class extends window.MutationObserver {
    constructor(callback) {
      super(callback);
      observer = this;
    }
  };
  t.after(() => {
    observer?.disconnect();
    window.close();
  });
  // jsdom provides textContent. The extension also reads innerText on rendered elements.
  Object.defineProperty(window.HTMLElement.prototype, 'innerText', {
    get() { return this.textContent; }
  });
  return window;
}

function load(window, file) {
  window.eval(readFileSync(path.join(root, 'apps', 'extension', file), 'utf8'));
}

test('captures prompts and streaming replies once per changed text, with a stable source ID', async t => {
  const window = extensionWindow(t, `
    <main data-conversation-id="conversation-1">
      <article data-message-author-role="user" data-message-id="prompt-1"><p>How does it work?</p></article>
      <article data-message-author-role="assistant" data-message-id="reply-1">
        <p id="wording">An early draft.</p>
        <ul><li><p>One list item</p></li></ul>
        <pre><code>example()</code></pre>
      </article>
    </main>
  `);
  const sent = [];
  window.chrome = { runtime: {
    sendMessage(event, done) {
      sent.push(event);
      done({ ok: true });
    }
  } };
  load(window, 'dom-adapter.js');
  load(window, 'content-script.js');

  assert.equal(sent.length, 2);
  assert.equal(sent[0].payload.role, 'user');
  assert.equal(sent[0].payload.text, 'How does it work?');
  const reply = sent[1];
  assert.equal(reply.type, 'capture-event');
  assert.equal(reply.payload.role, 'assistant');
  assert.equal(reply.payload.conversationId, 'conversation-1');
  assert.equal(reply.payload.sourceMessageId, 'reply-1');
  assert.equal(reply.payload.text, ['An early draft.', 'One list item', '```', 'example()', '```'].join('\n'));

  load(window, 'content-script.js');
  window.document.querySelector('main').append(window.document.createElement('div'));
  await setImmediate();
  assert.equal(sent.length, 2, 'Repeated injection and unrelated DOM changes do not forward duplicates');

  window.document.querySelector('#wording').textContent = 'The final reply.';
  await setImmediate();
  assert.equal(sent.length, 3);
  assert.equal(sent[2].payload.sourceMessageId, reply.payload.sourceMessageId);
  assert.match(sent[2].payload.text, /^The final reply\./);
});

test('uses conversation-turn markup when message-role elements are unavailable', t => {
  const window = extensionWindow(t, '<main><article data-testid="conversation-turn-4"><p>A fallback reply.</p></article></main>');
  load(window, 'dom-adapter.js');
  const { nodes } = window.ChatGptDomAdapter.detectMessages();
  assert.equal(nodes.length, 1);
  assert.equal(window.ChatGptDomAdapter.extractText(nodes[0]), 'A fallback reply.');
});

test('forwards enabled capture to the configured loopback port with its token', async t => {
  const window = extensionWindow(t);
  const requests = [];
  let onMessage;
  let enabled = false;
  let status = 200;
  window.chrome = {
    runtime: { onInstalled: { addListener() {} }, onStartup: { addListener() {} },
      onMessage: { addListener(listener) { onMessage = listener; } } },
    tabs: { onUpdated: { addListener() {} } },
    storage: { sync: { get: async () => ({ port: 17842, apiKey: 'test-key', enabled }) } }
  };
  window.fetch = async (url, options) => {
    requests.push({ url, ...options });
    return { ok: status === 200, status, text: async () => 'Denied' };
  };
  load(window, 'background.js');
  const payload = { role: 'assistant', text: 'Example reply.', sourceMessageId: 'reply-1' };
  const capture = () => new Promise(resolve => {
    assert.equal(onMessage({ type: 'capture-event', payload }, {}, resolve), true);
  });

  assert.equal((await capture()).skipped, true);
  assert.equal(requests.length, 0);
  enabled = true;
  assert.equal((await capture()).ok, true);
  assert.equal(requests[0].url, 'http://127.0.0.1:17842/api/capture/event');
  assert.equal(requests[0].method, 'POST');
  assert.equal(requests[0].headers['X-Reader-Token'], 'test-key');
  assert.deepEqual(JSON.parse(requests[0].body), payload);

  status = 401;
  assert.equal((await capture()).status, 401);
});

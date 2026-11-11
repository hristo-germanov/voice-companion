# Local API reference

Base URL: `http://127.0.0.1:17832`. Source: `apps/api/Controllers/`, reviewed on 2026-11-11. JSON uses camelCase. Send JSON bodies with `Content-Type: application/json`.

Only `POST /api/capture/event` checks `X-Reader-Token`, which must match `Api:ApiKey`. Other endpoints currently have no token requirement. The service is intended for loopback use. Successful control requests acknowledge the request; they do not prove audible playback.

## Routes

| Method | Route | Result |
| --- | --- | --- |
| GET | `/api/health` | `{"status":"ok"}` |
| POST | `/api/capture/event` | Saves/updates capture; returns `messageId`, `shouldSpeak` |
| GET | `/api/history?limit=50` | Recent captured messages, newest ID first; limit clamped to 1–500 |
| GET | `/api/settings` | All stored settings ordered by key, including credentials |
| PUT | `/api/settings` | Upserts one setting and returns `id`, `key`, `value` |
| GET | `/api/voices` | Installed Windows voices: `name`, `gender`, `culture`, `age`; may be empty |
| GET | `/api/voices/edge-en-us` | EN-US Edge voices: `shortName`, `locale`, `gender`, `friendlyName`; may be fallback data |
| GET | `/api/voices/edge-en-eu` | Compatibility alias for the same EN-US list |
| POST | `/api/speech/play/{messageId}` | Queues a stored message; returns `queued: true`, `messageId` |
| POST | `/api/speech/pause` | Requests pause; HTTP 200 with no body |
| POST | `/api/speech/resume` | Requests resume; HTTP 200 with no body |
| POST | `/api/speech/stop` | Stops active playback and drains the channel; HTTP 200 |
| POST | `/api/speech/skip` | Requests skipping current item; HTTP 200 |
| POST | `/api/speech/repeat` | Requeues last attempted item when available; HTTP 200 |
| GET | `/api/speech/tracker` | In-memory playback snapshot |
| GET | `/api/speech/diagnostics` | Stored mode and latest speech event, or null event |
| POST | `/api/speech/diagnostics/clear-latest-error` | Clears latest event's error text only |

## Capture request

```json
{
  "role": "assistant",
  "text": "Example captured reply.",
  "textHash": null,
  "sourceMessageId": "example-message-1",
  "conversationId": "example-conversation",
  "site": "chatgpt"
}
```

`role` and nonblank `text` are the core fields. `textHash`, `sourceMessageId`, `conversationId`, and `site` can be null; supply stable source/conversation IDs to reduce collisions. Missing/blank `textHash` is computed from trimmed text. A provided hash is stored as supplied, while the in-memory deduper computes its own text hash.

Example success (IDs vary):

```json
{"messageId": 42, "shouldSpeak": true}
```

`shouldSpeak` reports the text deduper result before role gating and stability processing. It does not mean the message was spoken. Only assistant messages currently leave the stability worker. Session storage is not isolated by `conversationId`.

| Condition | Response |
| --- | --- |
| Missing or incorrect capture token | 401 |
| Blank text | 400, `Text is required.` |
| Blank settings key | 400, `Key is required.` |
| Unknown message ID for Play | 404, `{"error":"message_not_found","messageId":42}` |
| Empty stored message for Play | 400, `{"error":"message_text_empty","messageId":42}` |
| Invalid JSON/model binding | Framework-generated 400 response |

## History and tracker

History returns an array of `id`, `role`, `text`, `sourceMessageId`, `createdAt`, `updatedAt`, and nullable `spokeAt`. Timestamps are serialized date/time values. `spokeAt` records successful completion, not queue entry.

An idle tracker looks like:

```json
{
  "currentMessageId": null,
  "currentText": null,
  "isPaused": false,
  "isSpeaking": false,
  "queuedItems": 0,
  "startedAt": null,
  "currentCharIndex": 0,
  "currentTextLength": 0,
  "currentSegmentIndex": 0,
  "totalSegments": 0,
  "currentSegmentText": null
}
```

The current message ID is a string. Segment indexes during segmented playback start at 1. Position is an estimate rather than an audio timestamp or exact word alignment. The current repeat path does not increment the queue counter, so `queuedItems` may undercount repeat requests.

Diagnostics returns `{"mode":"local","latestEvent":null}` before events/settings exist. When present, `latestEvent` has `id`, `messageId`, `status`, `startedAt`, `endedAt`, and `error`. The reported mode comes from the saved setting with a Local fallback; it is not a live worker-mode measurement.

Clear-error returns `{"cleared":true,"eventId":123}`, or `{"cleared":false,"reason":"no_events"}`. It leaves the event's status unchanged.

## PowerShell examples

Read-only checks:

```powershell
$apiUrl = 'http://127.0.0.1:17832'
Invoke-RestMethod "$apiUrl/api/health" -TimeoutSec 10
Invoke-RestMethod "$apiUrl/api/history?limit=10" -TimeoutSec 10
Invoke-RestMethod "$apiUrl/api/speech/tracker" -TimeoutSec 10
```

Persist the Edge mode explicitly (changes a setting):

```powershell
$settingBody = @{ key = 'speech.mode'; value = 'edge' } | ConvertTo-Json
Invoke-RestMethod "$apiUrl/api/settings" -Method Put -ContentType 'application/json' -Body $settingBody
```

Manual capture (stores text and can trigger audio):

```powershell
$captureToken = Read-Host 'Capture API token from local appsettings.json'
$captureBody = @{
  role = 'assistant'
  text = 'Manual capture verification.'
  textHash = $null
  sourceMessageId = 'manual-' + [guid]::NewGuid().ToString()
  conversationId = 'manual-check'
  site = 'chatgpt'
} | ConvertTo-Json
Invoke-RestMethod "$apiUrl/api/capture/event" -Method Post -ContentType 'application/json' -Headers @{ 'X-Reader-Token' = $captureToken } -Body $captureBody
```

Do not paste actual tokens, settings responses, or private captured text into shared logs. See [configuration](configuration.md) for the full settings list.

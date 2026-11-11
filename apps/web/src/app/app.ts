import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

type Role = 'assistant' | 'user';
type SpeechMode = 'local' | 'cloud' | 'edge';

interface Voice {
  name: string;
  gender: string;
  culture: string;
  age: string;
}

interface EdgeVoice {
  shortName: string;
  locale: string;
  gender: string;
  friendlyName: string;
}

interface HistoryItem {
  id: number;
  role: Role;
  text: string;
  sourceMessageId: string | null;
  createdAt: string;
  updatedAt: string;
  spokeAt: string | null;
}

interface AppSetting {
  id?: number;
  key: string;
  value: string;
}

interface SpeechDiagnostics {
  mode: string;
  latestEvent: {
    id: number;
    messageId: number;
    status: string;
    startedAt: string | null;
    endedAt: string | null;
    error: string | null;
  } | null;
}

interface SpeechTracker {
  currentMessageId: string | null;
  currentText: string | null;
  isPaused: boolean;
  isSpeaking: boolean;
  queuedItems: number;
  startedAt: string | null;
  currentCharIndex: number;
  currentTextLength: number;
  currentSegmentIndex: number;
  totalSegments: number;
  currentSegmentText: string | null;
}

interface TrackerHighlight {
  before: string;
  current: string;
  after: string;
}

@Component({
  selector: 'app-root',
  imports: [DatePipe],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit, OnDestroy {
  private readonly apiBase = 'http://127.0.0.1:17832';
  protected readonly legacyEdgeVoice = 'en-US-AndrewMultilingualNeural';
  private pollTimer: number | null = null;
  private liveRefreshInFlight = false;
  private liveRefreshTick = 0;

  protected readonly connected = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly voices = signal<Voice[]>([]);
  protected readonly edgeVoices = signal<EdgeVoice[]>([]);
  protected readonly history = signal<HistoryItem[]>([]);
  protected readonly readAssistant = signal(true);
  protected readonly readUser = signal(false);
  protected readonly speechMode = signal<SpeechMode>('edge');
  protected readonly speechRate = signal(0);
  protected readonly selectedVoice = signal('');
  protected readonly cloudApiKey = signal('');
  protected readonly cloudModel = signal('gpt-4o-mini-tts');
  protected readonly cloudVoice = signal('alloy');
  protected readonly cloudSpeed = signal(1);
  protected readonly edgeVoice = signal('en-US-AndrewMultilingualNeural');
  protected readonly edgeRatePercent = signal(0);
  protected readonly speechDiagnostics = signal<SpeechDiagnostics | null>(null);
  protected readonly speechTracker = signal<SpeechTracker | null>(null);
  protected readonly historyLimit = signal(50);
  protected readonly trackerHighlight = computed<TrackerHighlight>(() => this.calculateTrackerHighlight());

  constructor(private readonly http: HttpClient) {}

  async ngOnInit(): Promise<void> {
    await this.refreshAll();
    this.pollTimer = window.setInterval(() => {
      void this.refreshLiveData();
    }, 2000);
  }

  ngOnDestroy(): void {
    if (this.pollTimer !== null) {
      window.clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }

  protected async refreshAll(): Promise<void> {
    this.busy.set(true);
    this.message.set('');
    try {
      await this.checkHealth();
      await Promise.all([
        this.loadVoices(),
        this.loadHistory(this.historyLimit()),
        this.loadSettings(),
        this.loadDiagnostics(),
        this.loadTracker()
      ]);
      await this.loadEdgeVoices();
      this.message.set('Synced with local API.');
    } catch {
      this.connected.set(false);
      this.message.set('Local API unavailable. Start backend on 127.0.0.1:17832.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async setHistoryLimit(value: string): Promise<void> {
    const parsed = Number(value);
    const safe = Number.isFinite(parsed) ? Math.min(500, Math.max(10, parsed)) : 50;
    this.historyLimit.set(safe);
    await this.loadHistory(safe);
  }

  protected async setReadAssistant(enabled: boolean): Promise<void> {
    this.readAssistant.set(enabled);
    await this.putSetting('speech.read_assistant', String(enabled));
  }

  protected async setReadUser(enabled: boolean): Promise<void> {
    this.readUser.set(enabled);
    await this.putSetting('speech.read_user', String(enabled));
  }

  protected async setSpeechRate(value: string): Promise<void> {
    const parsed = Number(value);
    const safe = Number.isFinite(parsed) ? Math.min(10, Math.max(-10, parsed)) : 0;
    this.speechRate.set(safe);
    await this.putSetting('speech.rate', String(safe));
  }

  protected async setVoice(value: string): Promise<void> {
    this.selectedVoice.set(value);
    await this.putSetting('speech.voice', value);
  }

  protected async setSpeechMode(value: string): Promise<void> {
    const mode: SpeechMode = value === 'cloud' ? 'cloud' : value === 'edge' ? 'edge' : 'local';
    this.speechMode.set(mode);
    await this.putSetting('speech.mode', mode);
  }

  protected async setCloudApiKey(value: string): Promise<void> {
    const key = value.trim();
    this.cloudApiKey.set(key);
    await this.putSetting('speech.cloud.api_key', key);
  }

  protected async setCloudModel(value: string): Promise<void> {
    const model = value.trim() || 'gpt-4o-mini-tts';
    this.cloudModel.set(model);
    await this.putSetting('speech.cloud.model', model);
  }

  protected async setCloudVoice(value: string): Promise<void> {
    const voice = value.trim() || 'alloy';
    this.cloudVoice.set(voice);
    await this.putSetting('speech.cloud.voice', voice);
  }

  protected async setCloudSpeed(value: string): Promise<void> {
    const parsed = Number(value);
    const safe = Number.isFinite(parsed) ? Math.min(4, Math.max(0.25, parsed)) : 1;
    this.cloudSpeed.set(safe);
    await this.putSetting('speech.cloud.speed', String(safe));
  }

  protected async setEdgeVoice(value: string): Promise<void> {
    const voice = value.trim() || this.legacyEdgeVoice;
    this.edgeVoice.set(voice);
    await this.putSetting('speech.edge.voice', voice);
  }

  protected async setEdgeRatePercent(value: string): Promise<void> {
    const parsed = Number(value);
    const safe = Number.isFinite(parsed) ? Math.min(100, Math.max(-50, parsed)) : 0;
    this.edgeRatePercent.set(safe);
    await this.putSetting('speech.edge.rate_percent', String(safe));
  }

  protected async pause(): Promise<void> {
    await this.invokeSpeech('pause');
  }

  protected async start(): Promise<void> {
    const tracker = this.speechTracker();
    if (tracker?.isPaused) {
      await this.resume();
      return;
    }

    await this.repeat();
  }

  protected async resume(): Promise<void> {
    await this.invokeSpeech('resume');
  }

  protected async skip(): Promise<void> {
    await this.invokeSpeech('skip');
  }

  protected async stop(): Promise<void> {
    await this.invokeSpeech('stop');
  }

  protected async repeat(): Promise<void> {
    await this.invokeSpeech('repeat');
  }

  protected async playCapturedMessage(messageId: number): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${this.apiBase}/api/speech/play/${messageId}`, {}));
      this.message.set(`Queued message #${messageId} for playback.`);
    } catch {
      this.message.set(`Failed to queue message #${messageId}.`);
    }
  }

  protected async clearLatestError(): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${this.apiBase}/api/speech/diagnostics/clear-latest-error`, {}));
      await this.loadDiagnostics();
      this.message.set('Latest speech error cleared.');
    } catch {
      this.message.set('Failed to clear latest speech error.');
    }
  }

  private async refreshLiveData(): Promise<void> {
    if (!this.connected() || this.liveRefreshInFlight) {
      return;
    }

    this.liveRefreshInFlight = true;
    try {
      this.liveRefreshTick += 1;
      const shouldRefreshHistory = this.liveRefreshTick % 3 === 0;

      const tasks: Array<Promise<void>> = [this.loadDiagnostics(), this.loadTracker()];
      if (shouldRefreshHistory) {
        tasks.push(this.loadHistory(this.historyLimit()));
      }
      await Promise.all(tasks);
    } catch {
      // Keep background polling silent.
    } finally {
      this.liveRefreshInFlight = false;
    }
  }

  private async checkHealth(): Promise<void> {
    await firstValueFrom(this.http.get(`${this.apiBase}/api/health`));
    this.connected.set(true);
  }

  private async loadVoices(): Promise<void> {
    const voices = await firstValueFrom(this.http.get<Voice[]>(`${this.apiBase}/api/voices`));
    this.voices.set(voices);
  }

  private async loadEdgeVoices(): Promise<void> {
    const voices = await firstValueFrom(
      this.http.get<EdgeVoice[]>(`${this.apiBase}/api/voices/edge-en-us`)
    );
    this.edgeVoices.set(voices);
    await this.normalizeEdgeVoiceSelection(voices);
  }

  private async loadHistory(limit: number): Promise<void> {
    const history = await firstValueFrom(
      this.http.get<HistoryItem[]>(`${this.apiBase}/api/history?limit=${limit}`)
    );
    this.history.set(history);
  }

  private async loadSettings(): Promise<void> {
    const settings = await firstValueFrom(this.http.get<AppSetting[]>(`${this.apiBase}/api/settings`));
    const map = new Map(settings.map((x) => [x.key, x.value]));
    this.readAssistant.set(this.parseBool(map.get('speech.read_assistant'), true));
    this.readUser.set(this.parseBool(map.get('speech.read_user'), false));
    this.speechMode.set(this.parseSpeechMode(map.get('speech.mode')));
    this.speechRate.set(this.parseInt(map.get('speech.rate'), 0, -10, 10));
    this.selectedVoice.set(map.get('speech.voice') ?? '');
    this.cloudApiKey.set(map.get('speech.cloud.api_key') ?? '');
    this.cloudModel.set(map.get('speech.cloud.model') ?? 'gpt-4o-mini-tts');
    this.cloudVoice.set(map.get('speech.cloud.voice') ?? 'alloy');
    this.cloudSpeed.set(this.parseFloat(map.get('speech.cloud.speed'), 1, 0.25, 4));
    this.edgeVoice.set(map.get('speech.edge.voice') ?? this.legacyEdgeVoice);
    this.edgeRatePercent.set(this.parseInt(map.get('speech.edge.rate_percent'), 0, -50, 100));
  }

  protected isCurrentMessage(item: HistoryItem): boolean {
    const tracker = this.speechTracker();
    if (!tracker?.currentMessageId) {
      return false;
    }
    return tracker.currentMessageId === String(item.id);
  }

  protected currentMessageProgressPercent(item: HistoryItem): number {
    if (!this.isCurrentMessage(item)) {
      return 0;
    }

    const tracker = this.speechTracker();
    if (!tracker || tracker.currentTextLength <= 0) {
      return 0;
    }

    const ratio = tracker.currentCharIndex / tracker.currentTextLength;
    return Math.max(0, Math.min(100, Math.round(ratio * 100)));
  }

  private async loadDiagnostics(): Promise<void> {
    const data = await firstValueFrom(
      this.http.get<SpeechDiagnostics>(`${this.apiBase}/api/speech/diagnostics`)
    );
    this.speechDiagnostics.set(data);
  }

  private async loadTracker(): Promise<void> {
    const data = await firstValueFrom(
      this.http.get<SpeechTracker>(`${this.apiBase}/api/speech/tracker`)
    );
    this.speechTracker.set(data);
  }

  private parseBool(raw: string | undefined, fallback: boolean): boolean {
    if (!raw) {
      return fallback;
    }
    return raw.toLowerCase() === 'true';
  }

  private parseInt(
    raw: string | undefined,
    fallback: number,
    min: number,
    max: number
  ): number {
    if (!raw) {
      return fallback;
    }

    const parsed = Number(raw);
    if (!Number.isFinite(parsed)) {
      return fallback;
    }

    return Math.min(max, Math.max(min, parsed));
  }

  private parseFloat(
    raw: string | undefined,
    fallback: number,
    min: number,
    max: number
  ): number {
    if (!raw) {
      return fallback;
    }

    const parsed = Number(raw);
    if (!Number.isFinite(parsed)) {
      return fallback;
    }

    return Math.min(max, Math.max(min, parsed));
  }

  private parseSpeechMode(raw: string | undefined): SpeechMode {
    return raw === 'cloud' || raw === 'edge' ? raw : 'edge';
  }

  private async normalizeEdgeVoiceSelection(voices: EdgeVoice[]): Promise<void> {
    const current = this.edgeVoice();
    if (current === this.legacyEdgeVoice) {
      return;
    }

    if (voices.length === 0) {
      this.edgeVoice.set(this.legacyEdgeVoice);
      return;
    }

    const exists = voices.some((x) => x.shortName === current);
    if (exists) {
      return;
    }

    const preferred =
      voices.find((x) => x.shortName === this.legacyEdgeVoice)?.shortName ??
      voices[0].shortName;
    this.edgeVoice.set(preferred);

    try {
      await firstValueFrom(
        this.http.put(`${this.apiBase}/api/settings`, { key: 'speech.edge.voice', value: preferred })
      );
    } catch {
      // Keep UI responsive even if persisting default fails.
    }
  }

  private calculateTrackerHighlight(): TrackerHighlight {
    const tracker = this.speechTracker();
    const text = tracker?.currentText ?? '';
    if (!text) {
      return { before: '', current: '', after: '' };
    }

    const rawIndex = tracker?.currentCharIndex ?? 0;
    const index = Math.max(0, Math.min(text.length, rawIndex));

    let start = index;
    let end = index;
    const isWordChar = (ch: string): boolean => /[A-Za-z0-9']/u.test(ch);

    while (start > 0 && isWordChar(text[start - 1])) {
      start -= 1;
    }
    while (end < text.length && isWordChar(text[end])) {
      end += 1;
    }

    if (start === end) {
      let next = index;
      while (next < text.length && !isWordChar(text[next])) {
        next += 1;
      }
      if (next < text.length) {
        start = next;
        end = next;
        while (end < text.length && isWordChar(text[end])) {
          end += 1;
        }
      } else {
        let prev = Math.max(0, index - 1);
        while (prev > 0 && !isWordChar(text[prev])) {
          prev -= 1;
        }
        if (isWordChar(text[prev])) {
          end = prev + 1;
          start = prev;
          while (start > 0 && isWordChar(text[start - 1])) {
            start -= 1;
          }
        }
      }
    }

    if (start === end) {
      return { before: text, current: '', after: '' };
    }

    return {
      before: text.slice(0, start),
      current: text.slice(start, end),
      after: text.slice(end)
    };
  }

  private async putSetting(key: string, value: string): Promise<void> {
    try {
      await firstValueFrom(this.http.put(`${this.apiBase}/api/settings`, { key, value }));
      this.message.set(`Saved ${key}.`);
    } catch {
      this.message.set(`Failed to save ${key}.`);
    }
  }

  private async invokeSpeech(action: 'pause' | 'resume' | 'stop' | 'skip' | 'repeat'): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${this.apiBase}/api/speech/${action}`, {}));
      this.message.set(`Speech ${action} requested.`);
    } catch {
      this.message.set(`Failed to call /api/speech/${action}.`);
    }
  }
}

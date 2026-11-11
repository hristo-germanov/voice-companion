import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, OnDestroy, OnInit, ViewChild, computed, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

type Role = 'assistant' | 'user' | 'manual';
type SpeechMode = 'local' | 'cloud' | 'kokoro';

interface Voice {
  name: string;
  gender: string;
  culture: string;
  age: string;
}

interface HistoryItem {
  id: number;
  role: Role;
  text: string;
  sourceMessageId: string | null;
  createdAt: string;
  updatedAt: string;
  spokeAt: string | null;
  playbackPositionSeconds?: number;
  playbackDurationSeconds?: number;
  playbackProgressPercent?: number;
  playbackCompleted?: boolean;
}

interface QueuedSpeech {
  id: string;
  messageId: string;
  text: string;
  status: 'waiting' | 'buffering' | 'ready' | 'failed';
  bufferedParts: number;
  totalParts: number;
  voice: string | null;
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
  currentWord?: { start: number; length: number } | null;
  positionSeconds?: number;
  bufferedSeconds?: number;
  isFullyBuffered?: boolean;
  isPreparingAudio?: boolean;
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
  styleUrls: ['./app.css', './player.css', './queue.css']
})
export class App implements OnInit, OnDestroy {
  private readonly apiBase = 'http://127.0.0.1:17832';
  private pollTimer: number | null = null;
  private audioPollTimer: number | null = null;
  private trackerInFlight = false;
  private lastWordKey = '';
  @ViewChild('reader') private reader?: ElementRef<HTMLElement>;
  private liveRefreshInFlight = false;
  private liveRefreshTick = 0;

  protected readonly connected = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly manualText = signal('');
  protected readonly manualBusy = signal(false);
  protected readonly manualStatus = signal('');
  protected readonly manualTextLimit = 50000;
  protected readonly pendingSpeech = signal<QueuedSpeech[]>([]);
  protected readonly autoScroll = signal(true);
  protected readonly queueBusy = signal(false);
  protected readonly seekDraft = signal<number | null>(null);
  protected readonly timelineDuration = computed(() => {
    const tracker = this.speechTracker();
    // A growing generated prefix is not the recording's duration.
    return tracker?.currentMessageId && tracker.isFullyBuffered ? tracker.bufferedSeconds ?? 0 : 0;
  });
  protected readonly activeTab = signal<'manual' | 'chatgpt'>('manual');
  protected readonly playbackBusy = signal(false);
  protected readonly manualTextActive = computed(() => !!this.speechTracker()?.isSpeaking &&
    this.speechTracker()?.currentText?.trim() === this.manualText().trim());
  protected readonly visibleHistory = computed(() => this.history().filter(item =>
    this.activeTab() === 'manual' ? item.role === 'manual' : item.role !== 'manual'));
  protected readonly playbackProgress = computed(() => {
    const tracker = this.speechTracker();
    if (tracker?.isFullyBuffered && (tracker.bufferedSeconds ?? 0) > 0)
      return Math.min(99, Math.floor((tracker.positionSeconds ?? 0) / tracker.bufferedSeconds! * 100));
    return tracker?.currentTextLength
      ? Math.max(0, Math.min(100, Math.round(tracker.currentCharIndex / tracker.currentTextLength * 100))) : 0;
  });
  protected readonly playbackLabel = computed(() => {
    const tracker = this.speechTracker();
    if (!this.connected()) return 'Offline';
    if (tracker?.isPaused) return 'Paused';
    if (tracker?.isPreparingAudio) return tracker.currentCharIndex > 0 ? 'Preparing next part' : 'Preparing audio';
    if (tracker?.isSpeaking) return tracker.currentCharIndex > 0 ? 'Reading aloud' : 'Preparing audio';
    return 'Ready to listen';
  });

  protected async readManualText(): Promise<void> {
    const text = this.manualText().trim();
    if (this.manualBusy() || this.manualTextActive() || !text || this.manualText().length > this.manualTextLimit) {
      return;
    }

    this.manualBusy.set(true);
    this.manualStatus.set('');
    try {
      const queued = await firstValueFrom(this.http.post<{ messageId: number }>(`${this.apiBase}/api/speech/text`, { text }));
      const now = new Date().toISOString();
      this.history.update(items => [{ id: queued.messageId, role: 'manual', text,
        sourceMessageId: null, createdAt: now, updatedAt: now, spokeAt: null,
        playbackPositionSeconds: 0, playbackDurationSeconds: 0, playbackProgressPercent: 0,
        playbackCompleted: false } as HistoryItem, ...items.filter(item => item.id !== queued.messageId)]);
      this.manualStatus.set('Text added to the speech queue. Use Resume if playback is paused.');
    } catch {
      this.manualStatus.set('Could not queue text. Check the API connection and try again.');
    } finally {
      this.manualBusy.set(false);
    }
  }

  protected clearManualText(): void {
    this.manualText.set('');
    this.manualStatus.set('');
  }
  protected readonly voices = signal<Voice[]>([]);
  protected readonly history = signal<HistoryItem[]>([]);
  protected readonly pendingDeleteId = signal<number | null>(null);
  protected readonly deletingId = signal<number | null>(null);
  private readonly deletedHistoryIds = new Set<number>();
  protected readonly readAssistant = signal(true);
  protected readonly readUser = signal(false);
  protected readonly speechMode = signal<SpeechMode>('kokoro');
  protected readonly speechRate = signal(0);
  protected readonly selectedVoice = signal('');
  protected readonly cloudApiKey = signal('');
  protected readonly cloudModel = signal('gpt-4o-mini-tts');
  protected readonly cloudVoice = signal('alloy');
  protected readonly cloudSpeed = signal(1);
  protected readonly kokoroVoice = signal('af_heart');
  protected readonly kokoroSpeed = signal(1);
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
    this.audioPollTimer = window.setInterval(() => {
      if (this.connected()) void this.loadTracker().catch(() => {});
    }, 100);
  }

  ngOnDestroy(): void {
    if (this.audioPollTimer !== null) window.clearInterval(this.audioPollTimer);
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
        this.loadTracker(),
        this.loadQueue()
      ]);
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
    const mode: SpeechMode = value === 'cloud' ? 'cloud' : value === 'local' ? 'local' : 'kokoro';
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

  protected async setKokoroVoice(value: string): Promise<void> {
    const voice = value === 'af_bella' ? value : 'af_heart';
    this.kokoroVoice.set(voice);
    await this.putSetting('speech.kokoro.voice', voice);
  }

  protected async setKokoroSpeed(value: string): Promise<void> {
    const parsed = Number(value);
    const speed = Number.isFinite(parsed) ? Math.min(2, Math.max(0.5, parsed)) : 1;
    this.kokoroSpeed.set(speed);
    await this.putSetting('speech.kokoro.speed', String(speed));
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

    // Play resumes a paused item; replay is an explicit, separate action.
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
    const tracker = this.speechTracker();
    if (tracker?.isSpeaking || tracker?.isPaused || (tracker?.queuedItems ?? 0) > 0) {
      this.message.set('Wait for playback to finish before repeating.');
      return;
    }
    await this.invokeSpeech('repeat');
  }

  protected async playCapturedMessage(messageId: number, restart = false): Promise<void> {
    if (this.playbackBusy()) return;
    if (!restart && this.speechTracker()?.currentMessageId === String(messageId)) {
      if (this.speechTracker()?.isPaused) await this.resume();
      return;
    }
    this.playbackBusy.set(true);
    try {
      // Selecting a history row starts that recording, rather than appending repeats.
      await firstValueFrom(this.http.post(`${this.apiBase}/api/speech/stop`, {}));
      await firstValueFrom(this.http.post(`${this.apiBase}/api/speech/play/${messageId}${restart ? '?restart=true' : ''}`, {}));
      this.message.set(`Queued message #${messageId} for playback.`);
    } catch {
      this.message.set(`Failed to queue message #${messageId}.`);
    } finally {
      this.playbackBusy.set(false);
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

      const tasks: Array<Promise<void>> = [this.loadDiagnostics(), this.loadQueue()];
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

  private async loadHistory(limit: number): Promise<void> {
    const history = await firstValueFrom(
      this.http.get<HistoryItem[]>(`${this.apiBase}/api/history?limit=${limit}`)
    );
    this.history.set(history.filter(item => !this.deletedHistoryIds.has(item.id)));
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
    this.kokoroVoice.set(map.get('speech.kokoro.voice') === 'af_bella' ? 'af_bella' : 'af_heart');
    this.kokoroSpeed.set(this.parseFloat(map.get('speech.kokoro.speed'), 1, 0.5, 2));
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
    if (this.trackerInFlight) return;
    this.trackerInFlight = true;
    try {
      const data = await firstValueFrom(this.http.get<SpeechTracker>(`${this.apiBase}/api/speech/tracker`));
      const previous = this.speechTracker()?.currentMessageId;
      this.speechTracker.set(data.currentMessageId && this.deletedHistoryIds.has(Number(data.currentMessageId)) ? null : data);
      if (data.currentMessageId) this.history.update(items => items.map(item => String(item.id) === data.currentMessageId
        ? { ...item, playbackPositionSeconds: data.positionSeconds ?? 0,
            playbackDurationSeconds: data.isFullyBuffered ? data.bufferedSeconds : item.playbackDurationSeconds,
            playbackProgressPercent: this.playbackProgress(), playbackCompleted: false } : item));
      if (previous && previous !== data.currentMessageId) void this.loadHistory(this.historyLimit()).catch(() => {});
      const wordKey = `${data.currentMessageId}:${data.currentWord?.start}`;
      if (wordKey !== this.lastWordKey && this.autoScroll()) {
        this.lastWordKey = wordKey;
        window.requestAnimationFrame(() => {
          const reader = this.reader?.nativeElement;
          const mark = reader?.querySelector<HTMLElement>('mark');
          if (reader && mark && mark.textContent && (mark.offsetTop < reader.scrollTop || mark.offsetTop > reader.scrollTop + reader.clientHeight - 45))
            reader.scrollTo({ top: Math.max(0, mark.offsetTop - reader.clientHeight / 2), behavior: 'smooth' });
        });
      }
    } finally { this.trackerInFlight = false; }
  }

  private async loadQueue(): Promise<void> {
    this.pendingSpeech.set((await firstValueFrom(this.http.get<QueuedSpeech[]>(`${this.apiBase}/api/speech/queue`))).filter(item => !this.deletedHistoryIds.has(Number(item.messageId))));
  }

  protected async enqueueMessage(messageId: number): Promise<void> {
    if (this.queueBusy()) return;
    this.queueBusy.set(true);
    try {
      await firstValueFrom(this.http.post(`${this.apiBase}/api/speech/play/${messageId}`, {}));
      await this.loadQueue();
      this.message.set('Added to queue. Current reading continues.');
    } catch { this.message.set('Could not add this text to the queue.'); }
    finally { this.queueBusy.set(false); }
  }

  protected async deleteText(id: number): Promise<void> {
    if (this.deletingId() !== null) return;
    this.deletingId.set(id);
    try {
      await firstValueFrom(this.http.delete(`${this.apiBase}/api/history/${id}`));
      this.deletedHistoryIds.add(id);
      this.history.update(items => items.filter(item => item.id !== id));
      this.pendingSpeech.update(items => items.filter(item => item.messageId !== String(id)));
      if (this.speechTracker()?.currentMessageId === String(id)) this.speechTracker.set(null);
      this.pendingDeleteId.set(null);
      this.message.set('Text deleted.');
    } catch { this.message.set('Could not delete this text. Please try again.'); }
    finally { this.deletingId.set(null); }
  }

  protected async removeQueued(id: string): Promise<void> {
    try {
      await firstValueFrom(this.http.delete(`${this.apiBase}/api/speech/queue/${id}`));
      this.pendingSpeech.update(items => items.filter(item => item.id !== id));
    } catch { this.message.set('Could not remove the queued text.'); }
  }

  protected async clearQueued(): Promise<void> {
    try {
      await firstValueFrom(this.http.delete(`${this.apiBase}/api/speech/queue`));
      this.pendingSpeech.set([]);
    } catch { this.message.set('Could not clear the queue.'); }
  }

  protected async seekAudio(seconds: number | string, relative = false): Promise<void> {
    try {
      const data = await firstValueFrom(this.http.post<SpeechTracker>(`${this.apiBase}/api/speech/seek`, { seconds: Number(seconds), relative }));
      this.speechTracker.set(data.currentMessageId && this.deletedHistoryIds.has(Number(data.currentMessageId)) ? null : data);
    } catch { this.message.set('Could not seek. Wait for audio to buffer.'); }
  }

  protected previewSeek(value: string): void {
    const position = Number(value);
    if (Number.isFinite(position)) this.seekDraft.set(position);
  }

  protected async commitSeek(value: string): Promise<void> {
    try { await this.seekAudio(value); }
    finally { this.seekDraft.set(null); }
  }

  protected formatTime(seconds: number | undefined): string {
    const safe = Math.max(0, Math.floor(seconds ?? 0));
    return `${Math.floor(safe / 60)}:${String(safe % 60).padStart(2, '0')}`;
  }

  protected recordPercent(item: HistoryItem): number {
    return this.isCurrentMessage(item) ? this.playbackProgress() : item.playbackCompleted ? 100 : item.playbackProgressPercent ?? 0;
  }

  protected recordStatus(item: HistoryItem): string {
    const percent = this.recordPercent(item);
    const status = this.isCurrentMessage(item) ? this.speechTracker()?.isPaused ? 'Paused' : 'Reading'
      : item.playbackCompleted ? 'Completed' : this.pendingSpeech().some(entry => entry.messageId === String(item.id)) ? 'Queued'
      : (item.playbackPositionSeconds ?? 0) > 0 ? 'In progress' : 'Not started';
    return `${status} · ${percent}%`;
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
    return raw === 'cloud' || raw === 'local' ? raw : 'kokoro';
  }

  private calculateTrackerHighlight(): TrackerHighlight {
    const tracker = this.speechTracker();
    const text = tracker?.currentText ?? '';
    if (!text) {
      return { before: '', current: '', after: '' };
    }
    if (this.speechMode() === 'kokoro') {
      const word = tracker?.currentWord;
      if (!word) return { before: text, current: '', after: '' };
      const start = Math.max(0, Math.min(text.length, word.start));
      const end = Math.min(text.length, start + word.length);
      return { before: text.slice(0, start), current: text.slice(start, end), after: text.slice(end) };
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
      const tracker = this.speechTracker();
      if (tracker && action === 'pause') this.speechTracker.set({ ...tracker, isPaused: true });
      if (tracker && action === 'resume') this.speechTracker.set({ ...tracker, isPaused: false });
      if (tracker && action === 'stop') this.speechTracker.set({ ...tracker,
        isSpeaking: false, isPaused: false, currentMessageId: null, currentText: null,
        queuedItems: 0, currentCharIndex: 0, currentTextLength: 0 });
      if (action === 'stop') this.pendingSpeech.set([]);
      this.message.set(`Speech ${action} requested.`);
    } catch {
      this.message.set(`Failed to call /api/speech/${action}.`);
    }
  }
}

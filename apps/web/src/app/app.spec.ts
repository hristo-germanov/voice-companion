import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient()]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render dashboard title', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Turn words into listening.');
  });
});

describe('Manual text playback', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  function setup(text: string) {
    const fixture = TestBed.createComponent(App);
    fixture.componentInstance['connected'].set(true);
    fixture.componentInstance['manualText'].set(text);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('http://127.0.0.1:17832/api/health')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    return fixture;
  }

  it('queues trimmed text once, retains the draft, and reports success', async () => {
    const fixture = setup('  Hello\nworld  ');
    const http = TestBed.inject(HttpTestingController);
    const pending = fixture.componentInstance['readManualText']();
    await fixture.componentInstance['readManualText']();
    const request = http.expectOne('http://127.0.0.1:17832/api/speech/text');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ text: 'Hello\nworld' });
    expect(fixture.componentInstance['manualBusy']()).toBe(true);
    request.flush({ queued: true, messageId: 1 });
    await pending;
    expect(fixture.componentInstance['manualText']()).toBe('  Hello\nworld  ');
    expect(fixture.componentInstance['manualStatus']()).toContain('added to the speech queue');
    expect(fixture.componentInstance['manualBusy']()).toBe(false);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.history .source-label').textContent).toContain('0%');
    expect(fixture.nativeElement.querySelector('.history .source-label').textContent).not.toContain('PASTED TEXT');
    http.verify();
  });

  it('rejects blank and oversized drafts without a request', async () => {
    const fixture = setup(' \n ');
    await fixture.componentInstance['readManualText']();
    fixture.componentInstance['manualText'].set('a'.repeat(50001));
    await fixture.componentInstance['readManualText']();
    TestBed.inject(HttpTestingController).expectNone('http://127.0.0.1:17832/api/speech/text');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('textarea').value.length).toBe(50001);
  });

  it('does not replay when Play is pressed during or after playback', async () => {
    const fixture = setup('');
    await fixture.componentInstance['start']();
    TestBed.inject(HttpTestingController).expectNone('http://127.0.0.1:17832/api/speech/repeat');
  });

  it('does not submit the same pasted text again while it is being read', async () => {
    const fixture = setup('Same text');
    fixture.componentInstance['speechTracker'].set({ isSpeaking: true, currentText: 'Same text' } as never);
    await fixture.componentInstance['readManualText']();
    TestBed.inject(HttpTestingController).expectNone('http://127.0.0.1:17832/api/speech/text');
  });

  it('distinguishes generating the next audio part from active reading', () => {
    const fixture = setup('');
    fixture.componentInstance['connected'].set(true);
    fixture.componentInstance['speechTracker'].set({ isSpeaking: true, isPreparingAudio: true, currentCharIndex: 100 } as never);
    expect(fixture.componentInstance['playbackLabel']()).toBe('Preparing next part');
    fixture.componentInstance['speechTracker'].update(value => ({ ...value!, isPreparingAudio: false }));
    expect(fixture.componentInstance['playbackLabel']()).toBe('Reading aloud');
  });

  it('defaults to Kokoro Heart and offers Bella without Edge', () => {
    const fixture = setup('');
    expect(fixture.componentInstance['speechMode']()).toBe('kokoro');
    expect(fixture.componentInstance['kokoroVoice']()).toBe('af_heart');
    const html = fixture.nativeElement as HTMLElement;
    const options = Array.from(html.querySelectorAll('option')).map(option => option.textContent);
    expect(options).toContain('Heart US (default)');
    expect(options).toContain('Bella US');
    expect(options.some(option => option?.includes('Edge'))).toBe(false);
    expect(fixture.componentInstance['parseSpeechMode']('edge')).toBe('kokoro');
    expect(fixture.componentInstance['parseSpeechMode']('local')).toBe('local');
  });

  it('saves the selected Bella voice and restores it from settings', async () => {
    const fixture = setup('');
    const http = TestBed.inject(HttpTestingController);
    const save = fixture.componentInstance['setKokoroVoice']('af_bella');
    const request = http.expectOne('http://127.0.0.1:17832/api/settings');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ key: 'speech.kokoro.voice', value: 'af_bella' });
    request.flush({});
    await save;
    const load = fixture.componentInstance['loadSettings']();
    http.expectOne('http://127.0.0.1:17832/api/settings').flush([
      { key: 'speech.mode', value: 'kokoro' }, { key: 'speech.kokoro.voice', value: 'af_bella' },
      { key: 'speech.kokoro.speed', value: '1.25' }
    ]);
    await load;
    expect(fixture.componentInstance['kokoroVoice']()).toBe('af_bella');
    expect(fixture.componentInstance['kokoroSpeed']()).toBe(1.25);
    http.verify();
  });

  it('resumes paused playback without enqueuing a repeat', async () => {
    const fixture = setup('');
    fixture.componentInstance['speechTracker'].set({ isPaused: true } as never);
    const pending = fixture.componentInstance['start']();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://127.0.0.1:17832/api/speech/resume').flush(null);
    await pending;
    http.expectNone('http://127.0.0.1:17832/api/speech/repeat');
    http.verify();
  });

  it('only repeats explicitly after playback has finished', async () => {
    const fixture = setup('');
    const http = TestBed.inject(HttpTestingController);
    fixture.componentInstance['speechTracker'].set({ isSpeaking: true } as never);
    await fixture.componentInstance['repeat']();
    http.expectNone('http://127.0.0.1:17832/api/speech/repeat');
    fixture.componentInstance['speechTracker'].set(null);
    const pending = fixture.componentInstance['repeat']();
    http.expectOne('http://127.0.0.1:17832/api/speech/repeat').flush(null);
    await pending;
    http.verify();
  });

  it('keeps text on failure and allows retry, then clears the draft', async () => {
    const fixture = setup('Keep this text');
    const http = TestBed.inject(HttpTestingController);
    const pending = fixture.componentInstance['readManualText']();
    http.expectOne('http://127.0.0.1:17832/api/speech/text').flush({}, { status: 503, statusText: 'Unavailable' });
    await pending;
    expect(fixture.componentInstance['manualText']()).toBe('Keep this text');
    expect(fixture.componentInstance['manualBusy']()).toBe(false);
    expect(fixture.componentInstance['manualStatus']()).toContain('Could not queue');
    fixture.componentInstance['clearManualText']();
    expect(fixture.componentInstance['manualText']()).toBe('');
    expect(fixture.componentInstance['manualStatus']()).toBe('');
    http.verify();
  });

  it('separates pasted text and ChatGPT history between the two tabs', () => {
    const fixture = setup('Draft remains when switching tabs');
    const base = { sourceMessageId: null, createdAt: '2026-11-11T10:00:00Z', updatedAt: '2026-11-11T10:00:00Z', spokeAt: null };
    fixture.componentInstance['history'].set([
      { ...base, id: 1, role: 'manual', text: 'Pasted sample' },
      { ...base, id: 2, role: 'assistant', text: 'ChatGPT sample' }
    ]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.history').textContent).toContain('Pasted sample');
    expect(fixture.nativeElement.querySelector('.history').textContent).not.toContain('ChatGPT sample');
    fixture.nativeElement.querySelectorAll('.tabs button')[1].click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.history').textContent).toContain('ChatGPT sample');
    expect(fixture.nativeElement.querySelector('.history').textContent).not.toContain('Pasted sample');
    expect(fixture.componentInstance['manualText']()).toBe('Draft remains when switching tabs');
  });

  it('switches playback to a selected row and suppresses duplicate clicks', async () => {
    const fixture = setup('');
    const http = TestBed.inject(HttpTestingController);
    const pending = fixture.componentInstance['playCapturedMessage'](12);
    await fixture.componentInstance['playCapturedMessage'](12);
    http.expectOne('http://127.0.0.1:17832/api/speech/stop').flush(null);
    await Promise.resolve();
    http.expectOne('http://127.0.0.1:17832/api/speech/play/12').flush({ queued: true });
    await pending;
    expect(fixture.componentInstance['playbackBusy']()).toBe(false);
    http.verify();
  });

  it('shows pause, resume and stop only on the active history row', () => {
    const fixture = setup('');
    fixture.componentInstance['history'].set([{ id: 5, role: 'manual', text: 'Test text', sourceMessageId: null,
      createdAt: '2026-11-11T10:00:00Z', updatedAt: '2026-11-11T10:00:00Z', spokeAt: null }]);
    fixture.componentInstance['speechTracker'].set({ currentMessageId: '5', isSpeaking: true, isPaused: false } as never);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[aria-label="Pause message 5"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[aria-label="Stop message 5"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[aria-label="Play message 5"]')).toBeNull();
    fixture.componentInstance['speechTracker'].update(value => ({ ...value!, isPaused: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[aria-label="Resume message 5"]')).toBeTruthy();
  });

  it('highlights the exact timed word rather than estimating it from percentage', () => {
    const fixture = setup('');
    const text = '😀 First, second-word and last.';
    fixture.componentInstance['speechTracker'].set({ currentText: text, currentCharIndex: 0,
      currentWord: { start: text.indexOf('second-word'), length: 'second-word'.length } } as never);
    expect(fixture.componentInstance['trackerHighlight']()).toEqual({
      before: '😀 First, ', current: 'second-word', after: ' and last.'
    });
  });

  it('adds history to the queue without interrupting current audio', async () => {
    const fixture = setup('');
    const http = TestBed.inject(HttpTestingController);
    const pending = fixture.componentInstance['enqueueMessage'](7);
    http.expectOne('http://127.0.0.1:17832/api/speech/play/7').flush({ queued: true });
    await Promise.resolve();
    http.expectOne('http://127.0.0.1:17832/api/speech/queue').flush([{ id: 'q1', messageId: '7', text: 'Next text', status: 'ready', bufferedParts: 1, totalParts: 1, voice: 'af_heart' }]);
    await pending;
    expect(fixture.componentInstance['pendingSpeech']().length).toBe(1);
    http.expectNone('http://127.0.0.1:17832/api/speech/stop');
    http.verify();
  });

  it('sends real audio seek commands and uses the returned position', async () => {
    const fixture = setup('');
    const http = TestBed.inject(HttpTestingController);
    const pending = fixture.componentInstance['seekAudio'](-15, true);
    const request = http.expectOne('http://127.0.0.1:17832/api/speech/seek');
    expect(request.request.body).toEqual({ seconds: -15, relative: true });
    request.flush({ positionSeconds: 12, bufferedSeconds: 40 });
    await pending;
    expect(fixture.componentInstance['speechTracker']()?.positionSeconds).toBe(12);
    http.verify();
  });

  it('keeps the dragged slider position while playback updates arrive', async () => {
    const fixture = setup('');
    fixture.componentInstance['speechTracker'].set({ currentMessageId: '1', isFullyBuffered: true, positionSeconds: 5, bufferedSeconds: 40 } as never);
    fixture.componentInstance['previewSeek']('25');
    fixture.componentInstance['speechTracker'].set({ currentMessageId: '1', isFullyBuffered: true, positionSeconds: 6, bufferedSeconds: 40 } as never);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#audio-position').value).toBe('25');
    const pending = fixture.componentInstance['commitSeek']('25');
    TestBed.inject(HttpTestingController).expectOne('http://127.0.0.1:17832/api/speech/seek').flush({ currentMessageId: '1', isFullyBuffered: true, positionSeconds: 25, bufferedSeconds: 40 });
    await pending;
    expect(fixture.componentInstance['seekDraft']()).toBeNull();
  });

  it('does not resize the timeline as generated audio grows', () => {
    const fixture = setup('');
    const state = { currentMessageId: '1', currentText: 'The complete text is visible here.', positionSeconds: 5, bufferedSeconds: 20, isFullyBuffered: false };
    fixture.componentInstance['speechTracker'].set(state as never);
    fixture.detectChanges();
    const slider = fixture.nativeElement.querySelector('#audio-position') as HTMLInputElement;
    expect(slider.disabled).toBe(true);
    expect(slider.max).toBe('1');
    fixture.componentInstance['speechTracker'].set({ ...state, positionSeconds: 6, bufferedSeconds: 40 } as never);
    fixture.detectChanges();
    expect(slider.max).toBe('1');
    expect(slider.value).toBe('0');
    expect(fixture.nativeElement.querySelector('.buffering-note').textContent).toContain('0:40 ready');
    expect(fixture.nativeElement.querySelector('.time-caption').textContent).toContain('--:--');
    fixture.componentInstance['speechTracker'].set({ ...state, positionSeconds: 6, bufferedSeconds: 120, isFullyBuffered: true } as never);
    fixture.detectChanges();
    expect(slider.disabled).toBe(false);
    expect(slider.max).toBe('120');
    expect(slider.value).toBe('6');
    expect(fixture.nativeElement.querySelector('.time-caption').textContent).toContain('2:00');
  });

  it('keeps percentages beside the full text and out of the small player', () => {
    const fixture = setup('');
    const text = 'The complete text remains in the reader. Nothing is cut from it.';
    fixture.componentInstance['speechTracker'].set({ currentMessageId: '1', currentText: text,
      isSpeaking: true, isPaused: true, positionSeconds: 31, bufferedSeconds: 100, isFullyBuffered: true } as never);
    fixture.detectChanges();
    const player = fixture.nativeElement.querySelector('aside.player') as HTMLElement;
    expect(player.querySelector('progress')).toBeNull();
    expect(player.textContent).not.toContain('%');
    expect(fixture.nativeElement.querySelector('.reader-panel').textContent).toContain('31%');
    expect(fixture.nativeElement.querySelector('.word-reader').textContent).toBe(text);
  });

  it('shows saved partial progress, completed progress, and Continue', () => {
    const fixture = setup('');
    const base = { role: 'manual' as const, text: 'Saved sample', sourceMessageId: null,
      createdAt: '2026-11-11T10:00:00Z', updatedAt: '2026-11-11T10:00:00Z', spokeAt: null };
    fixture.componentInstance['history'].set([
      { ...base, id: 41, playbackPositionSeconds: 12, playbackProgressPercent: 37, playbackCompleted: false },
      { ...base, id: 42, playbackProgressPercent: 100, playbackCompleted: true }
    ]);
    fixture.detectChanges();
    const labels = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.source-label')).map(node => node.textContent);
    expect(labels[0]).toContain('37%');
    expect(labels[1]).toContain('100%');
    expect(fixture.nativeElement.querySelector('[aria-label="Play message 41"]').textContent).toContain('Continue');
  });
  it('confirms deletion and removes the text, queue copies and current reader', async () => {
    const fixture = setup('');
    const app = fixture.componentInstance;
    const base = { role: 'manual', text: 'Delete me', updatedAt: '2026-11-11T10:00:00Z' };
    app['history'].set([{ ...base, id: 41 }, { ...base, id: 42 }] as never);
    app['pendingSpeech'].set([{ id: 'a', messageId: '41', text: 'Delete me' }, { id: 'b', messageId: '42', text: 'Keep me' }] as never);
    app['speechTracker'].set({ currentMessageId: '41', currentText: 'Delete me' } as never);
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[aria-label="Delete message 41"]').click();
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectNone('http://127.0.0.1:17832/api/history/41');
    expect(fixture.nativeElement.querySelector('.delete-confirmation')).toBeTruthy();
    const pending = app['deleteText'](41);
    const request = http.expectOne('http://127.0.0.1:17832/api/history/41');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await pending;
    expect(app['history']().map(item => item.id)).toEqual([42]);
    expect(app['pendingSpeech']().map(item => item.messageId)).toEqual(['42']);
    expect(app['speechTracker']()).toBeNull();
    expect(app['pendingDeleteId']()).toBeNull();
  });

  it('keeps a text when deletion fails', async () => {
    const fixture = setup('');
    await fixture.whenStable();
    fixture.componentInstance['history'].set([{ id: 41, role: 'manual', text: 'Keep me' }] as never);
    const pending = fixture.componentInstance['deleteText'](41);
    TestBed.inject(HttpTestingController).expectOne('http://127.0.0.1:17832/api/history/41')
      .flush({}, { status: 500, statusText: 'Error' });
    await pending;
    expect(fixture.componentInstance['history']().length).toBe(1);
    expect(fixture.componentInstance['message']()).toContain('Could not delete');
    expect(fixture.componentInstance['deletingId']()).toBeNull();
  });

  it('removes decorative connection badges and the footer', () => {
    const fixture = setup('');
    expect(fixture.nativeElement.querySelector('.connection, .local-tag, .once-note, footer')).toBeNull();
    expect(fixture.nativeElement.querySelector('.close-button').getAttribute('aria-label')).toBe('Close voice settings');
  });

});

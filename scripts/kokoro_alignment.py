"""Map Kokoro token timestamps to original UTF-16 text offsets for the web/.NET player."""
def align_words(source, tokens, cursor=0, time_offset=0):
    words = []
    for token in tokens or []:
        if not token.text or not token.text.strip():
            continue
        start = source.find(token.text, cursor)
        if start < 0:
            continue
        cursor = start + len(token.text)
        if token.start_ts is None or token.end_ts is None or not any(c.isalnum() for c in token.text):
            continue
        words.append({
            'start': len(source[:start].encode('utf-16-le')) // 2,
            'length': len(token.text.encode('utf-16-le')) // 2,
            'startSeconds': time_offset + max(0, token.start_ts),
            'endSeconds': time_offset + max(token.start_ts, token.end_ts),
        })
    return words, cursor

import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from kokoro_alignment import align_words

def token(text, start, end):
    return SimpleNamespace(text=text, start_ts=start, end_ts=end)

class AlignmentTests(unittest.TestCase):
    def test_repeated_words_and_punctuation_use_original_offsets(self):
        words, cursor = align_words('Hello, hello again!', [token('Hello', 0, .4), token(',', .4, .5), token('hello', .5, 1), token('again', 1, 1.5)])
        self.assertEqual([(w['start'], w['length']) for w in words], [(0, 5), (7, 5), (13, 5)])
        self.assertEqual(cursor, 18)

    def test_emoji_offsets_match_utf16_and_multiple_audio_parts(self):
        words, cursor = align_words('😀 One\r\nTwo', [token('One', 0, .5)])
        self.assertEqual(words[0]['start'], 3)
        second, _ = align_words('😀 One\r\nTwo', [token('Two', 0, .4)], cursor, 2)
        self.assertEqual(second[0]['start'], 8)
        self.assertEqual(second[0]['startSeconds'], 2)

    def test_missing_timing_is_not_guessed(self):
        words, _ = align_words('No timing', [token('No', None, None), token('missing', 0, 1)])
        self.assertEqual(words, [])

if __name__ == '__main__': unittest.main()

"""Island example (Python): count words in the clipboard and copy a cleaned-up version back.

Button setup:  Action = Run script, Script file = this file,
               Output handling = JsonProtocol

The clipboard text comes from the ISLAND_CLIPBOARD environment variable, which is safer
than passing "{clipboard}" as an argument (quotes and newlines survive intact).
Island sets PYTHONIOENCODING=utf-8, so printing non-ASCII text is safe.
The LAST non-empty line of stdout is parsed as JSON.
"""
import json
import os
import re
import sys

text = os.environ.get("ISLAND_CLIPBOARD") or " ".join(sys.argv[1:])

if not text.strip():
    print(json.dumps({"error": "The clipboard is empty (or doesn't contain text)."}))
    sys.exit(0)

words = re.findall(r"\b\w+\b", text)
cleaned = re.sub(r"[ \t]+", " ", text).strip()

print(f"debug: {len(text)} characters received")  # log line, ignored by Island
print(json.dumps({
    "notify": f"{len(words)} words, {len(text)} characters. Cleaned text copied.",
    "clipboard": cleaned,
}))

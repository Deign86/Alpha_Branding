"""Fills table-of-contents page numbers in user-manual.html from a first-pass PDF render.

Usage: python fill_toc.py <first-pass.pdf> <source.html> <output.html>
"""
import re
import sys

from pypdf import PdfReader

pdf_path, html_in, html_out = sys.argv[1:4]
pages = [(page.extract_text() or "") for page in PdfReader(pdf_path).pages]

def kicker_lines(text, n=4):
    # Letter-spaced kickers extract as "C H A P T E R  1"; compare without whitespace.
    return {re.sub(r"\s+", "", line).lower() for line in text.strip().splitlines()[:n]}

targets = {}
for number in range(1, 17):
    for index, text in enumerate(pages):
        if f"chapter{number}" in kicker_lines(text):
            targets[f"ch{number}"] = index + 1
            break

toc_page = next(i for i, t in enumerate(pages) if "table of contents" in t.lower())
for index, text in enumerate(pages):
    if index > toc_page and "Hiding details with the PEN tool" in text:
        targets["ch7pen"] = index + 1
        break

missing = [f"ch{n}" for n in range(1, 17) if f"ch{n}" not in targets] + ([] if "ch7pen" in targets else ["ch7pen"])
if missing:
    sys.exit(f"Could not locate: {', '.join(missing)}")

html = open(html_in, encoding="utf-8").read()
for key, page in targets.items():
    html = html.replace(f'data-toc="{key}"></span>', f'data-toc="{key}">{page}</span>')
open(html_out, "w", encoding="utf-8").write(html)
print(f"{len(pages)} pages;", ", ".join(f"{k}={v}" for k, v in targets.items()))

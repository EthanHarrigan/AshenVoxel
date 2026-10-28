#!/usr/bin/env python3
"""Check repository Markdown links to local files, including nested documentation.

External URLs are deliberately not fetched. This static check does not validate
Unity import, source-code claims, or remote-link availability.
"""
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
SKIP = {'.git', '.terrain-cache', 'Library', 'Temp', 'Logs', 'obj', 'bin', 'Builds', '__pycache__'}
INLINE = re.compile(r'!?\[[^\]\n]*\]\(\s*(<[^>]+>|[^\s)]+)(?:\s+["\'][^\n]*["\'])?\s*\)')
REFERENCE = re.compile(r'^\s{0,3}\[[^\]]+\]:\s*(<[^>]+>|\S+)')


def prose_lines(document):
    fence = None
    for number, line in enumerate(document.splitlines(), 1):
        marker = re.match(r'^\s{0,3}(`{3,}|~{3,})', line)
        if marker:
            token = marker[1]
            if fence is None:
                fence = token
            elif token[0] == fence[0] and len(token) >= len(fence):
                fence = None
            continue
        if fence is None:
            yield number, line


def main():
    errors = []
    documents = 0
    checked = 0
    for source in sorted(ROOT.rglob('*.md')):
        if any(part in SKIP for part in source.relative_to(ROOT).parts):
            continue
        documents += 1
        for number, line in prose_lines(source.read_text(encoding='utf-8')):
            targets = [match[1] for match in INLINE.finditer(line)]
            reference = REFERENCE.match(line)
            if reference:
                targets.append(reference[1])
            for target in targets:
                target = target.strip('<>')
                parsed = urlsplit(target)
                if parsed.scheme or parsed.netloc or not parsed.path:
                    continue
                checked += 1
                path = source.parent / unquote(parsed.path)
                if not path.exists():
                    errors.append(f'{source.relative_to(ROOT)}:{number}: missing local target {target}')
    if errors:
        print('\n'.join('FAIL ' + error for error in errors))
        return 1
    print(f'PASS documentation: {documents} Markdown files, {checked} local file links; external URLs not fetched')
    return 0


if __name__ == '__main__':
    sys.exit(main())

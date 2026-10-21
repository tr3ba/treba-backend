#!/usr/bin/env python3
"""Check the actual Mini App module graph through the public proxy, not only HTTP status."""
import re
import sys
from html.parser import HTMLParser
from urllib.parse import urljoin, urlsplit
from urllib.request import Request, urlopen


class ModuleScripts(HTMLParser):
    def __init__(self):
        super().__init__()
        self.sources = []

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag == 'script' and values.get('type') == 'module' and values.get('src'):
            self.sources.append(values['src'])


def check(origin):
    page = origin.rstrip('/') + '/telegram/'
    authority = urlsplit(page).netloc

    def read(url, javascript=False):
        if urlsplit(url).netloc != authority:
            raise ValueError('A local Mini App module resolved to a different origin')
        with urlopen(Request(url, headers={'Cache-Control': 'no-cache'}), timeout=15) as response:
            mime = response.headers.get_content_type()
            if javascript and mime not in ('application/javascript', 'text/javascript'):
                raise ValueError(f'{urlsplit(url).path} returned {mime}; expected JavaScript')
            return response.read().decode('utf-8')

    parser = ModuleScripts()
    parser.feed(read(page))
    if not parser.sources:
        raise ValueError('Mini App HTML contains no module entry point')
    pending = [urljoin(page, source) for source in parser.sources]
    checked = set()
    while pending:
        url = pending.pop()
        if url in checked:
            continue
        source = read(url, javascript=True)
        checked.add(url)
        imports = re.findall(r'''^\s*(?:import|export)\s+(?:[^'";]*?\s+from\s+)?['"]([^'"]+)['"]''', source, re.MULTILINE)
        for dependency in imports:
            if dependency.startswith(('.', '/')):
                pending.append(urljoin(url, dependency))
        print('Mini App module passed:', urlsplit(url).path)
    print('Mini App module graph passed:', len(checked), 'modules')


if __name__ == '__main__':
    check(sys.argv[1])

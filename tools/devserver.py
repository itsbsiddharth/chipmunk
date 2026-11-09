"""Serve chipmunk/ui for UI development (the page falls back to a mock host).

Windows' registry can map .js to text/plain, which browsers refuse for modules,
so the MIME types are pinned here.
"""
import functools
import http.server
import os
import sys

UI_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ui")


class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {
        **http.server.SimpleHTTPRequestHandler.extensions_map,
        ".js": "text/javascript",
        ".css": "text/css",
        ".html": "text/html",
        ".svg": "image/svg+xml",
    }

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 5178
    handler = functools.partial(Handler, directory=UI_DIR)
    http.server.ThreadingHTTPServer(("127.0.0.1", port), handler).serve_forever()

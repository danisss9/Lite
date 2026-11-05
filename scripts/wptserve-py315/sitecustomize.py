"""Python 3.15 compatibility for the pinned WPT serve stack.

Python 3.15 removed ``http.server.CGIHTTPRequestHandler`` (deprecated in 3.13). The
pinned WPT checkout's vendored pywebsocket3 still subclasses it at import time, so
``wpt serve`` cannot start on a modern interpreter. This module restores the removed
class with the surface pywebsocket3 uses (``cgi_directories``, ``is_cgi``,
``is_executable``); ``scripts/serve-wpt.ps1`` puts this directory on PYTHONPATH so the
interpreter imports it automatically at startup, before any WPT module runs. It is
inert on interpreters that still ship the class.

The harness serves WebSocket traffic over pywebsocket's own ports only, and no
conformance test requests CGI execution there, so the CGI execution machinery
(``run_cgi`` and the ``#!`` interpreter dispatch) is intentionally not reproduced: a
CGI-directory request degrades to a static file response instead of executing.
"""
import http.server
import os


def _restore_removed_cgi_handler():
    if hasattr(http.server, "CGIHTTPRequestHandler"):
        return

    class CGIHTTPRequestHandler(http.server.SimpleHTTPRequestHandler):
        """Minimal restoration of the class Python 3.15 removed."""

        cgi_directories = ["/cgi-bin", "/htbin"]

        def is_cgi(self):
            # The 3.12 contract: a path segment inside cgi_directories marks a CGI
            # request; the remaining path (with the leading directory) is cgi_info.
            path = self.path.split("?", 1)[0].split("#", 1)[0]
            segments = [part for part in path.split("/") if part]
            for index in range(1, len(segments) + 1):
                candidate = "/" + "/".join(segments[:index])
                if candidate in self.cgi_directories:
                    self.cgi_info = segments
                    return True
            return False

        def is_executable(self, path):
            return os.path.isfile(path) and os.access(path, os.X_OK)

    http.server.CGIHTTPRequestHandler = CGIHTTPRequestHandler


_restore_removed_cgi_handler()

#!/usr/bin/python3

import os
from http.server import SimpleHTTPRequestHandler
from socketserver import TCPServer

class Handler(SimpleHTTPRequestHandler):
	def __init__(self, *args, **kwargs):
		scriptpath = os.path.dirname(os.path.realpath(__file__))
		srvdir = os.path.join(scriptpath, 'Content', '.out')

		self.extensions_map = {'': 'text/html'}

		super().__init__(*args, directory=srvdir, **kwargs)

print('Server up on http://localhost:5080')
with TCPServer(('', 5080), Handler) as httpd:
	try:
		httpd.serve_forever()
	except KeyboardInterrupt:
		print('Server down.')

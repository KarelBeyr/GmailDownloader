#!/usr/bin/env python3

import json
from http.server import BaseHTTPRequestHandler, HTTPServer
from sentence_transformers import SentenceTransformer

# --- Load model ONCE at startup ---
MODEL_NAME = "sentence-transformers/distiluse-base-multilingual-cased-v2"
model = SentenceTransformer(MODEL_NAME)

class EmbeddingHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        # Read the request length
        content_length = int(self.headers['Content-Length'])
        # Read the raw POST data
        post_data = self.rfile.read(content_length)
        
        # Expect JSON with a key "text" or "sentence"
        # Example input JSON: {"text": "Hello in Czech is Ahoj!"}
        data = json.loads(post_data)
        
        # Extract input text from the JSON
        input_text = data.get("text", "")
        
        # Compute embedding for the string
        embedding = model.encode(input_text)
        
        # Convert embedding (a NumPy array) to a Python list for JSON
        embedding_list = embedding.tolist()
        
        # Prepare JSON response
        response = {
            "embedding": embedding_list
        }
        response_json = json.dumps(response).encode('utf-8')
        
        # Write response
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(response_json)


def run_server(port=8000):
    # Create an HTTP server, specifying the host and port
    httpd = HTTPServer(("0.0.0.0", port), EmbeddingHandler)
    print(f"Server running on http://0.0.0.0:{port}, using model: {MODEL_NAME}")
    httpd.serve_forever()

if __name__ == "__main__":
    run_server(port=8000)
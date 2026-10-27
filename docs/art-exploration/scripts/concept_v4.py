"""Generates a concept sheet with the OpenAI Images API from reference images and a prompt.
Usage: OPENAI_API_KEY=... python3 concept.py OUT PROMPT_FILE REF [REF ...]"""
import base64, io, os, sys, requests
from PIL import Image
out, prompt, refs = sys.argv[1], open(sys.argv[2]).read(), sys.argv[3:]
files = [('image[]', (os.path.basename(p), open(p, 'rb'), 'image/jpeg' if p.endswith('.jpg') else 'image/png')) for p in refs]
r = requests.post('https://api.openai.com/v1/images/edits', headers={'Authorization': f'Bearer {os.environ["OPENAI_API_KEY"]}'},
                  data={'model': 'gpt-image-2', 'prompt': prompt, 'size': '1536x1024', 'quality': 'medium', 'n': '1'},
                  files=files, timeout=900)
if r.status_code != 200:
    sys.exit(f'{out}: HTTP {r.status_code} {r.text[:300]}')
Image.open(io.BytesIO(base64.b64decode(r.json()['data'][0]['b64_json']))).save(out)
print(out, 'done')

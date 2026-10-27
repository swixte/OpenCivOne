"""Edits a game-screen mockup with the OpenAI Images API. Usage: OPENAI_API_KEY=... python3 edit.py BASE OUT PROMPT_FILE"""
import base64, io, os, sys, requests
from PIL import Image
base, out, prompt = sys.argv[1], sys.argv[2], open(sys.argv[3]).read()
with open(base, 'rb') as f:
    r = requests.post('https://api.openai.com/v1/images/edits', headers={'Authorization': f'Bearer {os.environ["OPENAI_API_KEY"]}'},
                      data={'model': 'gpt-image-2', 'prompt': prompt, 'size': '1536x1024', 'quality': 'medium', 'n': '1'},
                      files={'image[]': (os.path.basename(base), f, 'image/jpeg')}, timeout=900)
if r.status_code != 200:
    sys.exit(f'{out}: HTTP {r.status_code} {r.text[:300]}')
Image.open(io.BytesIO(base64.b64decode(r.json()['data'][0]['b64_json']))).save(out)
print(out, 'done')

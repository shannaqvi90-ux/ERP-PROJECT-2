# Critic p04 r11: on the planted SL1 build (port 20470), tenant B's digit choice reaches tenant A's answer.
exec(open('base_plant.py').read().split("spec = json.loads")[0])
import json
def put(r, body):
    s, h, b = r('PUT', '/api/identity/me/preferences', body); return s, json.loads(b)
print('A sets en/latn             ->', put(reqA, {'language': 'en', 'numerals': 'latn'})[1]['numerals'])
print('A language-only (ar), B idle ->', put(reqA, {'language': 'ar'})[1]['numerals'])
print('B (gulfsteel) sets ar/arab   ->', put(reqB, {'language': 'ar', 'numerals': 'arab'})[1]['numerals'])
s, a = put(reqA, {'language': 'en'})
print('A language-only (en), after B ->', a['numerals'], ' (A stored numerals:', json.loads(reqA('GET', '/api/auth/session')[2])['user']['numerals'], ')')
print('B sets en/latn back        ->', put(reqB, {'language': 'en', 'numerals': 'latn'})[1]['numerals'])
print('A language-only (ar), after B->', put(reqA, {'language': 'ar'})[1]['numerals'])
put(reqA, {'language': 'en', 'numerals': 'latn'})

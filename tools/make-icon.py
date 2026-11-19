"""Renders the DeskDeck icon (same design as the Android launcher icon) as agent/DeskDeck.Agent/deskdeck.ico.
Usage: python3 tools/make-icon.py   (needs Pillow)"""
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024; k = S / 108
pt = lambda x, y: (x * k, y * k)

def bez(p0, p1, p2, p3, n=40):
    out = []
    for i in range(n + 1):
        t = i / n; u = 1 - t
        out.append((u**3*p0[0] + 3*u*u*t*p1[0] + 3*u*t*t*p2[0] + t**3*p3[0],
                    u**3*p0[1] + 3*u*u*t*p1[1] + 3*u*t*t*p2[1] + t**3*p3[1]))
    return out

def make(size):
    im = Image.new('RGBA', (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, S - 1, S - 1], radius=S * 0.22, fill='#0A0716')
    mask = Image.new('L', (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([22*k, 34*k, 86*k, 74*k], radius=8*k, fill=255)
    layer = Image.new('RGBA', (S, S), '#2B2150'); ld = ImageDraw.Draw(layer)
    for ribbon, colour in (
        (bez((22, 64), (36, 50), (46, 60), (58, 50)) + bez((58, 50), (68, 42), (76, 44), (86, 40)), '#2DD4BF'),
        (bez((22, 70), (34, 58), (48, 68), (60, 58)) + bez((60, 58), (70, 50), (78, 54), (86, 48)), '#A78BFA')):
        ld.polygon([pt(*p) for p in ribbon] + [pt(86, 80), pt(22, 80)], fill=colour)
    im.paste(layer, (0, 0), mask)
    d.rounded_rectangle([22*k, 34*k, 86*k, 74*k], radius=8*k, outline='#DDD6FE', width=int(3 * k))
    return im.resize((size, size), Image.LANCZOS)

sizes = [16, 24, 32, 48, 64, 128, 256]
imgs = [make(s) for s in sizes]
out = Path(__file__).resolve().parent.parent / 'agent' / 'DeskDeck.Agent' / 'deskdeck.ico'
imgs[-1].save(out, format='ICO', sizes=[(s, s) for s in sizes], append_images=imgs[:-1])

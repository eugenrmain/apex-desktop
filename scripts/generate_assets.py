"""Generate local design assets from geometry; no downloaded media."""
from pathlib import Path
from math import exp
from PIL import Image, ImageDraw, ImageFilter

assets = Path(__file__).resolve().parents[1] / "Assets"
assets.mkdir(exist_ok=True)
canvas = Image.new("RGBA", (1400, 1000))
for box, color, blur in [((450, -300, 1400, 470), (47, 106, 240, 86), 160), ((950, 350, 1700, 1150), (133, 64, 229, 72), 180), ((-350, 500, 550, 1300), (31, 183, 222, 44), 180), ((1100, -150, 1650, 350), (218, 73, 167, 32), 150)]:
    layer = Image.new("RGBA", canvas.size)
    ImageDraw.Draw(layer).ellipse(box, fill=color)
    canvas = Image.alpha_composite(canvas, layer.filter(ImageFilter.GaussianBlur(blur)))
canvas.save(assets / "Backdrop.png")

# Original two-tongue flame narrowed to 82% width, on the 64-unit grid shared with Assets/Apex.svg and MainWindow.Brand.cs.
FLAME = [(35.28, 3), [(35.28, 17), (27.08, 21), (25.44, 32)], [(22.16, 29), (20.52, 24), (20.52, 18)], [(12.32, 28), (9.86, 37), (11.5, 46)], [(13.14, 57), (22.16, 62), (31.18, 62)], [(45.12, 62), (54.14, 51), (52.5, 37)], [(51.68, 26), (45.12, 19), (42.66, 13)], [(43.48, 24), (39.38, 29), (36.92, 30)], [(40.2, 19), (40.2, 11), (35.28, 3)]]
BOLT = [(32.82, 32.4), (27.9, 43.2), (31.84, 43.2), (29.87, 51.6), (37.74, 38.4), (33.81, 38.4), (35.78, 32.4)]
# Dark purple tip through indigo to deep blue at the base.
STOPS = [(0, (122, 69, 230)), (.5, (79, 75, 216)), (1, (47, 98, 240))]

def outline(shape):
    points = [shape[0]]
    for a, b, c in shape[1:]:
        start = points[-1]
        for step in range(1, 33):
            t = step / 32
            points.append(tuple((1-t)**3*start[i] + 3*(1-t)**2*t*a[i] + 3*(1-t)*t*t*b[i] + t**3*c[i] for i in range(2)))
    return points

def icon(size, plated=True):
    scale = 8
    px = size * scale
    image = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    if plated:
        ImageDraw.Draw(image).rounded_rectangle((0, 0, px - 1, px - 1), radius=px * .24, fill="#0A0F1E")
    # Plated icons keep breathing room; tiny sizes enlarge the flame slightly to stay legible.
    fill = (.84 if size <= 24 else .76) if plated else .96
    def at(x, y): return ((.5 + (x - 32) / 59 * fill) * px, (.5 + (y - 32.5) / 59 * fill) * px)
    mask = Image.new("L", image.size)
    ImageDraw.Draw(mask).polygon([at(x, y) for x, y in outline(FLAME)], fill=255)
    color = Image.new("RGBA", image.size)
    top, bottom = at(32, 3)[1], at(32, 62)[1]
    draw = ImageDraw.Draw(color)
    for y in range(px):
        t = min(1, max(0, (y - top) / (bottom - top)))
        (o0, c0), (o1, c1) = (STOPS[0], STOPS[1]) if t <= STOPS[1][0] else (STOPS[1], STOPS[2])
        k = (t - o0) / (o1 - o0)
        draw.line([(0, y), (px, y)], fill=tuple(int(c0[i] + (c1[i] - c0[i]) * k) for i in range(3)) + (255,))
    image.paste(color, (0, 0), mask)
    ImageDraw.Draw(image).polygon([at(x, y) for x, y in BOLT], fill="#FFFFFF")
    return image.resize((size, size), Image.Resampling.LANCZOS)

for name, size in [("Square44x44Logo.png", 44), ("Square150x150Logo.png", 150), ("StoreLogo.png", 50), ("AppIcon.png", 256)]: icon(size).save(assets / name)
for size in [16, 24, 32, 48, 256]:
    for suffix in ["", "_altform-unplated"]:
        icon(size, plated=not suffix).save(assets / f"Square44x44Logo.targetsize-{size}{suffix}.png")
# Each ICO frame is rendered at its own size so 16px keeps its heavier stroke.
frames = [icon(s) for s in [256, 48, 32, 24, 16]]
frames[0].save(assets / "Apex.ico", sizes=[(f.width, f.height) for f in frames], append_images=frames[1:])

# Short, quiet synthesized completion cue. Generated once, never synthesized on the UI thread.
import math, struct, wave
rate = 44100
with wave.open(str(assets / "Complete.wav"), "wb") as sound:
    sound.setnchannels(1)
    sound.setsampwidth(2)
    sound.setframerate(rate)
    frames = []
    for i in range(int(rate * .65)):
        t = i / rate
        value = 0
        for start, frequency in [(0, 246.94), (.09, 329.63), (.16, 493.88)]:
            age = t - start
            if age >= 0:
                envelope = min(1, age / .025) * math.exp(-age * 10)
                value += .065 * envelope * (math.sin(2 * math.pi * frequency * age + .08 * math.sin(2 * math.pi * 5 * age)) + .12 * math.sin(4 * math.pi * frequency * age))
        frames.append(struct.pack("<h", int(max(-1, min(1, value)) * 32767)))
    sound.writeframes(b"".join(frames))

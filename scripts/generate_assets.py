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

# Antialiased icon geometry matches the editable vector source.
def icon(size, plated=True):
    scale = 4
    image = Image.new("RGBA", (size * scale, size * scale), (0, 0, 0, 0))
    if plated:
        ImageDraw.Draw(image).rounded_rectangle((0,0,image.width-1,image.height-1),radius=image.width*.24,fill="#0B1022")
    def points(values): return [(int(((x-32)*.80/59+.5)*size*scale), int(((y-32.5)*.80/59+.5)*size*scale)) for x,y in values]
    flame = [(36,3)]
    def cubic(a,b,c):
        start = flame[-1]
        for step in range(1,33):
            t = step/32
            flame.append(tuple((1-t)**3*start[i]+3*(1-t)**2*t*a[i]+3*(1-t)*t*t*b[i]+t**3*c[i] for i in range(2)))
    for a,b,c in [((36,17),(26,21),(24,32)),((20,29),(18,24),(18,18)),((8,28),(5,37),(7,46)),((9,57),(20,62),(31,62)),((48,62),(59,51),(57,37)),((56,26),(48,19),(45,13)),((46,24),(41,29),(38,30)),((42,19),(42,11),(36,3))]: cubic(a,b,c)
    mask = Image.new("L", image.size)
    md = ImageDraw.Draw(mask)
    md.polygon(points(flame),fill=255)
    # Violet at the tip, blue/cyan at the base; the lightning bolt stays white.
    color = Image.new("RGBA", image.size)
    pixels = color.load()
    for y in range(image.height):
        for x in range(image.width):
            fx = (x/image.width-.5)*59/.80+32
            fy = (y/image.height-.5)*59/.80+32.5
            t = max(0,min(1,(fy-3)/59))
            body = (int(163-118*t),int(94+90*t),int(242+10*t))
            core = .55*exp(-((fx-32)**2+(fy-43)**2)/55)
            pixels[x,y] = tuple(int(base+(hot-base)*core) for base,hot in zip(body,(147,224,255)))+(255,)
    image.paste(color,(0,0),mask)
    ImageDraw.Draw(image).polygon(points([(33,34),(28,43),(32,43),(30,50),(38,39),(34,39),(36,34)]),fill="#FFFFFF")
    return image.resize((size,size),Image.Resampling.LANCZOS)

for name,size in [("Square44x44Logo.png",44),("Square150x150Logo.png",150),("StoreLogo.png",50),("AppIcon.png",256)]: icon(size).save(assets/name)
icon(256, plated=False).save(assets/"FlameMark.png")
for size in [16,24,32,48,256]:
    for suffix in ["", "_altform-unplated"]:
        icon(size, plated=not suffix).save(assets/f"Square44x44Logo.targetsize-{size}{suffix}.png")
icon(256).save(assets/"Apex.ico",sizes=[(16,16),(24,24),(32,32),(48,48),(256,256)])

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

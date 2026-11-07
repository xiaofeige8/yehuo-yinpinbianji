from pathlib import Path
from PIL import Image, ImageDraw

folder = Path(__file__).resolve().parent
scale = 4
icon = Image.new('RGBA', (256 * scale, 256 * scale))
draw = ImageDraw.Draw(icon)


def curve(start, segments):
    points = [start]
    current = start
    for control_a, control_b, end in segments:
        for index in range(1, 41):
            step = index / 40
            inverse = 1 - step
            points.append((inverse ** 3 * current[0] + 3 * inverse ** 2 * step * control_a[0] + 3 * inverse * step ** 2 * control_b[0] + step ** 3 * end[0], inverse ** 3 * current[1] + 3 * inverse ** 2 * step * control_a[1] + 3 * inverse * step ** 2 * control_b[1] + step ** 3 * end[1]))
        current = end
    return [(int(horizontal * scale), int(vertical * scale)) for horizontal, vertical in points]


draw.rounded_rectangle((8 * scale, 8 * scale, 248 * scale, 248 * scale), radius=53 * scale, fill='#E75B2D')
flame = curve((132, 28), [((140, 67), (196, 89), (194, 145)), ((196, 196), (164, 222), (124, 222)), ((78, 222), (56, 190), (59, 156)), ((60, 135), (74, 115), (89, 101)), ((86, 131), (105, 136), (108, 112)), ((120, 91), (104, 63), (132, 28))])
draw.polygon(flame, fill='#FFCE69')
draw.line([(147 * scale, 106 * scale), (147 * scale, 176 * scale)], fill='white', width=13 * scale)
draw.line([(148 * scale, 108 * scale), (173 * scale, 119 * scale)], fill='white', width=13 * scale)
draw.ellipse((108 * scale, 163 * scale, 153 * scale, 196 * scale), fill='white')
icon.resize((256, 256), Image.Resampling.LANCZOS).save(folder / 'app.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

"""Draws the PenButton icon and writes src/PenButton.ico (16 to 256 px) and docs/icon.png.

A dark rounded square, a white pen pointing down-left, and an indigo cap at the
pen's end for the top button, with two small "press" arcs. Small sizes drop the
arcs and use thicker shapes so they stay readable.

    python tools/make_icon.py
"""
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SS = 8  # supersampling


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


def draw(size):
    n = size * SS
    small = size <= 24

    # Background: rounded square with a subtle vertical gradient.
    margin = 0.04 * n if size >= 32 else 0.0
    radius = 0.23 * n
    grad = Image.new("RGBA", (n, n))
    top, bottom = (44, 44, 52, 255), (16, 16, 20, 255)
    gd = ImageDraw.Draw(grad)
    for y in range(n):
        gd.line([(0, y), (n, y)], fill=lerp(top, bottom, y / (n - 1)))
    mask = Image.new("L", (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle([margin, margin, n - margin, n - margin], radius=radius, fill=255)
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    img.paste(grad, (0, 0), mask)

    # Pen, drawn horizontally (tip on the left), then rotated 45 degrees.
    pen = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(pen)
    cy = n * 0.5
    length = n * (0.66 if small else 0.56)
    w = n * (0.22 if small else 0.165)
    x0 = (n - length) / 2 - n * 0.02
    x1 = x0 + length
    tip = n * (0.15 if small else 0.14)
    white = (255, 255, 255, 255)
    nib = (196, 196, 206, 255)
    # cone
    d.polygon([(x0, cy), (x0 + tip, cy - w / 2), (x0 + tip, cy + w / 2)], fill=white)
    # darker nib point
    k = 0.42
    d.polygon([(x0, cy), (x0 + tip * k, cy - w / 2 * k), (x0 + tip * k, cy + w / 2 * k)], fill=nib)
    # body: square where it meets the cone, rounded at the far end
    d.rectangle([x0 + tip - 2, cy - w / 2, x1 - w * 0.5, cy + w / 2], fill=white)
    d.rounded_rectangle([x1 - w * 1.2, cy - w / 2, x1, cy + w / 2], radius=w * 0.28, fill=white)
    # grip line
    if not small:
        gx = x0 + tip + n * 0.05
        d.line([(gx, cy - w / 2), (gx, cy + w / 2)], fill=(214, 214, 222, 255), width=max(1, int(n * 0.012)))
    # top button: indigo cap at the end
    cap_r = w * (0.62 if small else 0.58)
    cx = x1 - cap_r * 0.35
    d.ellipse([cx - cap_r, cy - cap_r, cx + cap_r, cy + cap_r], fill=(99, 102, 241, 255))
    # press arcs
    if not small:
        aw = max(2, int(n * 0.026))
        for rr in (cap_r * 1.6, cap_r * 2.2):
            d.arc([cx - rr, cy - rr, cx + rr, cy + rr], start=-38, end=38, fill=(165, 180, 252, 255), width=aw)

    pen = pen.rotate(45, resample=Image.BICUBIC, center=(n / 2, n / 2))
    # Nudge so the drawing sits optically centred.
    shifted = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    shifted.paste(pen, (int(-n * 0.035), int(n * 0.04)), pen)
    clipped = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    clipped.paste(shifted, (0, 0), Image.composite(shifted.split()[3], Image.new("L", (n, n), 0), mask))
    img = Image.alpha_composite(img, clipped)
    return img.resize((size, size), Image.LANCZOS)


def main():
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [draw(s) for s in sizes]
    ico = os.path.join(ROOT, "src", "PenButton.ico")
    frames[-1].save(ico, format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    os.makedirs(os.path.join(ROOT, "docs"), exist_ok=True)
    draw(512).save(os.path.join(ROOT, "docs", "icon.png"))
    print("wrote", ico)


if __name__ == "__main__":
    main()

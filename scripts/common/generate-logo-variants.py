#!/usr/bin/env python3
"""Generate the monochrome logo variants from assets/logo-black.png.

Writes logo-<name>-dark.png (black), logo-<name>-medium.png (grey) and logo-<name>-light.png (white), each the
logo's alpha channel filled with one color, into every folder that carries the Armada logo:

  assets/                          logo-mono-*.png   full size
  src/Armada.Server/wwwroot/img/   logo-mono-*.png   512 px
  src/Armada.Proxy/wwwroot/img/    logo-mono-*.png   512 px
  src/Armada.Harbor/Assets/        logo-tray-*.png   64 px square, for the menu bar or system tray

The dark tray variant is pure black on transparent, which is what macOS needs for a template image: the menu bar
tints it to match the other menu bar icons in light and dark mode.

Usage: python3 scripts/common/generate-logo-variants.py   (needs Pillow: pip install pillow)
"""
import os
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
COLORS = {"dark": (0, 0, 0), "medium": (142, 142, 147), "light": (255, 255, 255)}


def variant(alpha, color):
    img = Image.new("RGBA", alpha.size, color + (0,))
    img.putalpha(alpha)
    return img


def square(img, size, padding):
    box = img.getbbox()
    img = img.crop(box)
    side = max(img.size)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(img, ((side - img.width) // 2, (side - img.height) // 2))
    inner = size - 2 * padding
    canvas = canvas.resize((inner, inner), Image.LANCZOS)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(canvas, (padding, padding))
    return out


def main():
    alpha = Image.open(os.path.join(ROOT, "assets", "logo-black.png")).convert("RGBA").getchannel("A")
    targets = [
        ("assets", "mono", None),
        ("src/Armada.Server/wwwroot/img", "mono", 512),
        ("src/Armada.Proxy/wwwroot/img", "mono", 512),
        ("src/Armada.Harbor/Assets", "tray", 64),
    ]
    for folder, name, size in targets:
        for shade, color in COLORS.items():
            img = variant(alpha, color)
            if name == "tray":
                img = square(img, size, 2)
            elif size is not None:
                img = square(img, size, 0)
            path = os.path.join(ROOT, folder, "logo-" + name + "-" + shade + ".png")
            img.save(path, optimize=True)
            print("wrote", os.path.relpath(path, ROOT), img.size)


if __name__ == "__main__":
    main()

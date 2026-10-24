"""生成启动器图标（.ico）。

纯标准库实现：逐像素画出"真核细胞"意象——深色外膜 + 绿色膜环 + 绿色核仁，
再手写 PNG（zlib）并包进 ICO 容器。不使用任何 Minecraft 素材。
"""
import math
import struct
import zlib
from pathlib import Path

SIZE = 256
BG = (0x12, 0x16, 0x12)      # bg-base
ACCENT = (0x6F, 0xCF, 0x7A)  # accent
TRANSPARENT = (0, 0, 0)


def smoothstep(edge0, edge1, x):
    t = max(0.0, min(1.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3 - 2 * t)


def pixel(x, y):
    """返回 (r, g, b, a)。"""
    cx = cy = SIZE / 2.0
    d = math.hypot(x - cx + 0.5, y - cy + 0.5)

    # 圆角方形外膜：用超椭圆近似，四角自然收圆
    r = SIZE / 2.0 - 2.0
    nx = abs(x - cx + 0.5) / r
    ny = abs(y - cy + 0.5) / r
    # p 越大越接近方；这里 4.0 得到明显圆角
    inside = (nx ** 4.0 + ny ** 4.0) <= 1.0
    if not inside:
        return TRANSPARENT + (0,)

    # 抗锯齿边缘：超椭圆值越接近 1 越靠边
    v = (nx ** 4.0 + ny ** 4.0) ** 0.25
    edge = 1.0 - smoothstep(-1.5, 1.5, (v - 1.0) * r)
    alpha = int(255 * max(0.0, min(1.0, edge)))

    # 膜环：外径 0.78r，环宽约 10px
    outer, inner = 0.78 * r, 0.78 * r - 11.0
    ring = (1.0 - smoothstep(outer - 1.5, outer, d)) * smoothstep(inner - 1.5, inner, d)

    # 核仁：直径约 0.2 * SIZE
    nucleus = 1.0 - smoothstep(0.075 * SIZE, 0.095 * SIZE, d)

    base = BG
    t = max(ring, nucleus)
    color = tuple(int(base[i] + (ACCENT[i] - base[i]) * t) for i in range(3))
    return color + (alpha,)


def write_png(path, size):
    raw = bytearray()
    for y in range(size):
        raw.append(0)  # filter type 0
        for x in range(size):
            raw.extend(pixel(x, y))

    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + \
        chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
    Path(path).write_bytes(png)
    return png


def write_ico(path, png, size):
    header = struct.pack("<HHH", 0, 1, 1)
    # ICO 目录项里宽高各占 1 字节，256 要写成 0
    b = 0 if size >= 256 else size
    entry = struct.pack("<BBBBHHII", b, b, 0, 0, 1, 32, len(png), 22)
    Path(path).write_bytes(header + entry + png)


if __name__ == "__main__":
    out_dir = Path(__file__).resolve().parent.parent / "EukaryLauncher.App" / "Assets"
    out_dir.mkdir(parents=True, exist_ok=True)
    png = write_png(out_dir / "AppIcon.png", SIZE)
    write_ico(out_dir / "AppIcon.ico", png, SIZE)
    print(f"icon written: {out_dir / 'AppIcon.ico'} ({len(png)} bytes png)")

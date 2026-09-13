"""앱 아이콘 생성: FoldeClean/app.ico (16~256px) 와 ui/icon.png.
디자인: 보라→파랑 그라데이션 둥근 사각형 안에 흰 폴더와 체크 표시 (화면 로고와 동일).

py -3.11 make_icon.py
"""
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
C1, C2 = (108, 140, 255), (143, 108, 255)   # --accent, --accent2


def render(size: int) -> Image.Image:
    S = 4  # 슈퍼샘플링
    n = size * S
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    # 그라데이션 배경
    grad = Image.new("RGBA", (n, n))
    px = grad.load()
    for y in range(n):
        for x in range(n):
            t = (x + y) / (2 * n)
            px[x, y] = tuple(int(C1[i] * (1 - t) + C2[i] * t) for i in range(3)) + (255,)
    mask = Image.new("L", (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=int(n * 0.23), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    w = max(2, int(n * 0.075))
    white = (255, 255, 255, 255)
    # 폴더: 탭 + 본체 (외곽선 스타일)
    L, T, R, B = n * 0.18, n * 0.30, n * 0.82, n * 0.76
    tab_w = n * 0.24
    d.rounded_rectangle([L, T, R, B], radius=int(n * 0.07), outline=white, width=w)
    d.rounded_rectangle([L, T - n * 0.09, L + tab_w, T + w], radius=int(n * 0.04), fill=white)
    # 체크 표시
    cx, cy = n * 0.50, n * 0.55
    pts = [(cx - n * 0.14, cy), (cx - n * 0.04, cy + n * 0.10), (cx + n * 0.16, cy - n * 0.11)]
    d.line(pts, fill=white, width=w, joint="curve")
    for p in pts:
        d.ellipse([p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2], fill=white)
    return img.resize((size, size), Image.LANCZOS)


def main():
    sizes = [16, 24, 32, 48, 64, 128, 256]
    imgs = [render(s) for s in sizes]
    ico = os.path.join(HERE, "FoldeClean", "app.ico")
    imgs[-1].save(ico, format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    imgs[-1].save(os.path.join(HERE, "ui", "icon.png"))
    print("written", ico)


if __name__ == "__main__":
    main()

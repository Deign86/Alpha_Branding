"""Draws simple, photo-like property scenes used as sample input for manual screenshots."""
import os
import sys
import random
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
random.seed(7)


def vgrad(draw, box, top, bottom):
    x0, y0, x1, y1 = box
    h = max(1, y1 - y0)
    for i in range(h):
        t = i / h
        c = tuple(int(top[k] + (bottom[k] - top[k]) * t) for k in range(3))
        draw.line([(x0, y0 + i), (x1, y0 + i)], fill=c)


def font(size):
    for name in ("arialbd.ttf", "segoeuib.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def finish(img, name):
    img = img.filter(ImageFilter.GaussianBlur(0.8))
    img.save(os.path.join(OUT, name), quality=92)


def exterior():
    w, h = 1800, 1200
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)
    vgrad(d, (0, 0, w, 760), (98, 160, 222), (200, 225, 245))
    for cx, cy in ((300, 160), (1350, 120), (900, 220)):
        for _ in range(8):
            r = random.randint(50, 90)
            ox, oy = random.randint(-120, 120), random.randint(-30, 30)
            d.ellipse((cx + ox - r, cy + oy - r * 0.6, cx + ox + r, cy + oy + r * 0.6), fill=(245, 248, 252))
    vgrad(d, (0, 760, w, h), (88, 150, 70), (52, 102, 44))
    # trees
    for tx in (120, 1600, 1720):
        d.rectangle((tx - 14, 520, tx + 14, 790), fill=(84, 60, 40))
        d.ellipse((tx - 130, 330, tx + 130, 620), fill=(46, 98, 52))
    # house body
    d.rectangle((420, 430, 1380, 820), fill=(236, 228, 214))
    d.polygon([(370, 440), (900, 210), (1430, 440)], fill=(70, 66, 72))
    d.rectangle((1080, 260, 1150, 380), fill=(150, 90, 70))
    # windows
    for x in (480, 640, 1120, 1260):
        d.rectangle((x, 500, x + 110, 640), fill=(255, 255, 255))
        d.rectangle((x + 8, 508, x + 102, 632), fill=(120, 160, 190))
        d.line((x + 55, 508, x + 55, 632), fill=(255, 255, 255), width=5)
    # door + house number plate (good redaction demo)
    d.rectangle((840, 560, 960, 820), fill=(110, 70, 45))
    d.ellipse((935, 690, 948, 703), fill=(220, 190, 90))
    d.rectangle((990, 600, 1060, 640), fill=(30, 30, 30))
    d.text((1000, 604), "128", font=font(30), fill=(240, 240, 240))
    # garage
    d.rectangle((1380, 560, 1560, 820), fill=(236, 228, 214))
    d.rectangle((1395, 600, 1545, 820), fill=(210, 210, 210))
    for y in range(620, 820, 30):
        d.line((1395, y, 1545, y), fill=(180, 180, 180), width=3)
    # path
    d.polygon([(860, 820), (940, 820), (1020, h), (780, h)], fill=(200, 196, 186))
    for _ in range(60):
        x = random.randint(380, 1400)
        d.ellipse((x - 30, 790, x + 30, 840), fill=(60, 120 + random.randint(0, 30), 55))
    finish(img, "01_Exterior_Front.jpg")


def living_room():
    w, h = 1800, 1200
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)
    vgrad(d, (0, 0, w, 820), (232, 226, 214), (214, 204, 188))
    vgrad(d, (0, 820, w, h), (168, 124, 86), (126, 88, 58))
    for x in range(0, w, 120):
        d.line((x, 820, x - 200, h), fill=(140, 100, 68), width=2)
    # window with view
    d.rectangle((1080, 140, 1620, 640), fill=(250, 250, 250))
    vgrad(d, (1100, 160, 1600, 620), (150, 200, 240), (220, 238, 250))
    d.line((1350, 160, 1350, 620), fill=(250, 250, 250), width=10)
    d.ellipse((1150, 420, 1340, 620), fill=(80, 140, 80))
    # art on wall
    d.rectangle((300, 230, 760, 470), fill=(60, 50, 40))
    vgrad(d, (316, 246, 744, 454), (197, 160, 89), (120, 80, 50))
    # sofa
    d.rounded_rectangle((180, 560, 920, 860), 40, fill=(70, 82, 98))
    d.rounded_rectangle((220, 520, 880, 650), 30, fill=(84, 98, 116))
    for x in (260, 470, 680):
        d.rounded_rectangle((x, 640, x + 190, 740), 20, fill=(96, 110, 130))
    d.rounded_rectangle((300, 560, 400, 640), 14, fill=(197, 160, 89))
    # coffee table + rug
    d.ellipse((260, 900, 1300, 1140), fill=(210, 200, 182))
    d.rounded_rectangle((520, 880, 1000, 960), 12, fill=(60, 44, 32))
    # lamp + plant
    d.rectangle((1000, 420, 1012, 840), fill=(40, 40, 40))
    d.polygon([(950, 420), (1062, 420), (1040, 340), (972, 340)], fill=(240, 230, 200))
    d.rectangle((1640, 700, 1740, 840), fill=(180, 160, 140))
    d.ellipse((1580, 480, 1800, 720), fill=(60, 110, 60))
    finish(img, "02_Living_Room.jpg")


def kitchen_portrait():
    w, h = 1000, 1500
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)
    vgrad(d, (0, 0, w, 1050), (244, 242, 236), (226, 222, 214))
    vgrad(d, (0, 1050, w, h), (190, 186, 178), (150, 146, 140))
    for x in range(0, w, 160):
        d.line((x, 1050, x, h), fill=(170, 166, 158), width=2)
    # upper cabinets
    for x in range(60, 940, 220):
        d.rectangle((x, 120, x + 200, 460), fill=(52, 70, 66))
        d.rectangle((x + 92, 280, x + 108, 330), fill=(197, 160, 89))
    # backsplash
    for y in range(500, 700, 40):
        for x in range(0, w, 80):
            d.rectangle((x + 2, y + 2, x + 78, y + 38), fill=(236, 236, 232))
    # counter + base cabinets
    d.rectangle((0, 700, w, 740), fill=(230, 226, 220))
    d.rectangle((0, 740, w, 1050), fill=(52, 70, 66))
    for x in range(40, w, 240):
        d.rectangle((x, 780, x + 200, 1020), outline=(40, 56, 52), width=6)
        d.rectangle((x + 80, 800, x + 120, 812), fill=(197, 160, 89))
    # pendant lights
    for x in (300, 700):
        d.line((x, 0, x, 300), fill=(30, 30, 30), width=4)
        d.polygon([(x - 60, 360), (x + 60, 360), (x + 30, 300), (x - 30, 300)], fill=(197, 160, 89))
    d.ellipse((380, 600, 460, 700), fill=(200, 80, 60))
    d.rectangle((560, 620, 640, 700), fill=(240, 240, 240))
    finish(img, "03_Kitchen.jpg")


def bedroom_portrait():
    w, h = 1000, 1500
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)
    vgrad(d, (0, 0, w, 1000), (206, 214, 222), (186, 194, 204))
    vgrad(d, (0, 1000, w, h), (200, 190, 176), (168, 156, 140))
    # headboard + bed
    d.rounded_rectangle((120, 520, 880, 900), 30, fill=(90, 74, 60))
    d.rounded_rectangle((90, 820, 910, 1180), 30, fill=(245, 244, 240))
    d.rectangle((90, 1000, 910, 1180), fill=(197, 160, 89))
    d.rounded_rectangle((170, 760, 470, 880), 30, fill=(255, 255, 255))
    d.rounded_rectangle((530, 760, 830, 880), 30, fill=(255, 255, 255))
    # side lamps + art
    for x in (40, 900):
        d.rectangle((x, 900, x + 70, 1020), fill=(60, 50, 40))
    d.rectangle((300, 220, 700, 440), fill=(60, 50, 40))
    vgrad(d, (314, 234, 686, 426), (110, 140, 170), (220, 200, 160))
    # family photo frame on nightstand (redaction demo)
    d.rectangle((48, 820, 104, 896), fill=(30, 30, 30))
    d.ellipse((60, 832, 92, 864), fill=(220, 180, 150))
    finish(img, "04_Bedroom.jpg")


exterior()
living_room()
kitchen_portrait()
bedroom_portrait()
print("ok", os.listdir(OUT))

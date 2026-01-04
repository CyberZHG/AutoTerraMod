import math
import sys
from PIL import Image

OUTLINE = (34, 24, 26)
SKY_TOP = (92, 142, 226)
SKY_BOTTOM = (156, 196, 240)
CLOUD = (240, 246, 252)
CLOUD_SHADE = (206, 222, 240)
GRASS_LIGHT = (126, 206, 78)
GRASS = (86, 170, 52)
GRASS_DARK = (56, 122, 40)
DIRT = (151, 102, 60)
DIRT_DARK = (112, 72, 40)
DIRT_LIGHT = (176, 124, 76)
STONE = (128, 128, 138)
STONE_DARK = (92, 92, 104)
WOOD_LIGHT = (196, 142, 84)
WOOD = (166, 112, 62)
WOOD_DARK = (116, 74, 38)
WALL = (104, 70, 44)
WALL_DARK = (78, 52, 32)
DOOR = (134, 88, 46)
DOOR_DARK = (96, 60, 30)
METAL = (208, 200, 168)
FLAME_CORE = (255, 248, 196)
FLAME = (255, 196, 64)
FLAME_EDGE = (238, 112, 34)
GLOW = (255, 226, 150)
GLASS = (164, 214, 238)
GLASS_SHINE = (226, 244, 252)

SPRITE_COLORS = {
    'K': OUTLINE,
    'H': (110, 62, 30), 'h': (150, 90, 46),
    'F': (242, 198, 160), 'f': (214, 160, 122),
    'E': (250, 250, 250), 'P': (40, 52, 96),
    'R': (214, 64, 64), 'r': (160, 44, 52), 'q': (240, 112, 104),
    'L': (66, 62, 104), 'l': (44, 42, 72),
    'S': (96, 62, 36),
    'W': (150, 100, 56), 'w': (102, 66, 34),
    'C': (212, 132, 62), 'c': (156, 88, 40), 'Y': (246, 186, 112),
}

PLAYER = [
    "....KKKKKK....",
    "...KhHhHHhK...",
    "..KHhHHHHHHK..",
    "..KHHHHHHHHK..",
    "..KHHFFFFHHK..",
    "..KHFFFFEPFK..",
    "..KHFFFFEPFK..",
    "...KFFFFFFfK..",
    "...KfFFFFFK...",
    "....KKFFKK....",
    "...KqRRRRRK...",
    "..KqRRRRRRRKK.",
    "..KRrRRRRRRFFK",
    "..KRrRRRRRRKKK",
    "..KRrrRRRRrK..",
    "..KLLLLLLLLK..",
    "..KLlLLLLLlK..",
    "..KLlLKKLLlK..",
    "..KLlK..KLlK..",
    "..KLlK..KLlK..",
    "..KSSK..KSSK..",
    ".KSSSK..KSSSK.",
    ".KKKKK..KKKKK.",
]

PICKAXE = [
    "..KKKK.....",
    ".KYYCCKK...",
    "..KKKCCCK..",
    ".....KWCCK.",
    "....KWK.KCK",
    "...KWK...KK",
    "..KWK......",
    ".KWK.......",
    "KWK........",
    "KK.........",
]


class Canvas:
    def __init__(self, size):
        self.size = size
        self.pixels = [[SKY_BOTTOM] * size for _ in range(size)]

    def put(self, x, y, color):
        if 0 <= x < self.size and 0 <= y < self.size and color is not None:
            self.pixels[y][x] = color

    def get(self, x, y):
        return self.pixels[y][x]

    def rect(self, x0, y0, x1, y1, color):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.put(x, y, color)

    def sprite(self, rows, left, top):
        for dy, row in enumerate(rows):
            for dx, glyph in enumerate(row):
                if glyph != '.':
                    self.put(left + dx, top + dy, SPRITE_COLORS[glyph])

    def blend(self, x, y, color, amount):
        if 0 <= x < self.size and 0 <= y < self.size:
            base = self.pixels[y][x]
            self.pixels[y][x] = tuple(int(b + (c - b) * amount) for b, c in zip(base, color))

    def save(self, path, scale):
        image = Image.new("RGB", (self.size * scale, self.size * scale))
        for y in range(self.size):
            for x in range(self.size):
                image.paste(self.pixels[y][x], (x * scale, y * scale, (x + 1) * scale, (y + 1) * scale))
        image.save(path)


def lerp(a, b, t):
    return tuple(int(x + (y - x) * t) for x, y in zip(a, b))


def sky(c, horizon):
    for y in range(horizon):
        c.rect(0, y, c.size - 1, y, lerp(SKY_TOP, SKY_BOTTOM, y / max(1, horizon - 1)))


def cloud(c, x, y, width):
    c.rect(x + 1, y, x + width - 2, y, CLOUD)
    c.rect(x, y + 1, x + width - 1, y + 1, CLOUD)
    c.rect(x + 1, y + 2, x + width - 2, y + 2, CLOUD_SHADE)


def ground(c, top):
    noise = [7, 2, 11, 5, 13, 3, 9, 1, 12, 6, 10, 4, 8, 0]
    for x in range(c.size):
        c.put(x, top, GRASS_LIGHT if x % 3 else GRASS)
        c.put(x, top + 1, GRASS)
        c.put(x, top + 2, GRASS_DARK if noise[x % len(noise)] % 2 else GRASS)
        for y in range(top + 3, c.size):
            n = noise[(x * 3 + y * 5) % len(noise)]
            c.put(x, y, DIRT_DARK if n < 2 else DIRT_LIGHT if n > 11 else DIRT)
        if noise[x % len(noise)] > 9:
            c.put(x, top - 1, GRASS_LIGHT)
    for sx, sy in [(4, 6), (17, 4), (30, 7), (36, 5)]:
        for dx, dy in [(0, 0), (1, 0), (0, 1), (1, 1), (2, 1)]:
            c.put(sx + dx, top + sy + dy, STONE if dy == 0 else STONE_DARK)


def wood_block(c, x0, y0, width, height):
    for y in range(y0, y0 + height):
        row = y - y0
        for x in range(x0, x0 + width):
            offset = (x - x0 + (2 if (row // 2) % 2 else 0)) % 4
            if row % 2 == 1:
                color = WOOD_DARK if offset == 3 else WOOD
            else:
                color = WOOD_LIGHT if offset == 0 else WOOD
            c.put(x, y, color)


def wood_wall(c, x0, y0, x1, y1):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            seam = (y - y0) % 4 == 3 or ((x - x0 + (2 if ((y - y0) // 4) % 2 else 0)) % 5 == 4)
            c.put(x, y, WALL_DARK if seam else WALL)


def house(c, left, floor, width, height):
    right = left + width - 1
    ceiling = floor - height
    wood_wall(c, left + 2, ceiling + 2, right - 2, floor - 1)
    wood_block(c, left, ceiling, 2, height)
    wood_block(c, right - 1, ceiling, 2, height)
    wood_block(c, left, ceiling, width, 2)
    for rise in range(1, width // 2 + 1):
        x0 = left - 2 + rise * 2
        x1 = right + 2 - rise * 2
        if x0 > x1:
            break
        wood_block(c, x0, ceiling - rise * 2, x1 - x0 + 1, 2)
        c.rect(x0, ceiling - rise * 2 + 1, x1, ceiling - rise * 2 + 1, WOOD_DARK)
    door_x = left + 3
    c.rect(door_x - 1, floor - 8, door_x + 3, floor - 1, OUTLINE)
    c.rect(door_x, floor - 7, door_x + 2, floor - 1, DOOR)
    c.rect(door_x + 1, floor - 7, door_x + 1, floor - 1, DOOR_DARK)
    c.rect(door_x, floor - 4, door_x + 2, floor - 4, DOOR_DARK)
    c.put(door_x + 2, floor - 3, METAL)
    window_x, window_y = left + 8, ceiling + 4
    c.rect(window_x - 1, window_y - 1, window_x + 3, window_y + 3, WOOD_DARK)
    c.rect(window_x, window_y, window_x + 2, window_y + 2, GLASS)
    c.put(window_x, window_y, GLASS_SHINE)
    c.put(window_x + 1, window_y, GLASS_SHINE)
    c.put(window_x, window_y + 1, GLASS_SHINE)
    torch_x, torch_y = right - 3, ceiling + 5
    for dx in range(-3, 4):
        for dy in range(-3, 4):
            distance = math.hypot(dx, dy)
            if distance <= 3.2:
                c.blend(torch_x + dx, torch_y + dy, GLOW, 0.45 * (1 - distance / 3.4))
    c.put(torch_x, torch_y + 1, WOOD_DARK)
    c.put(torch_x, torch_y + 2, WOOD_DARK)
    c.put(torch_x, torch_y, FLAME)
    c.put(torch_x, torch_y - 1, FLAME_EDGE)
    c.put(torch_x, torch_y, FLAME_CORE)
    c.put(torch_x - 1, torch_y, FLAME_EDGE)
    c.put(torch_x + 1, torch_y, FLAME_EDGE)
    return ceiling - width // 2


def flags(c, positions):
    colors = [(224, 70, 70), (76, 136, 240), (90, 200, 100), (240, 196, 64)]
    for (x, base), color in zip(positions, colors):
        c.rect(x, base - 6, x, base, OUTLINE)
        c.rect(x + 1, base - 6, x + 3, base - 5, color)
        c.rect(x + 1, base - 4, x + 2, base - 4, color)
        c.put(x + 3, base - 5, lerp(color, OUTLINE, 0.35))


def large_icon():
    c = Canvas(40)
    sky(c, 30)
    cloud(c, 24, 3, 7)
    cloud(c, 4, 6, 6)
    house(c, 0, 30, 17, 11)
    flags(c, [(3, 15), (6, 13), (10, 13), (13, 15)])
    ground(c, 30)
    c.sprite(PICKAXE, 29, 12)
    c.sprite(PLAYER, 17, 8)
    return c


def small_icon():
    c = Canvas(30)
    sky(c, 23)
    cloud(c, 2, 3, 6)
    ground(c, 23)
    flags(c, [(21, 22), (25, 22)])
    c.sprite(PICKAXE, 13, 5)
    c.sprite(PLAYER, 1, 1)
    return c


out = sys.argv[1]
large_icon().save(f"{out}/icon.png", 2)
small_icon().save(f"{out}/icon_small.png", 1)

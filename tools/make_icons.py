"""Compose the mod icons from vanilla Terraria sprites.

Usage: python3 tools/make_icons.py <extracted Terraria Images dir> <output dir>
"""
import sys
from PIL import Image, ImageChops

ASSETS = sys.argv[1]
OUT = sys.argv[2]
FRAME_W, FRAME_H = 40, 56
TILE, STRIDE = 16, 18

_cache = {}


def tex(name):
    if name not in _cache:
        _cache[name] = Image.open(f'{ASSETS}/{name}.png').convert('RGBA')
    return _cache[name]


def sub(name, x, y, w, h):
    return tex(name).crop((x, y, x + w, y + h))


def tint(image, color):
    tinted = ImageChops.multiply(image, Image.new('RGBA', image.size, color + (255,)))
    tinted.putalpha(image.getchannel('A'))
    return tinted


def body_cell(name, col, row):
    return sub(name, col * FRAME_W, row * FRAME_H, FRAME_W, FRAME_H)


def column_frame(name, index):
    return sub(name, 0, index * FRAME_H, FRAME_W, FRAME_H)


FACTIONS = [
    dict(shirt=(220, 60, 60), under=(240, 200, 120), pants=(70, 60, 90), hair=(110, 60, 30), hairstyle=1),
    dict(shirt=(70, 120, 230), under=(220, 230, 240), pants=(60, 70, 90), hair=(230, 200, 110), hairstyle=5),
    dict(shirt=(80, 190, 90), under=(240, 230, 190), pants=(110, 80, 50), hair=(60, 40, 30), hairstyle=8),
    dict(shirt=(235, 190, 60), under=(250, 240, 220), pants=(70, 80, 120), hair=(200, 70, 40), hairstyle=14),
]
SKIN = (250, 190, 150)
EYES = (60, 80, 140)
SHOES = (90, 60, 40)


def hero(faction, arm=(2, 0)):
    look = FACTIONS[faction]
    layers = [
        (column_frame('Player_0_10', 0), SKIN),
        (column_frame('Player_0_11', 0), look['pants']),
        (column_frame('Player_0_12', 0), SHOES),
        (body_cell('Player_0_3', 0, 0), SKIN),
        (body_cell('Player_0_4', 0, 0), look['under']),
        (body_cell('Player_0_6', 0, 0), look['shirt']),
        (column_frame('Player_0_0', 0), SKIN),
        (column_frame('Player_0_1', 0), None),
        (column_frame('Player_0_2', 0), EYES),
        (column_frame(f"Player_Hair_{look['hairstyle']}", 0), look['hair']),
        (body_cell('Player_0_7', *arm), SKIN),
        (body_cell('Player_0_8', *arm), look['under']),
        (body_cell('Player_0_13', *arm), look['shirt']),
        (body_cell('Player_0_5', *arm), SKIN),
    ]
    sprite = Image.new('RGBA', (FRAME_W, FRAME_H))
    for image, color in layers:
        sprite.alpha_composite(tint(image, color) if color else image)
    return sprite


def armed(canvas, faction, arm, item, grip, at):
    canvas.alpha_composite(hero(faction, arm), at)
    if item:
        canvas.alpha_composite(tex(item), (at[0] + grip[0], at[1] + grip[1]))


def sky(canvas, gradient_y):
    strip = tex('Background_0').crop((0, gradient_y, 48, gradient_y + 400))
    canvas.alpha_composite(strip.resize(canvas.size))


def scenery(canvas, name, src_x, dest_y):
    image = tex(name)
    image = image.resize((image.width // 2, image.height // 2), Image.NEAREST)
    canvas.alpha_composite(image.crop((src_x, 0, src_x + canvas.width, image.height)), (0, dest_y))


def tile(canvas, name, frame, x, y):
    canvas.alpha_composite(sub(name, frame[0], frame[1], TILE, TILE), (x, y))


def ground(canvas, top):
    for i, x in enumerate(range(-8, canvas.width, TILE)):
        tile(canvas, 'Tiles_2', (STRIDE * (1 + i % 3), 0), x, top)
        for y in range(top + TILE, canvas.height, TILE):
            tile(canvas, 'Tiles_0', (STRIDE * (1 + (i + y // TILE) % 3), STRIDE), x, y)


def block_frame(col, row, cols, rows):
    left, right, top, bottom = col == 0, col == cols - 1, row == 0, row == rows - 1
    variant = (col + row) % 3
    if top and left:
        return 0, 54
    if top and right:
        return 18, 54
    if bottom and left:
        return 0, 72
    if bottom and right:
        return 18, 72
    if top:
        return STRIDE * (1 + variant), 0
    if bottom:
        return STRIDE * (1 + variant), 36
    if left:
        return 0, STRIDE * variant
    if right:
        return 72, STRIDE * variant
    return STRIDE * (1 + variant), STRIDE


def house(canvas, left, top, cols, rows):
    for row in range(1, rows - 1):
        for col in range(1, cols - 1):
            wall = sub('Wall_4', 36 * (1 + (row + col) % 3) + 8, 44, TILE, TILE)
            canvas.alpha_composite(wall, (left + col * TILE, top + row * TILE))
    door_rows = range(rows - 4, rows - 1)
    for row in range(rows):
        for col in range(cols):
            is_edge = row in (0, rows - 1) or col in (0, cols - 1)
            is_doorway = col == cols - 1 and row in door_rows
            if is_edge and not is_doorway:
                tile(canvas, 'Tiles_30', block_frame(col, row, cols, rows), left + col * TILE, top + row * TILE)
    for i, row in enumerate(door_rows):
        tile(canvas, 'Tiles_10', (0, STRIDE * i), left + (cols - 1) * TILE, top + row * TILE)
    canvas.alpha_composite(sub('Tiles_4', 0, 0, 20, 20), (left + 2 * TILE - 2, top + 2 * TILE - 2))
    for i in range(2):
        tile(canvas, 'Tiles_15', (0, STRIDE * i), left + TILE, top + (rows - 3 + i) * TILE)


def king_slime(canvas, x, bottom):
    top = bottom - 120
    canvas.alpha_composite(sub('NPC_50', 0, 0, 174, 120), (x, top))
    canvas.alpha_composite(tex('Extra_39'), (x + 87 - 41, top - 20))


def workshop_icon():
    canvas = Image.new('RGBA', (240, 240))
    sky(canvas, 700)
    canvas.alpha_composite(tex('Sun').resize((57, 57), Image.NEAREST), (10, 8))
    canvas.alpha_composite(tex('Cloud_3'), (60, 30))
    canvas.alpha_composite(tex('Cloud_1').resize((96, 32), Image.NEAREST), (150, 10))
    scenery(canvas, 'Background_7', 60, 46)
    scenery(canvas, 'Background_9', 0, 100)
    ground(canvas, 192)
    house(canvas, -24, 96, 7, 6)
    king_slime(canvas, 132, 190)
    armed(canvas, 2, (3, 0), 'Item_1', (7, -8), (8, 136))
    armed(canvas, 3, (2, 0), None, None, (32, 136))
    armed(canvas, 1, (5, 0), 'Item_39', (20, 19), (58, 136))
    armed(canvas, 0, (4, 0), 'Item_4', (21, -11), (92, 136))
    return canvas.resize((480, 480), Image.NEAREST)


def large_icon():
    canvas = Image.new('RGBA', (80, 80))
    sky(canvas, 900)
    canvas.alpha_composite(tex('Cloud_3').resize((44, 26), Image.NEAREST), (2, 4))
    ground(canvas, 64)
    canvas.alpha_composite(tex('NPC_Head_Boss_7'), (50, 0))
    armed(canvas, 1, (5, 0), 'Item_39', (20, 19), (0, 8))
    armed(canvas, 0, (5, 0), 'Item_4', (24, 3), (30, 8))
    return canvas


def small_icon():
    canvas = Image.new('RGBA', (30, 30))
    sky(canvas, 900)
    canvas.alpha_composite(hero(0).crop((2, 6, 32, 36)))
    return canvas


workshop_icon().save(f'{OUT}/icon_workshop.png')
large_icon().save(f'{OUT}/icon.png')
small_icon().save(f'{OUT}/icon_small.png')

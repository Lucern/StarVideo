# -*- coding: utf-8 -*-
"""
星影视频 —— 演示素材生成器

一次性生成：
  Assets/catalog.json        片库数据（C# 运行时读取，可直接手工编辑加片）
  Assets/Posters/<id>.jpg    竖版海报 400x560
  Assets/Banners/<id>.jpg    横版焦点图 1600x700（仅 banner=True 的条目）
  Assets/Brand/*             应用图标 / Logo

全部为程序化绘制的原创占位素材，不依赖网络、不含第三方版权图片。
重新生成：python Tools/generate_assets.py
"""
from __future__ import annotations

import json
import math
import os
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")

FONT_BOLD = "C:/Windows/Fonts/msyhbd.ttc"
FONT_REG = "C:/Windows/Fonts/msyh.ttc"
FONT_LATIN = "C:/Windows/Fonts/segoeuib.ttf"


def font(path: str, size: int) -> ImageFont.FreeTypeFont:
    try:
        return ImageFont.truetype(path, size, index=0)
    except OSError:
        return ImageFont.truetype(FONT_REG, size, index=0)


def hx(value: str) -> tuple[int, int, int]:
    value = value.lstrip("#")
    return int(value[0:2], 16), int(value[2:4], 16), int(value[4:6], 16)


def mix(a, b, t: float):
    t = max(0.0, min(1.0, t))
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


# id, 片名, 英文名, 分类, 类型, 地区, 年份, 评分, 热度(万), 集数, 时长文案,
# VIP, 独播, 标签, 简介, 主演, 一句话标语, 焦点图, 配色A, 配色B, 构图母题
CATALOG = [
    ("m01", "云上之城", "CITY ABOVE CLOUDS", "电视剧", "都市 剧情", "内地", 2026, 8.9, 982, 32, "32集全", 1, 1, "独播,4K", "一座建在云层之上的新城，把三个家庭的命运重新折叠在一起。当电梯一次次穿过云海，旧日的秘密也随之上浮。", ["林知远", "苏晚", "陈默", "周雨桐"], "有些重逢，注定要在云端发生。", 1, "#20386E", "#0A1024", "city"),
    ("m02", "长街旧事", "THE OLD STREET", "电视剧", "年代 剧情", "内地", 2025, 9.1, 876, 40, "40集全", 0, 1, "口碑,4K", "一条青石板长街，四十年烟火起落。裁缝铺、照相馆、老邮局，记录着普通人最不普通的半生。", ["何绍庭", "沈素芬", "李文栋", "许念"], "人间烟火处，最见岁月长。", 1, "#6A3A1E", "#180C08", "stripes"),
    ("m03", "深海回声", "ECHOES OF THE DEEP", "电视剧", "悬疑 科幻", "内地", 2026, 8.6, 913, 24, "24集全", 1, 0, "4K,杜比", "深海观测站接收到一段来自三十年前的求救信号，而发信人，正是此刻站在站里的自己。", ["江屿", "陆星遥", "白桦", "秦川"], "听见回声的人，也正在被听见。", 1, "#0E4E63", "#04141F", "waves"),
    ("m04", "风起南岸", "WIND FROM THE SOUTH", "电视剧", "商战 剧情", "内地", 2025, 8.3, 654, 36, "36集全", 0, 0, "热播", "改革开放后的南岸码头，一船货物改写两代人的选择，也吹起了一个时代的风。", ["赵其琛", "梁思远", "唐幼宁"], "风起时，没有人能站在原地。", 0, "#5B2130", "#160910", "rings"),
    ("m05", "春日列车", "SPRING TRAIN", "电视剧", "爱情 治愈", "日韩", 2026, 8.7, 731, 16, "16集全", 1, 0, "独播", "每周五傍晚的那班慢车，载着六个人、六段没能说出口的话，和一个终于到来的春天。", ["尹书妍", "徐在赫", "韩宥真"], "下一站，是春天。", 0, "#4C5E2E", "#12180C", "orb"),
    ("m06", "无声之境", "SILENT REALM", "电视剧", "悬疑 犯罪", "内地", 2024, 8.8, 588, 12, "12集全", 0, 1, "口碑", "一座不下雪的北方小城，十二年间失踪的七个人，都曾出现在同一段没有声音的监控里。", ["顾准", "叶知秋", "罗景", "孟凡"], " silence 之后，真相才开口。", 0, "#2B2F4A", "#090A14", "grid"),
    ("m07", "山海之间", "BETWEEN MOUNTAINS", "电视剧", "古装 奇幻", "内地", 2026, 8.5, 1042, 36, "更新至24集", 1, 1, "独播,4K", "山海有界，人心无涯。少年持一卷残图上路，要把散落九州的旧山河一一找回。", ["沈砚", "阿萝", "谢无咎", "青妩"], "山河万里，始于足下。", 1, "#3B3A20", "#100E08", "mountain"),
    ("m08", "第七封信", "THE SEVENTH LETTER", "电视剧", "剧情 家庭", "欧美", 2025, 8.4, 402, 10, "10集全", 0, 0, "获奖", "母亲留下的七封信，前六封写给家人，第七封的收件人，是一个从未被提起的名字。", ["Elena Moore", "David Shaw", "Nora Blake"], "有些话，要寄很多年才到。", 0, "#5A3A52", "#170C17", "rings"),
    ("m09", "极地来信", "LETTERS FROM THE POLE", "电视剧", "剧情 冒险", "欧美", 2024, 8.2, 356, 8, "8集全", 0, 0, "4K", "极夜第 47 天，科考队收到一封来自基地内部的信：请立刻停止向外发送任何消息。", ["Erik Lund", "Mia Frost", "Owen Reed"], "最冷的地方，藏着最烫的秘密。", 0, "#1B4765", "#071422", "stars"),
    ("m10", "星轨之下", "UNDER THE ORBIT", "电视剧", "科幻 青春", "内地", 2026, 8.1, 699, 24, "更新至12集", 1, 0, "热播", "近地轨道的一所中学里，学生们第一次发现：他们课本上的历史，才刚刚开始发生。", ["程野", "夏栀", "顾南星"], "我们都在同一条轨道上。", 0, "#31306E", "#0B0A22", "orb"),

    ("m11", "追光的人", "CHASING LIGHT", "电影", "剧情 励志", "内地", 2026, 9.0, 1120, 1, "128分钟", 1, 1, "独播,4K", "一个小镇放映员，用三十年时间，把露天电影放到了最后一排观众也看得见的地方。", ["周立诚", "桑宁", "杨小满"], "光落在哪里，哪里就是银幕。", 1, "#7A4318", "#1B0E06", "orb"),
    ("m12", "雾中列车", "TRAIN IN THE FOG", "电影", "悬疑 犯罪", "日韩", 2025, 8.7, 623, 1, "116分钟", 0, 0, "口碑", "末班列车驶入浓雾，八名乘客醒来时，少了一个人，多了一张写着自己名字的车票。", ["崔珉豪", "郑由美", "朴成镇"], "别在雾里数人数。", 0, "#2C3A46", "#080E13", "stripes"),
    ("m13", "银河旅店", "GALAXY INN", "电影", "科幻 喜剧", "欧美", 2025, 8.5, 541, 1, "109分钟", 1, 0, "4K", "银河边缘一家常年亏本的旅店，某天同时迎来了三位来自不同时间线的自己。", ["Ava Chen", "Milo Grant", "Iris Vale"], "今晚房客：过去、现在、还有点麻烦的未来。", 0, "#3A2A72", "#0E0922", "stars"),
    ("m14", "夏日回响", "SUMMER ECHO", "电影", "爱情 青春", "中国台湾", 2024, 8.2, 388, 1, "102分钟", 0, 0, "获奖", "1998 年的夏天，一盘被反复录过的磁带，把两个没来得及告别的人留在了同一个下午。", ["林晏如", "陈以恒", "吴沛珊"], "蝉声停了，回声还在。", 0, "#1E5C57", "#071917", "waves"),
    ("m15", "北境之约", "NORTHERN PACT", "电影", "动作 冒险", "欧美", 2026, 8.3, 706, 1, "134分钟", 1, 0, "杜比,4K", "为了兑现二十年前的一个约定，两支队伍在封冻的边境线上，同时出发。", ["Ryan Knox", "Sofia Berg", "Kai Nilsen"], "风雪会覆盖脚印，盖不住约定。", 0, "#294A63", "#08131D", "mountain"),
    ("m16", "纸上山海", "INK MOUNTAINS", "电影", "动画 奇幻", "内地", 2025, 8.9, 814, 1, "98分钟", 0, 1, "国风,4K", "一滴墨落在纸上，长出山川；一个孩子走进画里，替画师补完了没画完的那半个世界。", [], "一笔一画，皆是山河。", 0, "#3D4A32", "#0F140C", "mountain"),
    ("m17", "一分钟永恒", "ONE MINUTE FOREVER", "电影", "爱情 奇幻", "日韩", 2024, 8.0, 337, 1, "112分钟", 0, 0, "口碑", "每天下午 5 点 20 分，城市会多出一分钟。只有两个人记得这一分钟里发生过什么。", ["李瑟菲", "姜道允"], "世界偷走的时间，用来遇见你。", 0, "#6B2F4A", "#1A0A12", "rings"),
    ("m18", "荒原电台", "WASTELAND RADIO", "电影", "剧情 公路", "内地", 2025, 8.6, 465, 1, "121分钟", 0, 0, "获奖", "一台旧收音机、一千公里无人区、一个从不报名字的主持人，陪三个人走完了各自的告别。", ["马向东", "许知意", "格桑"], "频率 87.6，只播给还在路上的人。", 0, "#6A3B1C", "#1A0E07", "stripes"),
    ("m19", "静水之城", "STILL WATER", "电影", "悬疑 惊悚", "中国香港", 2024, 8.1, 359, 1, "105分钟", 0, 0, "口碑", "水库水位逐年下降，露出的旧街区里，有一户人家的灯二十年来一直亮着。", ["梁俊文", "何家欣", "郑Sir"], "水面之下，城市还记得。", 0, "#183F4A", "#061317", "waves"),
    ("m20", "归途二十三", "ROAD 23", "电影", "剧情 家庭", "内地", 2026, 8.4, 512, 1, "118分钟", 1, 0, "4K", "二十三号公路修到家门口那年，父亲第一次坐上女儿开的车，去见一个迟到十年的人。", ["苏建国", "苏禾", "王婶"], "回家的路，总是最不熟的那条。", 0, "#4A3A24", "#120E08", "grid"),

    ("m21", "一起去远方", "GO FAR AWAY", "综艺", "真人秀 旅行", "内地", 2026, 8.8, 903, 12, "更新至8期", 1, 1, "独播", "没有攻略、没有固定路线，五个人一辆车，把目的地交给沿途遇到的人来决定。", ["常驻：林野", "周舟", "阿澈", "唐糖"], "下一站，听路过的人说。", 1, "#1E5A55", "#071A19", "waves"),
    ("m22", "声音的房间", "THE SOUND ROOM", "综艺", "音乐 竞演", "内地", 2026, 9.2, 1186, 10, "更新至6期", 1, 1, "独播,4K", "关掉灯，只留声音。十位歌手在同一个黑暗的房间里，重新认识自己的歌。", ["主持：沈砚", "唱将：白露", "陈屿", "阿依"], "熄灯之后，才听得见。", 1, "#532C6B", "#140B1F", "rings"),
    ("m23", "厨房奇妙夜", "MIDNIGHT KITCHEN", "综艺", "美食 生活", "内地", 2025, 8.5, 547, 12, "12期全", 0, 0, "下饭", "夜里十点开门的厨房，只做客人带来的一道菜，和一个愿意讲出来的故事。", ["主厨：老陆", "苏小满", "大林"], "今晚这道菜，有故事。", 0, "#6B4020", "#1B1007", "orb"),
    ("m24", "脑力俱乐部", "BRAIN CLUB", "综艺", "益智 竞技", "内地", 2025, 8.3, 438, 10, "10期全", 0, 0, "热播", "每期一道没有标准答案的题，八位来自不同行业的解题者，比谁想得更远。", ["主持：许之恒", "观察员：叶蓁"], "答案不止一个。", 0, "#243C69", "#080F1F", "grid"),
    ("m25", "舞台之上", "ON THE STAGE", "综艺", "舞蹈 竞演", "日韩", 2026, 8.6, 621, 8, "更新至4期", 0, 0, "4K", "从练习室到万人舞台，一百天，只为一支不被剪掉的完整作品。", ["导师：Kai", "Yuna", "主演：练习生全员"], "灯光亮起前，先站稳。", 0, "#6A2438", "#1B0910", "stars"),

    ("m26", "星海航行记", "VOYAGE TO STARS", "动漫", "热血 冒险", "日韩", 2026, 9.3, 1290, 48, "更新至36集", 1, 1, "独播,4K", "一艘补给船、半张残缺星图，少年们要把熄灭的航标一盏一盏重新点亮。", ["配音：陆星", "夏禾", "秦风"], "星图会旧，航向不会。", 1, "#22346F", "#080C20", "stars"),
    ("m27", "灵猫事务所", "SPIRIT CAT AGENCY", "动漫", "奇幻 治愈", "内地", 2025, 8.9, 733, 24, "24集全", 0, 1, "国风", "巷子尽头的事务所只接一桩委托：替放不下的人，把没说完的话送到。店长是一只猫。", [], "喵，委托已受理。", 0, "#4A3A63", "#120E1C", "orb"),
    ("m28", "机械之心", "MECHA HEART", "动漫", "科幻 机战", "日韩", 2026, 8.7, 842, 26, "更新至18集", 1, 0, "4K,杜比", "当机甲开始记录驾驶员的梦，战争第一次有了被拒绝的可能。", [], "钢铁也会做梦。", 0, "#2B4260", "#080F18", "grid"),
    ("m29", "山海食记", "TALES OF TASTE", "动漫", "美食 国风", "内地", 2025, 8.8, 566, 20, "20集全", 0, 0, "下饭", "照着一本古食谱走遍山海，每找到一味食材，就找回一段被忘掉的旧事。", [], "山海有味，人间有信。", 0, "#4F5227", "#131509", "mountain"),
    ("m30", "青空下的约定", "PROMISE UNDER SKY", "动漫", "青春 校园", "日韩", 2024, 8.6, 489, 12, "12集全", 0, 0, "治愈", "毕业前最后一个夏天，天文社的三个人决定把没做完的观测记录补完。", [], "那天的云，和约定一样轻。", 0, "#275A77", "#081823", "waves"),

    ("m31", "蓝色星球边缘", "EDGE OF BLUE", "纪录片", "自然", "欧美", 2025, 9.5, 612, 6, "6集全", 0, 0, "4K,杜比", "从海沟到冰盖，六个摄制组用四年时间，拍下那些此前只存在于数据里的生命。", ["旁白：Alan Reeve"], "海的边缘，也是我们的边缘。", 1, "#0E4B68", "#04141F", "waves"),
    ("m32", "我们的城", "OUR CITY", "纪录片", "人文 社会", "内地", 2026, 9.1, 458, 8, "8集全", 0, 1, "独播", "菜市场、修理铺、末班公交、深夜急诊——八座城，一百个普通岗位上的一天。", [], "城市由谁组成？由每一个早起的人。", 0, "#5C4026", "#160F08", "city"),
    ("m33", "时间的味道", "TASTE OF TIME", "纪录片", "美食 文化", "内地", 2025, 9.0, 391, 6, "6集全", 0, 0, "4K", "发酵、腌制、风干、窖藏：六集讲清楚，人类怎样把时间做成一种味道。", [], "有些味道，急不得。", 0, "#5A3A1C", "#170E07", "rings"),
    ("m34", "极境飞行", "FLYING EXTREMES", "纪录片", "自然 航拍", "内地", 2024, 8.9, 347, 5, "5集全", 0, 0, "4K", "跟着候鸟的航线，越过雪线、火山口与无人之境，看一次完整迁徙。", [], "把镜头交给风。", 0, "#254F5E", "#08161C", "mountain"),

    ("m35", "小恐龙阿布", "ABU THE DINO", "少儿", "动画 启蒙", "内地", 2026, 8.8, 428, 52, "52集全", 0, 0, "亲子", "阿布每天发现一个新问题，也每天多认识一个好朋友。", [], "今天又学到了什么？", 0, "#3F6B3A", "#0E1A0C", "orb"),
    ("m36", "星星课堂", "LITTLE STAR CLASS", "少儿", "教育 益智", "内地", 2025, 8.5, 286, 40, "40集全", 0, 0, "亲子", "十分钟的星星课堂，把孩子最爱的「为什么」讲成一个小故事。", [], "每一个为什么，都值得认真回答。", 0, "#3A4C7A", "#0B1020", "stars"),
]

CATEGORY_ORDER = ["电视剧", "电影", "综艺", "动漫", "纪录片", "少儿"]


def build_records():
    records = []
    for row in CATALOG:
        (mid, title, en, category, genre, region, year, score, hot, episodes, duration,
         vip, exclusive, tags, desc, cast, tagline, banner, ca, cb, motif) = row
        records.append({
            "id": mid,
            "title": title,
            "en": en,
            "category": category,
            "genre": genre,
            "region": region,
            "year": year,
            "score": score,
            "hot": hot,
            "episodes": episodes,
            "duration": duration,
            "vip": bool(vip),
            "exclusive": bool(exclusive),
            "tags": tags.split(",") if tags else [],
            "description": desc,
            "cast": cast,
            "tagline": tagline,
            "banner": bool(banner),
            "colorA": ca,
            "colorB": cb,
            "motif": motif,
        })
    return records


def gradient(size, c1, c2):
    """对角渐变底图。"""
    w, h = size
    img = Image.new("RGB", size)
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(1, h - 1)
        # 轻微的水平方向偏移，形成对角感
        d.line((0, y, w, y), fill=mix(c1, c2, t))
    # 斜向高光带
    overlay = Image.new("L", size, 0)
    od = ImageDraw.Draw(overlay)
    od.polygon([(int(w * 0.15), 0), (int(w * 0.62), 0), (int(w * 0.10), h), (-int(w * 0.35), h)], fill=26)
    overlay = overlay.filter(ImageFilter.GaussianBlur(w * 0.09))
    img = ImageChops.screen(img, Image.merge("RGB", (overlay, overlay, overlay)))
    return img


def add_glow(img, color, center, radius, strength=90):
    w, h = img.size
    layer = Image.new("RGB", img.size, (0, 0, 0))
    d = ImageDraw.Draw(layer)
    x, y = center
    d.ellipse((x - radius, y - radius, x + radius, y + radius),
              fill=tuple(int(c * strength / 255) for c in color))
    layer = layer.filter(ImageFilter.GaussianBlur(radius * 0.72))
    return ImageChops.screen(img, layer)


def draw_motif(img, motif: str, seed: int, accent):
    """在半透明图层上绘制装饰构图。"""
    w, h = img.size
    rnd = random.Random(seed * 7919)
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    light = (255, 255, 255, 26)
    strong = (255, 255, 255, 52)

    if motif == "rings":
        cx, cy = int(w * 0.68), int(h * 0.34)
        for i in range(7):
            r = int(min(w, h) * (0.10 + i * 0.075))
            d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=strong if i % 2 == 0 else light, width=2)
    elif motif == "waves":
        for k in range(9):
            pts = []
            base = h * (0.22 + k * 0.062)
            for x in range(0, w + 8, 8):
                pts.append((x, base + math.sin(x / 42.0 + k * 0.55) * (16 + k * 1.6)))
            d.line(pts, fill=light if k % 2 else strong, width=2)
    elif motif == "mountain":
        for k in range(3):
            base = h * (0.46 + k * 0.12)
            peak = h * (0.16 + k * 0.08)
            pts = [(-20, h), (-20, base), (w * 0.30, peak), (w * 0.58, base + 20),
                   (w * 0.82, peak + h * 0.12), (w + 20, base - 10), (w + 20, h)]
            d.polygon(pts, fill=(8, 10, 18, 40 + k * 22))
            d.line(pts[1:-1], fill=strong, width=2)
    elif motif == "grid":
        for i in range(13):
            x = -w * 0.3 + i * (w * 0.14)
            d.line((x, h, w * 0.5 + (x - w * 0.5) * 0.25, h * 0.36), fill=light, width=1)
        for j in range(8):
            y = h * 0.38 + (j ** 1.7) * 8
            d.line((0, y, w, y), fill=light, width=1)
    elif motif == "stripes":
        for i in range(-6, 14):
            x = i * (w * 0.13)
            d.polygon([(x, 0), (x + w * 0.045, 0), (x - w * 0.16, h), (x - w * 0.205, h)],
                      fill=light if i % 2 else strong)
    elif motif == "city":
        for i in range(16):
            bw = rnd.randint(int(w * 0.045), int(w * 0.10))
            x = i * (w / 15) - bw / 2
            top = h * rnd.uniform(0.24, 0.62)
            d.rectangle((x, top, x + bw, h), fill=(10, 14, 28, 70))
            d.rectangle((x, top, x + bw, top + 3), fill=strong)
            for wy in range(int(top) + 12, int(h * 0.72), 14):
                for wx in range(int(x) + 5, int(x + bw) - 4, 10):
                    if rnd.random() > 0.55:
                        d.rectangle((wx, wy, wx + 3, wy + 5), fill=(255, 220, 160, 90))
    elif motif == "stars":
        for _ in range(120):
            x, y = rnd.uniform(0, w), rnd.uniform(0, h * 0.78)
            r = rnd.choice([0.8, 1.0, 1.4, 2.0])
            a = rnd.randint(60, 190)
            d.ellipse((x - r, y - r, x + r, y + r), fill=(255, 255, 255, a))
        d.ellipse((w * 0.48, h * 0.12, w * 0.48 + w * 0.42, h * 0.12 + w * 0.42), outline=strong, width=2)
    else:  # orb
        cx, cy, r = int(w * 0.66), int(h * 0.35), int(w * 0.30)
        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(255, 255, 255, 22))
        d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=strong, width=3)
        d.ellipse((cx - r * 0.68, cy - r * 0.68, cx + r * 0.68, cy + r * 0.68), outline=light, width=2)
        d.arc((cx - r * 1.34, cy - r * 1.34, cx + r * 1.34, cy + r * 1.34), 20, 170, fill=strong, width=2)

    return Image.alpha_composite(img.convert("RGBA"), layer).convert("RGB")


def bottom_shade(img, top: float, alpha: int = 225):
    w, h = img.size
    shade = Image.new("L", (1, h), 0)
    sd = ImageDraw.Draw(shade)
    for y in range(h):
        t = max(0.0, min(1.0, (y / h - top) / (1 - top)))
        sd.point((0, y), fill=int(alpha * (t ** 1.35)))
    shade = shade.resize((w, h))
    black = Image.new("RGB", img.size, (6, 7, 12))
    return Image.composite(black, img, shade)


def add_grain(img, seed: int, amount: int = 7):
    """轻微胶片颗粒，避免大面积纯渐变显得发闷。"""
    rnd = random.Random(seed)
    w, h = img.size
    noise = Image.new("L", (w // 2, h // 2))
    noise.putdata([rnd.randint(128 - amount, 128 + amount) for _ in range((w // 2) * (h // 2))])
    noise = noise.resize((w, h), Image.Resampling.BILINEAR).convert("RGB")
    return Image.blend(img, ImageChops.overlay(img, noise), 0.10)


def wrap_cn(draw, text, fnt, max_width, max_lines=2):
    lines, cur = [], ""
    for ch in text:
        trial = cur + ch
        if draw.textlength(trial, font=fnt) > max_width and cur:
            lines.append(cur)
            cur = ch
            if len(lines) == max_lines:
                break
        else:
            cur = trial
    if len(lines) < max_lines and cur:
        lines.append(cur)
    return lines[:max_lines]


def text_with_shadow(draw, xy, text, fnt, fill=(255, 255, 255), shadow=(0, 0, 0), offset=2, blur_alpha=150):
    x, y = xy
    draw.text((x + offset, y + offset), text, font=fnt, fill=(*shadow, blur_alpha) if isinstance(fill, tuple) and len(fill) == 3 else shadow)
    draw.text((x, y), text, font=fnt, fill=fill)


def make_poster(rec, idx):
    w, h = 400, 560
    c1, c2 = hx(rec["colorA"]), hx(rec["colorB"])
    img = gradient((w, h), c1, c2)
    img = add_glow(img, c1, (int(w * 0.72), int(h * 0.30)), int(w * 0.55), 120)
    img = draw_motif(img, rec["motif"], idx + 1, c1)

    # 顶部微弱彩色条，模拟海报的印刷边缘
    d = ImageDraw.Draw(img, "RGBA")
    d.rectangle((0, 0, w, 5), fill=(*mix(hx(rec["colorA"]), (255, 255, 255), 0.35), 160))

    img = bottom_shade(img, top=0.46, alpha=232)
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    f_title = font(FONT_BOLD, 42)
    f_en = font(FONT_LATIN, 15)
    f_small = font(FONT_REG, 17)
    f_tag = font(FONT_BOLD, 15)

    # 类型标签（左上角）
    label = rec["category"]
    tw = d.textlength(label, font=f_tag)
    d.rounded_rectangle((22, 22, 22 + tw + 26, 22 + 30), radius=6,
                        fill=(255, 122, 24, 235))
    d.text((35, 26), label, font=f_tag, fill=(255, 255, 255, 255))

    # 片名（最多两行）
    lines = wrap_cn(d, rec["title"], f_title, w - 52, 2)
    line_h = 52
    base_y = h - 62 - len(lines) * line_h - 46
    for i, line in enumerate(lines):
        d.text((25 + 2, base_y + i * line_h + 2), line, font=f_title, fill=(0, 0, 0, 170))
        d.text((25, base_y + i * line_h), line, font=f_title, fill=(255, 255, 255, 255))

    en_y = base_y + len(lines) * line_h + 4
    d.text((26, en_y), rec["en"][:34], font=f_en, fill=(255, 255, 255, 165))

    # 底部信息条
    d.line((26, h - 62, w - 26, h - 62), fill=(255, 255, 255, 45), width=1)
    info = f'{rec["year"]}  ·  {rec["genre"].replace(" ", " / ")}'
    d.text((26, h - 50), info, font=f_small, fill=(228, 230, 240, 210))
    sw = d.textlength("SV", font=f_tag)
    d.text((w - 26 - sw, h - 50), "SV", font=f_tag, fill=(255, 150, 60, 210))

    img = Image.alpha_composite(img.convert("RGBA"), layer).convert("RGB")
    img = add_grain(img, idx + 1)
    path = os.path.join(ASSETS, "Posters", rec["id"] + ".jpg")
    img.save(path, quality=90, optimize=True)
    return path


def make_banner(rec, idx):
    w, h = 1600, 700
    c1, c2 = hx(rec["colorA"]), hx(rec["colorB"])
    img = gradient((w, h), c2, c1)
    img = add_glow(img, c1, (int(w * 0.74), int(h * 0.38)), int(w * 0.30), 155)
    img = draw_motif(img, rec["motif"], idx + 101, c1)

    # 左侧压暗（UI 会在左下方叠标题与按钮，这里只做基底）
    mask = Image.new("L", (w, 1), 0)
    md = ImageDraw.Draw(mask)
    for x in range(w):
        md.point((x, 0), fill=int(215 * max(0.0, 1.0 - x / (w * 0.72)) ** 1.4))
    mask = mask.resize((w, h))
    img = Image.composite(Image.new("RGB", img.size, (7, 8, 13)), img, mask)
    img = bottom_shade(img, top=0.48, alpha=185)
    # 只做背景视觉：标题 / 标语 / 按钮全部由 XAML 绘制，
    # 这样窗口任意缩放（Image 使用 UniformToFill 裁剪）文字都不会被切掉。
    img = add_grain(img, idx + 201, 5)
    path = os.path.join(ASSETS, "Banners", rec["id"] + ".jpg")
    img.save(path, quality=90, optimize=True)
    return path


def make_brand():
    brand = os.path.join(ASSETS, "Brand")
    im = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    # 橙色渐变圆角底
    for y in range(256):
        d.line((16, y, 240, y), fill=mix((255, 146, 32), (255, 92, 24), y / 255) + (255,))
    mask = Image.new("L", (256, 256), 0)
    ImageDraw.Draw(mask).rounded_rectangle((8, 8, 248, 248), radius=56, fill=255)
    im.putalpha(mask)
    d = ImageDraw.Draw(im)
    d.polygon([(104, 74), (104, 182), (194, 128)], fill=(255, 255, 255, 255))
    im.save(os.path.join(brand, "logo.png"))
    im.save(os.path.join(brand, "app.ico"),
            sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])


def main():
    for sub in ("Posters", "Banners", "Brand"):
        os.makedirs(os.path.join(ASSETS, sub), exist_ok=True)
    records = build_records()

    with open(os.path.join(ASSETS, "catalog.json"), "w", encoding="utf-8") as f:
        json.dump(records, f, ensure_ascii=False, indent=2)

    for i, rec in enumerate(records):
        make_poster(rec, i)
    for i, rec in enumerate([r for r in records if r["banner"]]):
        make_banner(rec, i)
    make_brand()

    print(f"generated {len(records)} posters, "
          f"{sum(1 for r in records if r['banner'])} banners, catalog.json")


if __name__ == "__main__":
    main()

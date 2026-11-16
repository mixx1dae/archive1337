"""Експерименти зони П2 (Захист + Інтеграція) для звіту до чекпоінта 2.

Відтворює числа зі звіту. Усе, що стосується пакування, рахується дзеркальною Python-реалізацією
тих самих формул, що й SA.Packing (вага = coded_len + 15, W = max − 79 − таблиця файлів, цінність = пріоритет типу).
DEFLATE (zlib) використано ЛИШЕ як замінник кодера в експериментах, бо кодера П1 у цьому дереві ще немає;
у самому архіваторі zlib заборонений.

Потрібен пакет:  pip install cryptography
Запуск:  python tools/p2_experiments.py <каталог_team> [частина] [файл.arc] [пароль]   (останні два лише для tamper)
   <каталог_team> містить corpus/, reference/, team_config.json
   частина: order | pack | ref06 | e2 | tamper | nonce | kdf | abl | leak | all
"""
import csv, hashlib, json, math, os, random, statistics, struct, sys, time, zlib
from collections import Counter, defaultdict
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ref_decoder as rd

OVERHEAD = 41 + 32 + 6


def load(team):
    cfg = json.load(open(os.path.join(team, "team_config.json"), encoding="utf-8"))
    classes = {int(k): v for k, v in cfg["класифікатор"]["класи"].items()}
    prio = {t: cfg["пріоритети_типів"][name] for t, name in classes.items()}
    rows = list(csv.DictReader(open(os.path.join(team, "corpus", "labels.csv"), encoding="utf-8")))
    files = []
    for r in rows:
        data = open(os.path.join(team, "corpus", r["file"]), "rb").read()
        files.append(dict(name=r["file"], type=int(r["type"]), type_name=r["type_name"], data=data))
    return cfg, classes, prio, files


def ctr(key, nonce, data, first=1):
    enc = Cipher(algorithms.AES(key), modes.ECB()).encryptor()
    out = bytearray()
    for i, off in enumerate(range(0, len(data), 16), start=first):
        ks = enc.update(nonce + struct.pack(">I", i))
        out += bytes(a ^ b for a, b in zip(data[off:off + 16], ks))
    return bytes(out)


def entropy(data):
    if not data:
        return 0.0
    c = Counter(data)
    n = len(data)
    return -sum(v / n * math.log2(v / n) for v in c.values())


# ---------------------------------------------------------------- порядок операцій
def exp_order(team):
    cfg, classes, prio, files = load(team)
    salt = bytes(range(16))
    keys = hashlib.pbkdf2_hmac("sha256", b"algo2026", salt, 100000, 64)
    kenc = keys[:32]
    nonce = bytes.fromhex("010203040506070800000000")
    groups = defaultdict(lambda: [0, 0, 0, b""])
    for f in files:
        d = f["data"]
        comp = min(len(zlib.compress(d, 6)), len(d))
        ct = ctr(kenc, nonce, d)
        enc_first = min(len(zlib.compress(ct, 6)), len(ct))
        g = groups[f["type"]]
        g[0] += len(d); g[1] += comp; g[2] += enc_first; g[3] += ct
    print("Порядок операцій (DEFLATE як замінник кодера; сира довжина = байти до шифрування)")
    print("| Група | Вхід, байт | Стиснути→шифрувати | Шифрувати→стиснути | Коеф. 1 | Коеф. 2 | Ентропія шифротексту |")
    print("|---|---:|---:|---:|---:|---:|---:|")
    tot = [0, 0, 0]
    allct = b""
    for t in sorted(groups):
        raw, a, b, ct = groups[t]
        tot[0] += raw; tot[1] += a; tot[2] += b; allct += ct
        print(f"| {t} {classes[t]} | {raw} | {a} | {b} | {raw / a:.2f} | {raw / b:.3f} | {entropy(ct):.4f} |")
    print(f"| **Разом** | **{tot[0]}** | **{tot[1]}** | **{tot[2]}** | **{tot[0] / tot[1]:.2f}** | **{tot[0] / tot[2]:.3f}** | {entropy(allct):.4f} |")
    ref = cfg["довідково"]
    print(f"\nДовідково, опорний кодек викладача (стиснути→шифрувати): {ref['сирий_розмір']} → {ref['розмір_архіву_опорного_пакувальника']}, коефіцієнт {ref['коефіцієнт']}")


# ---------------------------------------------------------------- рюкзак
def dp_solve(items, W):
    n = len(items)
    if n == 0 or W <= 0:
        return []
    best = [0] * (W + 1)
    keep = [bytearray(W + 1) for _ in range(n)]
    for i, (w, v) in enumerate(items):
        if w > W:
            continue
        row = keep[i]
        for c in range(W, w - 1, -1):
            cand = best[c - w] + v
            if cand > best[c]:
                best[c] = cand
                row[c] = 1
    ch, c = [], W
    for i in range(n - 1, -1, -1):
        if keep[i][c]:
            ch.append(i)
            c -= items[i][0]
    return sorted(ch)


def greedy_solve(items, W):
    order = sorted(range(len(items)), key=lambda i: (-items[i][1] / items[i][0], i))
    rem, ch = W, []
    for i in order:
        if items[i][0] <= rem:
            ch.append(i)
            rem -= items[i][0]
    return sorted(ch)


def seq_solve(items, W):
    rem, ch = W, []
    for i, (w, _) in enumerate(items):
        if w <= rem:
            ch.append(i)
            rem -= w
    return ch


def table_size(names):
    return sum(12 + len(n.encode()) for n in names)


def pack(blocks, maxv, solver, empty_names=()):
    """blocks: список (file, type, seq, coded, value). Повертає список томів і час."""
    remaining = list(range(len(blocks)))
    volumes = []
    t0 = time.perf_counter()
    first = True
    while True:
        names = sorted({blocks[i][0] for i in remaining} | (set(empty_names) if first else set()))
        W = maxv - OVERHEAD - table_size(names)
        if W < 0:
            raise ValueError("таблиця не вміщується")
        items = [(blocks[i][3] + 15, blocks[i][4]) for i in remaining]
        ch = solver(items, W) if remaining else []
        if remaining and not ch:
            raise ValueError("жоден блок не вміщується")
        chosen = [remaining[k] for k in ch]
        size = 41 + 6 + table_size(names) + sum(15 + blocks[i][3] for i in chosen) + 32
        volumes.append(dict(blocks=chosen, names=names, size=size, value=sum(blocks[i][4] for i in chosen), W=W,
                            weight=sum(blocks[i][3] + 15 for i in chosen)))
        s = set(chosen)
        remaining = [i for i in remaining if i not in s]
        first = False
        if not remaining:
            break
    return volumes, time.perf_counter() - t0


def make_blocks(files, prio, block_size, proxy):
    blocks, empties = [], []
    for f in files:
        d = f["data"]
        if not d:
            blocks.append((f["name"], f["type"], 0, 0, prio[f["type"]]))
            continue
        for seq, off in enumerate(range(0, len(d), block_size)):
            chunk = d[off:off + block_size]
            coded = min(len(zlib.compress(chunk, 6)), len(chunk)) if proxy else len(chunk)
            blocks.append((f["name"], f["type"], seq, coded, prio[f["type"]]))
    return blocks, empties


def exp_pack(team):
    cfg, classes, prio, files = load(team)
    raw = sum(len(f["data"]) for f in files)
    bs = cfg["пакування"]["block_size"]
    print(f"Корпус: {len(files)} файлів, {raw} байтів; block_size = {bs}\n")
    print("| Розміри блоків | max_volume | Метод | Томів | B_arc, байт | S | Цінність 1-го тому | Вага 1-го тому | мс |")
    print("|---|---:|---|---:|---:|---:|---:|---:|---:|")
    for proxy, label in ((False, "без стиснення"), (True, "DEFLATE-замінник")):
        blocks, _ = make_blocks(files, prio, bs, proxy)
        for maxv in (cfg["пакування"]["max_volume_bytes"], 32768):
            for name, solver in (("ДП", dp_solve), ("жадібний", greedy_solve), ("послідовно", seq_solve)):
                vols, t = pack(blocks, maxv, solver)
                b_arc = sum(v["size"] for v in vols)
                print(f"| {label} | {maxv} | {name} | {len(vols)} | {b_arc} | {raw / (raw + b_arc):.4f} | {vols[0]['value']} | {vols[0]['weight']} | {t * 1000:.0f} |")
    # випадкова перевірка кейсу 10
    rng = random.Random(2026)
    strict, worse, total = 0, 0, 3000
    for _ in range(total):
        n = rng.randint(1, 25)
        W = rng.randint(50, 600)
        items = [(rng.randint(15, 200), rng.randint(1, 3)) for _ in range(n)]
        d = sum(items[i][1] for i in dp_solve(items, W))
        g = sum(items[i][1] for i in greedy_solve(items, W))
        worse += d < g
        strict += d > g
    print(f"\nКейс 10 (випадкові набори): {total} наборів, ДП гірший за жадібний: {worse}, ДП строго кращий: {strict}")


# ---------------------------------------------------------------- ref_06 (реальні розміри від кодека викладача)
def exp_ref06(team):
    cfg, classes, prio, files = load(team)
    R = os.path.join(team, "reference")
    vols = [open(os.path.join(R, f"ref_06_two_volumes.part{i}.arc"), "rb").read() for i in range(3)]
    parsed = [rd.parse_volume(v, "algo2026") for v in vols]
    blocks = []
    for p in parsed:
        for b in p["blocks"]:
            f = p["files"][b["file"]]
            blocks.append((f["name"], f["type"], b["seq"], b["coded"], prio[f["type"]]))
    maxv = 32768
    print(f"ref_06: {len(blocks)} блоків від опорного кодека, max_volume = {maxv}; архів викладача: томів 3, {sum(map(len, vols))} байтів\n")
    print("| Метод | Томів | B_arc | Цінність по томах | Вага по томах |")
    print("|---|---:|---:|---|---|")
    for name, solver in (("ДП", dp_solve), ("жадібний", greedy_solve), ("послідовно", seq_solve)):
        v, _ = pack(blocks, maxv, solver)
        print(f"| {name} | {len(v)} | {sum(x['size'] for x in v)} | {[x['value'] for x in v]} | {[x['weight'] for x in v]} |")
    print(f"| опорний пакувальник | 3 | {sum(map(len, vols))} | {[sum(prio[p['files'][b['file']]['type']] for b in p['blocks']) for p in parsed]} | {[sum(b['coded'] + 15 for b in p['blocks']) for p in parsed]} |")


# ---------------------------------------------------------------- E2: том менший у 10 разів
def exp_e2(team):
    cfg, classes, prio, files = load(team)
    base = cfg["пакування"]["max_volume_bytes"]
    small = base // 10
    raw = sum(len(f["data"]) for f in files)
    print(f"Том {base} → {small} байтів (÷10). Корпус {raw} байтів, без стиснення.\n")
    print("| block_size | Томів | B_arc | Службові байти | Частка службових | Результат |")
    print("|---:|---:|---:|---:|---:|---|")
    for bs in (cfg["пакування"]["block_size"], 4096, 2048, 1024, 512):
        blocks, _ = make_blocks(files, prio, bs, False)
        try:
            vols, _ = pack(blocks, small, dp_solve)
        except ValueError as e:
            print(f"| {bs} | - | - | - | - | {e} |")
            continue
        b_arc = sum(v["size"] for v in vols)
        over = b_arc - raw
        print(f"| {bs} | {len(vols)} | {b_arc} | {over} | {over / b_arc * 100:.1f} % | ok |")


# ---------------------------------------------------------------- модифікація одного байта
def exp_tamper(team, path=None, password="algo2026"):
    """Змінює по одному байту томy. path: власний .arc (наприклад, архів іншої команди); за замовчуванням ref_01."""
    path = path or os.path.join(team, "reference", "ref_01_single_file.arc")
    v = open(path, "rb").read()
    fields = [("magic", 0, 4), ("version", 4, 5), ("volume_index+count", 5, 9), ("salt", 9, 25), ("nonce", 25, 37),
              ("ciphertext_len", 37, 41), ("ciphertext", 41, len(v) - 32), ("HMAC-тег", len(v) - 32, len(v))]
    print(f"{os.path.basename(path)}: {len(v)} байтів; кожен байт по черзі змінюється (xor 0x01). Пароль вірний.\n")
    print("| Поле | Байтів | Коди відмови | Прийнято |")
    print("|---|---:|---|---:|")
    accepted_total = 0
    for name, a, b in fields:
        codes, acc = Counter(), 0
        for i in range(a, b):
            m = bytearray(v); m[i] ^= 1
            try:
                rd.parse_volume(bytes(m), password); acc += 1
            except rd.Err as e:
                codes[e.code] += 1
        accepted_total += acc
        print(f"| {name} | {b - a} | {dict(codes)} | {acc} |")
    print(f"\nУсього змін: {len(v)}, прийнято: {accepted_total}")
    # обрізання і доповнення
    res = Counter()
    for cut in range(0, len(v)):
        try:
            rd.parse_volume(v[:cut], password); res["прийнято"] += 1
        except rd.Err as e:
            res[e.code] += 1
    print(f"Обрізання до 0…{len(v) - 1} байтів: {dict(res)}")
    try:
        rd.parse_volume(v + b"\x00", password); print("Додатковий байт: прийнято")
    except rd.Err as e:
        print("Додатковий байт:", e.code)
    try:
        rd.parse_volume(v, "wrong"); print("Невірний пароль: прийнято")
    except rd.Err as e:
        print("Невірний пароль:", e.code)


# ---------------------------------------------------------------- повторне використання nonce
def exp_nonce():
    key = hashlib.pbkdf2_hmac("sha256", b"pw", bytes(16), 1000, 32)
    nonce = bytes(12)
    p1 = b"Attack at dawn! Transfer 1000 to Bob."
    p2 = b"Retreat now!!! Transfer 9999 to Eve."
    n = min(len(p1), len(p2))
    p1, p2 = p1[:n], p2[:n]
    c1, c2 = ctr(key, nonce, p1), ctr(key, nonce, p2)
    xor_c = bytes(a ^ b for a, b in zip(c1, c2))
    xor_p = bytes(a ^ b for a, b in zip(p1, p2))
    print("Той самий ключ і nonce для двох повідомлень:")
    print("  c1 XOR c2 == p1 XOR p2 :", xor_c == xor_p)
    recovered = bytes(a ^ b for a, b in zip(xor_c, p1))
    print("  знаючи p1, відновлюємо p2 без ключа:", recovered == p2, recovered)


# ---------------------------------------------------------------- KDF
def exp_kdf(team):
    cfg, classes, prio, files = load(team)
    ts = []
    for _ in range(7):
        t0 = time.perf_counter(); hashlib.pbkdf2_hmac("sha256", b"algo2026", bytes(16), 100000, 64); ts.append(time.perf_counter() - t0)
    med = statistics.median(ts)
    blocks, _ = make_blocks(files, prio, cfg["пакування"]["block_size"], False)
    vols, _ = pack(blocks, cfg["пакування"]["max_volume_bytes"], dp_solve)
    print(f"PBKDF2-HMAC-SHA256, 100 000 ітерацій: медіана {med * 1000:.0f} мс (мін {min(ts) * 1000:.0f}, макс {max(ts) * 1000:.0f}), 7 прогонів")
    print(f"Корпус без стиснення = {len(vols)} томів: один раз на архів {med * 1000:.0f} мс; для кожного тому {med * len(vols) * 1000:.0f} мс")


# ---------------------------------------------------------------- вплив блоків (за розміром; Q потребує часу з C#)
def exp_abl(team):
    cfg, classes, prio, files = load(team)
    raw = sum(len(f["data"]) for f in files)
    bs, maxv = cfg["пакування"]["block_size"], cfg["пакування"]["max_volume_bytes"]
    flat = {t: prio[0] for t in prio}
    variants = [
        ("повна система (ДП, DEFLATE-замінник, типи)", True, dp_solve, prio),
        ("без стиснення (NO_COMPRESSION)", False, dp_solve, prio),
        ("жадібний замість ДП", True, greedy_solve, prio),
        ("без рюкзака (послідовне пакування)", True, seq_solve, prio),
        ("без класифікатора (один тип для всіх)", True, dp_solve, flat),
    ]
    print(f"Корпус {raw} байтів, max_volume = {maxv}, block_size = {bs}; S = B_orig/(B_orig + B_arc); Q потребує часу з C#\n")
    print("| Конфігурація | Томів | B_arc | S | ΔS | Цінність 1-го тому (за справжніми пріоритетами) |")
    print("|---|---:|---:|---:|---:|---:|")
    base_s = None
    for name, proxy, solver, pr in variants:
        blocks, _ = make_blocks(files, pr, bs, proxy)
        vols, _ = pack(blocks, maxv, solver)
        b_arc = sum(v["size"] for v in vols)
        sv = raw / (raw + b_arc)
        base_s = sv if base_s is None else base_s
        real_blocks, _ = make_blocks(files, prio, bs, proxy)
        first_val = sum(real_blocks[i][4] for i in [
            next(j for j, b in enumerate(blocks) if (b[0], b[2]) == (blocks[i][0], blocks[i][2])) for i in vols[0]["blocks"]])
        print(f"| {name} | {len(vols)} | {b_arc} | {sv:.4f} | {sv - base_s:+.4f} | {first_val} |")


# ---------------------------------------------------------------- витік метаданих і вартість вирівнювання
def exp_leak(team):
    cfg, classes, prio, files = load(team)
    raw = sum(len(f["data"]) for f in files)
    bs = cfg["пакування"]["block_size"]
    blocks, _ = make_blocks(files, prio, bs, True)
    print(f"Корпус {raw} байтів, DEFLATE-замінник, ДП, block_size = {bs}\n")
    print("| max_volume | Томів | B_arc | Відкрито спостерігачу в нашому форматі | Було б відкрито з відкритою таблицею | Вирівнювання всіх томів до max | Вирівнювання лише останнього |")
    print("|---:|---:|---:|---|---:|---:|---:|")
    for maxv in (cfg["пакування"]["max_volume_bytes"], 32768):
        vols, _ = pack(blocks, maxv, dp_solve)
        b_arc = sum(v["size"] for v in vols)
        table_bytes = sum(table_size(v["names"]) + 15 * len(v["blocks"]) for v in vols)
        pad_all = len(vols) * maxv - b_arc
        pad_last = maxv - vols[-1]["size"]
        sizes = [v["size"] - 73 for v in vols]
        print(f"| {maxv} | {len(vols)} | {b_arc} | 41 байт заголовка на том, ciphertext_len = {sizes} | {table_bytes} байтів ({table_bytes / b_arc * 100:.1f} %) | {pad_all} байтів = {pad_all * 8} біт ({pad_all / b_arc * 100:.1f} %) | {pad_last} байтів = {pad_last * 8} біт ({pad_last / b_arc * 100:.1f} %) |")
    names = sum(len(f["name"].encode()) for f in files)
    print(f"\nУ відкритій таблиці були б видні: {len(files)} імен ({names} байтів), розміри, CRC32, типи файлів і розбиття на блоки.")


if __name__ == "__main__":
    team = sys.argv[1]
    part = sys.argv[2] if len(sys.argv) > 2 else "all"
    extra_path = sys.argv[3] if len(sys.argv) > 3 else None
    extra_pw = sys.argv[4] if len(sys.argv) > 4 else "algo2026"
    steps = dict(order=lambda: exp_order(team), pack=lambda: exp_pack(team), ref06=lambda: exp_ref06(team),
                 e2=lambda: exp_e2(team), tamper=lambda: exp_tamper(team, extra_path, extra_pw), nonce=exp_nonce, kdf=lambda: exp_kdf(team),
                 abl=lambda: exp_abl(team), leak=lambda: exp_leak(team))
    for name, fn in steps.items():
        if part in ("all", name):
            print(f"\n===== {name} =====")
            fn()

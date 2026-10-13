"""Oyunun kart veri tabanını (def_gwint_cards_final.xml) okur; cards.json ile karşılaştırır. Kişisel kullanım.

  python tools/w3cards_xml.py dump        -> tüm kartları tablo olarak yazar
  python tools/w3cards_xml.py compare     -> cards.json ile farkları yazar
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XML = os.path.join(ROOT, "assets", "extracted", "raw", "gameplay", "items", "def_gwint_cards_final.xml")
DECKS = os.path.join(ROOT, "assets", "extracted", "raw", "gameplay", "items", "def_gwint_decks.xml")

FACTION = {"F_NEUTRAL": "neutral", "F_NORTHERN_KINGDOMS": "northern_realms", "F_NILFGAARD": "nilfgaard",
           "F_SCOIATAEL": "scoiatael", "F_NO_MANS_LAND": "monsters", "F_SKELIGE": "skellige", "F_SKELLIGE": "skellige"}


def load_cards():
    text = open(XML, encoding="utf-8-sig").read()
    root = ET.fromstring(text)
    cards = []
    for c in root.iter("card"):
        a = c.attrib
        flags = {}
        for grp in c:
            flags[grp.tag] = [f.get("name") for f in grp.iter("flag")]
        cards.append({
            "index": int(a.get("index", -1)), "name": a.get("name", ""), "title": a.get("title", ""),
            "power": int(a.get("power", 0)), "picture": a.get("picture", ""),
            "faction": a.get("faction_index", ""), "flags": flags,
        })
    return cards


def short(card):
    f = card["flags"]
    return ",".join(x.replace("TYPE_", "").replace("EFFECT_", "") for x in f.get("type_flags", []) + f.get("effect_flags", []))


FACT = {"NEUTRAL": "neutral", "NORTHERN_KINGDOM": "northern_realms", "NILFGAARD": "nilfgaard",
        "SCOIATAEL": "scoiatael", "NO_MANS_LAND": "monsters", "SKELLIGE": "skellige"}
EFFECT_AB = {"NURSE": "medic", "SPY": "spy", "SUMMON_CLONES": "muster", "SAME_MORALE": "bond", "IMPROVE_NEIGHBOURS": "morale",
             "HORN": "horn", "CP_MELEE_SCORCH": "scorch_c", "CP_SIEGE_SCORCH": "scorch_s", "RANGED_SCORCH": "scorch_r",
             "SCORCH": "scorch", "MUSHROOM": "mardroeme", "MORPH": "berserker", "SUICIDE_SUMMON": "avenger"}


def norm(card):
    f = card["flags"]
    allf = [x.replace("TYPE_", "").replace("EFFECT_", "") for g in f.values() for x in g]
    types = set(x.replace("TYPE_", "") for x in f.get("type_flags", []))
    hero = "HERO" in allf
    m, r, sg = "MELEE" in types, "RANGED" in types, "SIEGE" in types
    row = "agile" if m and r else "close" if m else "ranged" if r else "siege" if sg else ""
    ab = sorted(set(EFFECT_AB[e] for e in allf if e in EFFECT_AB))
    return {"power": card["power"], "row": row, "hero": hero, "abilities": ab, "creature": "CREATURE" in allf,
            "faction": FACT.get(card["faction"][2:], card["faction"])}


def compare():
    import difflib
    ours = json.load(open(os.path.join(ROOT, "data", "cards.json"), encoding="utf-8"))
    units = [c for c in ours if c["type"] == "unit"]
    xml = load_cards()
    by_pic = {}
    for c in xml:
        by_pic.setdefault(c["picture"], []).append(c)
    mapping, problems, used = {}, [], set()
    for pic, group in by_pic.items():
        c = group[0]
        n = norm(c)
        if not n["creature"]:
            continue
        stem = pic.split("_", 1)[-1] + " " + c["name"]
        cands = [u for u in units if u["faction"] == n["faction"] or u["faction"] == "neutral" or n["faction"] == "neutral"]

        def exact(u):
            return (u["strength"] == n["power"] and u["row"] == n["row"] and bool(u["hero"]) == n["hero"]
                    and sorted(u["abilities"]) == n["abilities"])

        def ratio(u):
            return max(difflib.SequenceMatcher(None, stem.lower(), u["id"]).ratio(), difflib.SequenceMatcher(None, c["name"].lower(), u["id"]).ratio())

        pool = [u for u in cands if exact(u)] or cands
        pool = [u for u in pool if u["id"] not in used] or pool
        best = max(pool, key=lambda u: (exact(u), ratio(u)))
        mapping[pic] = best["id"]
        used.add(best["id"])
        diffs = []
        if best["strength"] != n["power"]: diffs.append(f"güç {best['strength']}≠{n['power']}")
        if best["row"] != n["row"]: diffs.append(f"sıra {best['row']}≠{n['row']}")
        if bool(best["hero"]) != n["hero"]: diffs.append(f"kahraman {best['hero']}≠{n['hero']}")
        if sorted(best["abilities"]) != n["abilities"]: diffs.append(f"yetenek {sorted(best['abilities'])}≠{n['abilities']}")
        if len(group) > 1 and best["copies"] != len(group): diffs.append(f"kopya bizde {best['copies']}, oyunda {len(group)} kayıt")
        if diffs:
            problems.append(f"FARK {pic:34s} → {best['id']:34s} " + "; ".join(diffs))
    print("eşlenen resim:", len(mapping))
    for p in problems:
        print(p)
    json.dump(mapping, open(os.path.join(ROOT, "assets", "extracted", "picture_map.json"), "w"), indent=1)
    mapped = set(mapping.values())
    print("oyunda karşılığı bulunmayan (ya da jeton) bizim birimler:", [u["id"] for u in units if u["id"] not in mapped])


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "dump"
    cards = load_cards()
    if cmd == "compare":
        compare()
        sys.exit(0)
    if cmd == "dump":
        print(len(cards), "kart")
        for c in cards:
            print(f"{c['index']:4d} {c['faction'][2:]:18s} {c['power']:3d} {c['picture']:34s} {c['name']:28s} {short(c)}")

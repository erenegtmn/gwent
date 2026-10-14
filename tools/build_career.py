"""Oyunun NPC Gwent destelerini (def_gwint_decks.xml) bizim kart kimliklerimize çevirir: data/career_decks.json.

Her deste koleksiyonu (Baron, Zoltan, Roche...) zorluk 1-3 için lider + kart listesi ve oyunun "specialCard" alanını
(rakibin destesine eklenen/ödül kartı) taşır. Kart adları oyunun iç adlarıdır; resim adı üzerinden picture_map.json ile eşlenir.

  python tools/build_career.py            -> data/career_decks.json yazar ve eşlenemeyenleri listeler
"""
import json
import os
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ITEMS = os.path.join(ROOT, "assets", "extracted", "raw", "gameplay", "items")
OUT = os.path.join(ROOT, "data", "career_decks.json")


# Özel kartlar ve liderler resim eşlemesinde yok: oyunun iç adı -> bizim kimlik.
# Liderler oyundaki yetenek bayrağına göre eşlenir (ör. foltest_copper = CP_PICK_FOG_CARD = King of Temeria).
FIXED = {
    "frost": "biting_frost", "fog": "impenetrable_fog", "rain": "torrential_rain", "clear_sky": "clear_weather",
    "dummy": "decoy", "horn": "commanders_horn", "scorch": "scorch", "gwint_card_scorch": "scorch",
    "mushroom": "mardroeme", "skellige_storm": "skellige_storm", "gwint_card_siegfried": "siegfried_of_denesle",
    "geraltgog": "geralt_of_rivia", "cirigog": "cirilla_fiona_elen_riannon",
    "foltest_copper": "foltest_king_of_temeria", "foltest_bronze": "foltest_lord_commander_of_the_north",
    "foltest_silver": "foltest_the_siegemaster", "foltest_gold": "foltest_the_steel_forged", "foltest_platinium": "foltest_son_of_medell",
    "emhyr_copper": "emhyr_var_emreis_his_imperial_majesty", "emhyr_bronze": "emhyr_var_emreis_emperor_of_nilfgaard",
    "emhyr_silver": "emhyr_var_emreis_the_white_flame", "emhyr_gold": "emhyr_var_emreis_the_relentless",
    "emhyr_platinium": "emhyr_var_emreis_invader_of_the_north",
    "francesca_copper": "francesca_findabair_pureblood_elf", "francesca_bronze": "francesca_findabair_daisy_of_the_valley",
    "francesca_silver": "francesca_findabair_the_beautiful", "francesca_gold": "francesca_findabair_queen_of_dol_blathanna",
    "francesca_platinium": "francesca_findabair_hope_of_the_aen_seidhe",
    "eredin_bronze": "eredin_king_of_the_wild_hunt", "eredin_copper": "eredin_commander_of_the_red_riders",
    "eredin_gold": "eredin_destroyer_of_worlds", "eredin_silver": "eredin_bringer_of_death",
    "eredin_platinium": "eredin_breacc_glas_the_treacherous",
    "king_bran": "king_bran", "Crach_an_Craite": "crach_an_craite",
    "king_bran_bronze": "king_bran", "king_bran_copper": "crach_an_craite",
}


def load_xml(name):
    return ET.fromstring(open(os.path.join(ITEMS, name), encoding="utf-8-sig").read())


def main():
    pic_map = json.load(open(os.path.join(ROOT, "assets", "extracted", "picture_map.json"), encoding="utf-8"))
    name_to_pic = {}
    for f in ("def_gwint_cards_final.xml", "def_gwint_battle_king_cards.xml"):
        for c in load_xml(f).iter("card"):
            n = c.get("name")
            if n:
                name_to_pic[n] = c.get("picture", "")
    ours = {c["id"]: c for c in json.load(open(os.path.join(ROOT, "data", "cards.json"), encoding="utf-8"))}

    missing = set()

    def our_id(game_name):
        if game_name in FIXED:
            return FIXED[game_name]
        pic = name_to_pic.get(game_name)
        cid = pic_map.get(pic) if pic else None
        if cid is None or cid not in ours:
            missing.add(f"{game_name} (resim {pic})")
            return None
        return cid

    out = {}
    for coll in load_xml("def_gwint_decks.xml").iter("deck_collection"):
        name = coll.get("name")
        decks = {}
        for d in coll.iter("deck"):
            diff = d.get("difficulty")
            leader = our_id(d.get("leaderCard", ""))
            cards = [our_id(c.get("name")) for c in d.iter("card")]
            special = d.get("specialCard") or None
            decks[diff] = {
                "leader": leader,
                "cards": [c for c in cards if c],
                "special": our_id(special) if special else None,
            }
        out[name] = decks

    # Tüccar fiyatları (def_item_gwint.xml: gwint_card_<iç ad> price="..."); aynı karta birden çok ad düşerse en ucuzu
    prices = {}
    for it in load_xml("def_item_gwint.xml").iter("item"):
        n = it.get("name", "")
        if not n.startswith("gwint_card_") or not it.get("price"):
            continue
        cid = our_id(n[len("gwint_card_"):])
        if cid:
            p = int(it.get("price"))
            prices[cid] = min(p, prices.get(cid, p))

    json.dump({"decks": out, "prices": dict(sorted(prices.items()))}, open(OUT, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    print(f"{len(prices)} kart fiyatı")
    print(f"{len(out)} koleksiyon yazıldı -> {OUT}")
    if missing:
        print("Eşlenemeyen:", ", ".join(sorted(missing)))
    for k, v in out.items():
        d = v.get("2") or next(iter(v.values()))
        print(f"  {k:20s} lider={d['leader']} kart={len(d['cards'])} özel={d['special']}")


if __name__ == "__main__":
    sys.exit(main())

"""Kart verisini ham tablodan data/cards.json'a derler.

Ham tablo: bagimsiz bir Witcher 3 yeniden yapiminin (asundr/gwent-classic) kart listesinden
alinmis OLGULAR (isim/guc/sira/yetenek); kod kopyalanmadi. Adlar ve kart kumesi ayrica
Fextralife + PocketTactics + game-checklists listeleriyle karsilastirildi.
Kullanim: python tools/build_cards.py <cards_table.txt> <cikti.json>
"""
import json, re, sys, unicodedata
from collections import OrderedDict

FACTION = {"realms": "northern_realms", "nilfgaard": "nilfgaard", "monsters": "monsters",
           "scoiatael": "scoiatael", "skellige": "skellige", "neutral": "neutral",
           "special": "neutral", "weather": "neutral"}
NAMEFIX = {"S�le de Tansarville": "Síle de Tansarville", "Eithn�": "Eithné",
           "Eredin Br�acc Glas - The Treacherous": "Eredin Bréacc Glas: The Treacherous",
           "Clan Brokva Archer": "Clan Brokvar Archer", "Arachas- Behemoth": "Arachas Behemoth",
           "Mysterious Elf": "Avallac'h", "Cirilla Fiona Elen Riannon": "Cirilla Fiona Elen Riannon",
           "Filavandrel aen Fidhail": "Filavandrel aen Fidhail"}
HOS = {"Toad", "Schirru", "Cow", "Gaunter O'Dimm", "Gaunter O'Dimm: Darkness", "Olgierd von Everec",
       "Eredin Bréacc Glas: The Treacherous", "Francesca Findabair: Hope of the Aen Seidhe",
       "Foltest: Son of Medell", "Emhyr var Emreis: Invader of the North", "Bovine Defense Force"}
NEXTGEN = {"Roach"}
TOKENS = {"Bovine Defense Force", "Hemdall", "Transformed Vildkaarl", "Transformed Young Vildkaarl"}
MUSTER_GROUP = {"Cerys": "Cerys", "Geralt of Rivia": "Roach", "Cirilla Fiona Elen Riannon": "Roach"}


def pretty(n):
    n = n.strip()
    n = NAMEFIX.get(n, n)
    n = n.replace("Eredin - ", "Eredin: ").replace("Foltest - ", "Foltest: ")
    n = n.replace("Emhyr var Emreis - the ", "Emhyr var Emreis: The ").replace("Emhyr var Emreis - ", "Emhyr var Emreis: ")
    n = n.replace("Francesca Findabair - the ", "Francesca Findabair: The ").replace("Francesca Findabair - ", "Francesca Findabair: ")
    n = n.replace("Crone - ", "Crone ").replace("Vampire - ", "Vampire ")
    n = n.replace("Gaunter O'Dimm - Darkness", "Gaunter O'Dimm: Darkness")
    n = n.replace("Cerys - Clan", "Clan")
    return n


def slug(n):
    n = unicodedata.normalize("NFKD", n).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", "_", n.lower().replace("'", "")).strip("_")


rows = [l.rstrip("\n").split(" | ") for l in open(sys.argv[1], encoding="utf8") if l.strip()]
cards = OrderedDict()
for deck, name, row, strength, ability, count, muster in rows:
    raw = name.strip()
    shield = raw.startswith("Cerys - ")
    name = pretty(name)
    abil = [a for a in ability.split() if a != "hero"]
    abil = ["avenger" if a == "avenger_kambi" else a for a in abil]
    hero = "hero" in ability.split()
    if deck == "special" or deck == "weather":
        kind = "weather" if deck == "weather" else "special"
    elif row == "leader":
        kind = "leader"
    else:
        kind = "unit"
    faction = "skellige" if name == "Mardroeme" else FACTION[deck]
    group = None
    if "muster" in abil:
        group = muster or MUSTER_GROUP.get(name) or re.split(r" (?=Behemoth|Brewess|Weavess|Whispess|Bruxa|Ekimmara|Fleder|Garkain|Katakan)", name)[0]
        if shield: group = "Cerys"
        if name.startswith("Gaunter"): group = "Gaunter O'Dimm"
    if shield: group = group or "Cerys"
    key = (kind, faction, name)
    if key in cards:
        cards[key]["copies"] += 1
        continue
    edition = "nextgen" if name in NEXTGEN else "hos" if name in HOS else \
        "bw" if (faction == "skellige" or name == "Skellige Storm") else "base"
    c = OrderedDict(id=slug(name), name=name, faction=faction, type=kind, edition=edition)
    if kind == "unit":
        c.update(row=row, strength=int(strength or 0), hero=hero, abilities=abil)
        if group: c["musterGroup"] = group
        if name in TOKENS: c["token"] = True
    elif kind == "leader":
        c.update(abilities=abil)
    else:
        c.update(abilities=abil)
    c["copies"] = int(count) if count.isdigit() and int(count) > 0 else (0 if kind != "leader" else 1)
    if c["copies"] == 0 and "token" not in c and kind == "unit": c["token"] = True
    if kind == "leader" and c["copies"] == 0: c["copies"] = 1
    cards[key] = c
# 'count' ham alani yalniz bazi kartlarda dolu; kopyalari birlestirirken toplam = max(sayac, satir)
LEADER_EFFECT = {
    "foltest_king_of_temeria": "Pick an Impenetrable Fog card from your deck and play it instantly.",
    "foltest_lord_commander_of_the_north": "Clear any weather effects in play.",
    "foltest_the_siegemaster": "Doubles the strength of all your Siege units (unless a Commander's Horn is on that row).",
    "foltest_the_steel_forged": "Destroy your enemy's strongest Siege unit(s) if the combined strength of his or her Siege units is 10 or more.",
    "foltest_son_of_medell": "Destroy your enemy's strongest Ranged unit(s) if the combined strength of his or her Ranged units is 10 or more.",
    "emhyr_var_emreis_his_imperial_majesty": "Pick a Torrential Rain card from your deck and play it instantly.",
    "emhyr_var_emreis_emperor_of_nilfgaard": "Look at 3 random cards from your opponent's hand.",
    "emhyr_var_emreis_the_white_flame": "Cancel your opponent's Leader Ability.",
    "emhyr_var_emreis_the_relentless": "Draw a card from your opponent's discard pile.",
    "emhyr_var_emreis_invader_of_the_north": "Abilities that restore a unit to the battlefield restore a randomly-chosen unit (both players).",
    "eredin_commander_of_the_red_riders": "Double the strength of all your Close Combat units (unless a Commander's Horn is on that row).",
    "eredin_bringer_of_death": "Restore a card from your discard pile to your hand.",
    "eredin_destroyer_of_worlds": "Discard 2 cards and draw 1 card of your choice from your deck.",
    "eredin_king_of_the_wild_hunt": "Pick any weather card from your deck and play it instantly.",
    "eredin_breacc_glas_the_treacherous": "Doubles the strength of all spy cards (both players).",
    "francesca_findabair_queen_of_dol_blathanna": "Destroy your enemy's strongest Close Combat unit(s) if the combined strength of his or her Close Combat units is 10 or more.",
    "francesca_findabair_the_beautiful": "Doubles the strength of all your Ranged units (unless a Commander's Horn is on that row).",
    "francesca_findabair_daisy_of_the_valley": "Draw an extra card at the beginning of the battle.",
    "francesca_findabair_pureblood_elf": "Pick a Biting Frost card from your deck and play it instantly.",
    "francesca_findabair_hope_of_the_aen_seidhe": "Move agile units to whichever valid row maximizes their strength.",
    "crach_an_craite": "Shuffle all cards from each player's graveyard back into their decks.",
    "king_bran": "Units only lose half their strength in bad weather.",
}
SUMMONS = {"cow": "bovine_defense_force", "kambi": "hemdall"}
TRANSFORM = {"berserker": "transformed_vildkaarl", "young_berserker": "transformed_young_vildkaarl"}
out = []
for c in cards.values():
    if c["type"] == "leader":
        c["effect"] = LEADER_EFFECT.get(c["id"], "?")
        c.pop("abilities", None)
    if c["id"] in SUMMONS: c["summons"] = SUMMONS[c["id"]]
    if c["id"] in TRANSFORM: c["transformsInto"] = TRANSFORM[c["id"]]
    if c["id"] in ("geralt_of_rivia", "cirilla_fiona_elen_riannon"):
        # muster:Roach 'next-gen' guncellemesiyle eklendi; orijinalde yetenek yok
        c["abilities"] = []
        c["nextgenAbilities"] = ["muster"]
    if c["id"] == "skellige_storm": c["abilities"] = ["storm"]
    if c["id"] == "roach": c["musterGroup"] = "Roach"
    if c["id"] == "poor_fucking_infantry": c["copies"] = 3  # oyun verisi (def_gwint_cards_final.xml): 3 kayıt
    out.append(c)
json.dump(out, open(sys.argv[2], "w", encoding="utf8"), ensure_ascii=False, indent=1)
print(len(out), "kart girisi")

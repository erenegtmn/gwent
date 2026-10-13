"""Çıkarılmış ham varlıkları istemcinin kullanacağı adlandırılmış klasörlere derler (kişisel kullanım).

Girdi : assets/extracted/gwint_game (w3swf.py), assets/extracted/tex (w3tex_extract.py), assets/extracted/picture_map.json
Çıktı : assets/ui/*.png, assets/cards/<kart_kimliği>.png, assets/cards/_picture_map.json, assets/ui/manifest.json
"""
import json
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EX = os.path.join(ROOT, "assets", "extracted")
UI = os.path.join(ROOT, "assets", "ui")
CARDS = os.path.join(ROOT, "assets", "cards")
SUB = os.path.join(EX, "gwint_game", "sub")
ATLAS = os.path.join(EX, "gwint_game", "atlas")
TEX = os.path.join(EX, "tex", "gameplay", "gui_new", "icons", "gwint")

# Arayüz parçaları: ad -> GFx alt resim kimliği (kataloglara bakılarak belirlendi)
SUBS = {
    "badge": 574, "badge_big": 602, "badge_hero": 617, "corner_sun": 600,
    # yetenek simgeleri (siyah çizim)
    "ab_horn": 576, "ab_muster": 578, "ab_medic": 580, "ab_spy": 582, "ab_bond": 584, "ab_morale": 586,
    "ab_agile": 588, "ab_scorch": 590, "ab_mardroeme": 592, "ab_berserker": 594, "ab_avenger": 596,
    "ab_clear": 604, "ab_frost": 606, "ab_fog": 608, "ab_rain": 610, "ab_decoy": 612, "ab_storm": 614,
    # sıra türleri
    "row_ranged": 619, "row_close": 620, "row_siege": 624,
    # fraksiyon kalkanı ve deste arkası
    "shield_monsters": 1209, "shield_nilfgaard": 1211, "shield_northern_realms": 1213, "shield_scoiatael": 1215, "shield_skellige": 1217,
    "deck_monsters": 1276, "deck_nilfgaard": 1278, "deck_northern_realms": 1280, "deck_scoiatael": 1282, "deck_skellige": 1284,
    "banner_neutral": 561, "banner_monsters": 563, "banner_northern_realms": 566, "banner_scoiatael": 568, "banner_skellige": 570,
    # genel
    "gem": 1163, "laurel": 1195, "ring": 1199, "avatar_silhouette": 1236, "avatar_geralt": 1203, "cards_icon": 1160,
    "crown_blue": 1249, "crown_red": 1251, "coin_a": 890, "coin_b": 910, "coin_side": 1189, "paper": 552,
    "hand_frame": 1243, "name_band": 1161, "label_box": 944, "glow": 1224, "glow_wide": 921,
    "divider": 555, "row_fire": 261,
    # hava (sıra boyunda)
    "wx_frost": 990, "wx_frost_alt": 987, "wx_fog": 999, "wx_rain": 1010, "wx_rain_streaks": 1065, "wx_lightning": 1073,
    "wx_cloud": 983, "wx_frost_flake": 979,
}

# Oyunun efekt animasyonu kareleri (GFx alt resimleri): ad -> kimlik listesi
FX_SEQUENCES = {
    "lightning": list(range(74, 126, 2)),   # mavi elektrik (kahraman / güç)
    "smoke": list(range(136, 158, 2)),      # yükselen duman (diriltme)
    "fire": [163] + list(range(166, 208, 2)),  # yanma (Yak)
    "burst": list(range(424, 462, 2)),      # kart iniş patlaması
    "cloud": list(range(356, 422, 2)),      # duman bulutu (toplanma/jeton)
    "storm": [1073, 1075, 1077],            # büyük yıldırım (fırtına)
}

# Lider kartı: oyun kimliği -> resim adı (oyun verisindeki etkilere göre eşlendi)
LEADER_PICTURES = {
    "foltest_king_of_temeria": "nor_ldr_foltest_silver",
    "foltest_lord_commander_of_the_north": "nor_ldr_foltest_gold",
    "foltest_the_siegemaster": "nor_ldr_foltest_copper",
    "foltest_the_steel_forged": "nor_ldr_foltest_bronze",
    "foltest_son_of_medell": "nor_ldr_foltest_platinium",
    "emhyr_var_emreis_his_imperial_majesty": "nil_ldr_emhyr_gold",
    "emhyr_var_emreis_emperor_of_nilfgaard": "nil_ldr_emhyr_bronze",
    "emhyr_var_emreis_the_white_flame": "nil_ldr_emhyr_copper",
    "emhyr_var_emreis_the_relentless": "nil_ldr_emhyr_silver",
    "emhyr_var_emreis_invader_of_the_north": "nil_ldr_emhyr_platinium",
    "francesca_findabair_pureblood_elf": "sco_ldr_francesca_gold",
    "francesca_findabair_daisy_of_the_valley": "sco_ldr_francesca_silver",
    "francesca_findabair_the_beautiful": "sco_ldr_francesca_copper",
    "francesca_findabair_queen_of_dol_blathanna": "sco_ldr_francesca_bronze",
    "francesca_findabair_hope_of_the_aen_seidhe": "sco_ldr_francesca_platinium",
    "eredin_king_of_the_wild_hunt": "nml_ldr_eredin_copper",
    "eredin_commander_of_the_red_riders": "nml_ldr_eredin_silver",
    "eredin_destroyer_of_worlds": "nml_ldr_eredin_gold",
    "eredin_bringer_of_death": "nml_ldr_eredin_bronze",
    "eredin_breacc_glas_the_treacherous": "nml_ldr_eredin_platinium",
    "king_bran": "ske_ldr_king_bran_bronze",
    "crach_an_craite": "ske_ldr_king_bran_copper",
}

# Özel kartlar ve jetonlar
OTHER_PICTURES = {
    "decoy": "spc_dummy", "commanders_horn": "spc_horn", "scorch": "spc_scorch", "biting_frost": "spc_frost",
    "impenetrable_fog": "spc_fog", "torrential_rain": "spc_rain", "clear_weather": "spc_clearsky",
    "mardroeme": "spc_mushroom", "skellige_storm": "spc_skellige_storm",
    "bovine_defense_force": "neu_czart", "hemdall": "ske_hemdal", "transformed_vildkaarl": "ske_berserker_bear",
    "transformed_young_vildkaarl": "ske_young_berserker_bear", "kambi": "ske_cock", "roach": "gog_roach",
    "cow": "neu_cow", "gaunter_odimm": "neu_mrmirror", "gaunter_odimm_darkness": "neu_mrmirror_foglet",
}


def copy(src, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copyfile(src, dst)


def main():
    os.makedirs(UI, exist_ok=True)
    os.makedirs(CARDS, exist_ok=True)
    manifest = {}

    # masa
    board = [f for f in os.listdir(ATLAS) if f.endswith("_i50a.png")][0]
    copy(os.path.join(ATLAS, board), os.path.join(UI, "board.png"))
    manifest["board"] = "board.png"

    missing = []
    for name, sid in SUBS.items():
        src = os.path.join(SUB, f"{sid}.png")
        if os.path.exists(src):
            copy(src, os.path.join(UI, name + ".png"))
            manifest[name] = name + ".png"
        else:
            missing.append(name)

    # efekt animasyon kareleri
    fx_dir = os.path.join(UI, "fx")
    os.makedirs(fx_dir, exist_ok=True)
    fx_counts = {}
    for name, ids in FX_SEQUENCES.items():
        n = 0
        for sid in ids:
            src = os.path.join(SUB, f"{sid}.png")
            if os.path.exists(src):
                copy(src, os.path.join(fx_dir, f"{name}_{n:02d}.png"))
                n += 1
        fx_counts[name] = n
    manifest["fx"] = fx_counts

    # ana menü: arka plan panoramaları (pano_<yer>.png) ve oyun logosu
    menu_dir = os.path.join(EX, "tex", "gameplay", "gui_new", "icons")
    pano_dir = os.path.join(menu_dir, "menubackground")
    n_pano = 0
    if os.path.isdir(pano_dir):
        for f in sorted(os.listdir(pano_dir)):
            if f.startswith("panorama_") and f.endswith(".png"):
                copy(os.path.join(pano_dir, f), os.path.join(UI, "pano_" + f[len("panorama_"):]))
                n_pano += 1
    logo = os.path.join(menu_dir, "logos", "witcherlogo_en_l.png")
    if os.path.exists(logo):
        copy(logo, os.path.join(UI, "logo.png"))
    manifest["pano"] = n_pano

    # mesaj afişleri
    for f in os.listdir(os.path.join(TEX, "message_icons")):
        copy(os.path.join(TEX, "message_icons", f), os.path.join(UI, "msg_" + f))
    for f in os.listdir(os.path.join(TEX, "results")):
        copy(os.path.join(TEX, "results", f), os.path.join(UI, "msg_" + f))

    # kart resimleri: oyun resim adı -> kendi kart kimliğimiz
    pic_map = json.load(open(os.path.join(EX, "picture_map.json"), encoding="utf-8"))  # resim -> kimlik
    ours = {}
    for pic, cid in pic_map.items():
        ours.setdefault(cid, pic)
    for cid, pic in OTHER_PICTURES.items():
        ours[cid] = pic
    for cid, pic in LEADER_PICTURES.items():
        ours[cid] = pic
    out_map, miss_pic = {}, []
    for cid, pic in sorted(ours.items()):
        src = os.path.join(TEX, pic + ".png")
        if not os.path.exists(src):
            miss_pic.append((cid, pic))
            continue
        copy(src, os.path.join(CARDS, cid + ".png"))
        out_map[cid] = pic
    json.dump(out_map, open(os.path.join(CARDS, "_picture_map.json"), "w"), indent=1)
    json.dump(manifest, open(os.path.join(UI, "manifest.json"), "w"), indent=1)
    print(f"arayüz: {len(manifest)} parça ({len(missing)} eksik: {missing}); kart resmi: {len(out_map)}; resmi bulunamayan: {miss_pic}")


if __name__ == "__main__":
    main()

"""data/cards.json -> KARTLAR.md (okunabilir liste). Kullanim: python tools/build_kartlar_md.py"""
import json
d = json.load(open('data/cards.json', encoding='utf8'))
F = {"northern_realms": "Northern Realms", "nilfgaard": "Nilfgaard", "scoiatael": "Scoia'tael", "monsters": "Monsters", "skellige": "Skellige", "neutral": "Tarafsız"}
E = {"base": "", "hos": " (HoS)", "bw": " (B&W)", "nextgen": " (Next-Gen)"}
R = {"close": "Yakın", "ranged": "Menzilli", "siege": "Kuşatma", "agile": "Çevik (Yakın/Menzilli)"}
L = ["# Kart Listesi", "", "> Otomatik üretildi: `python tools/build_cards.py` → `data/cards.json` → `python tools/build_kartlar_md.py`. **Elle düzenleme.**",
     "> HoS = Hearts of Stone, B&W = Blood and Wine, Next-Gen = yalnız yeni nesil güncelleme. Kopya = destede en fazla kaç tane.",
     "> Yetenek anahtarları: bond=Tight Bond, morale=Morale Boost, muster, medic, spy, scorch (+ _c/_r/_s satır kısıtlı), horn, agile, avenger, berserker, mardroeme.", ""]
for f in F:
    L += [f"## {F[f]}", ""]
    ld = [c for c in d if c['faction'] == f and c['type'] == 'leader']
    if ld:
        L += ["**Liderler**", "", "| Lider | Etki |", "|---|---|"] + [f"| {c['name']}{E[c['edition']]} | {c['effect']} |" for c in ld] + [""]
    un = [c for c in d if c['faction'] == f and c['type'] == 'unit']
    if un:
        L += ["**Birimler**", "", "| Kart | Sıra | Güç | Kahraman | Yetenek | Kopya |", "|---|---|---|---|---|---|"]
        for c in un:
            ab = ", ".join(c['abilities'] + (["(next-gen: " + ",".join(c['nextgenAbilities']) + ")"] if c.get('nextgenAbilities') else []))
            if c.get('musterGroup') and 'muster' in c['abilities']: ab += f" [{c['musterGroup']}]"
            if c.get('summons'): ab += f" → {c['summons']}"
            if c.get('transformsInto'): ab += f" → {c['transformsInto']}"
            cp = "jeton" if c.get('token') else c['copies']
            L.append(f"| {c['name']}{E[c['edition']]} | {R[c['row']]} | {c['strength']} | {'✔' if c['hero'] else ''} | {ab} | {cp} |")
        L.append("")
    sp = [c for c in d if c['faction'] == f and c['type'] in ('special', 'weather')]
    if sp:
        L += ["**Özel / Hava**", "", "| Kart | Tür | Yetenek | Kopya |", "|---|---|---|---|"] + [f"| {c['name']}{E[c['edition']]} | {'Hava' if c['type'] == 'weather' else 'Özel'} | {', '.join(c['abilities'])} | {c['copies']} |" for c in sp] + [""]
open('KARTLAR.md', 'w', encoding='utf8').write("\n".join(L))
print(len(L), "satir")

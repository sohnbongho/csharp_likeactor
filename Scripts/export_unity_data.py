"""
유니티 프로젝트(unity_samkuk)의 ScriptableObject(.asset)를 읽어 게임서버용 JSON으로 내보낸다.

사용: python Scripts/export_unity_data.py [unity_project_root] [out_dir]
기본값: ../unity_samkuk, GameServer/Data

유니티 에디터 없이 돌릴 수 있도록 Unity YAML의 필요한 부분만 직접 파싱한다.
(PyYAML은 Unity 태그 때문에 그대로 쓸 수 없고, 설치돼 있지 않다.)
표시용 필드(sprite, tint, portrait 등)는 내보내지 않는다. id는 에셋 파일명이다.
"""
import json
import os
import re
import sys

ROLE_NAMES = ["Infantry", "Archer", "Cavalry"]
WEAPON_TYPES = ["Arrow", "Slash", "Orbit", "Thrust", "FireZone", "Lightning", "Rain", "Nova", "Halberd"]
PASSIVE_TYPES = ["Damage", "Cooldown", "MoveSpeed", "MaxHp", "PickupRadius", "ExpGain", "Regen"]


# ───────────────────────── Unity YAML 부분 파서 ─────────────────────────

def _scalar(text):
    text = text.strip()
    if text == "":
        return ""
    if text.startswith('"'):
        # YAML 더블쿼트 문자열: \uXXXX 이스케이프는 JSON과 같다
        return json.loads(text)
    if text.startswith("{") and text.endswith("}"):
        inner = text[1:-1].strip()
        result = {}
        if inner:
            for part in inner.split(","):
                k, v = part.split(":", 1)
                result[k.strip()] = _scalar(v)
        return result
    if text.startswith("[") and text.endswith("]"):
        inner = text[1:-1].strip()
        return [_scalar(p) for p in inner.split(",")] if inner else []
    if re.fullmatch(r"-?\d+", text):
        return int(text)
    if re.fullmatch(r"-?\d*\.\d+(e-?\d+)?|-?\d+e-?\d+", text):
        return float(text)
    return text


def _join_folded(lines, i, indent):
    """더블쿼트 문자열이 여러 줄로 접혀 있으면 닫는 따옴표까지 이어 붙인다."""
    text = lines[i]
    while text.count('"') % 2 == 1 and i + 1 < len(lines):
        i += 1
        text = text + " " + lines[i].strip()
    return text, i


def _parse_block(lines, i, indent):
    """lines[i:]에서 들여쓰기 indent인 블록(dict 또는 list)을 파싱해 (값, 다음 인덱스)를 돌려준다."""
    if i >= len(lines):
        return {}, i
    first = lines[i]
    if first[indent:].startswith("- "):
        return _parse_list(lines, i, indent)
    return _parse_dict(lines, i, indent)


def _indent_of(line):
    return len(line) - len(line.lstrip(" "))


def _parse_dict(lines, i, indent):
    result = {}
    while i < len(lines):
        line = lines[i]
        if line.strip() == "":
            i += 1
            continue
        ind = _indent_of(line)
        if ind < indent or (ind == indent and line[ind:].startswith("- ")):
            break
        if ind > indent:
            # 예상 밖의 깊은 들여쓰기(접힌 문자열 잔여 등)는 건너뛴다
            i += 1
            continue
        body = line[indent:]
        if ":" not in body:
            i += 1
            continue
        key, rest = body.split(":", 1)
        key = key.strip()
        rest = rest.strip()
        if rest == "":
            # 중첩 블록 (다음 줄의 들여쓰기로 판단)
            j = i + 1
            while j < len(lines) and lines[j].strip() == "":
                j += 1
            if j < len(lines) and _indent_of(lines[j]) > indent:
                value, i = _parse_block(lines, j, _indent_of(lines[j]))
                result[key] = value
                continue
            if j < len(lines) and _indent_of(lines[j]) == indent and lines[j][indent:].startswith("- "):
                # Unity는 리스트를 부모와 같은 들여쓰기로 쓸 때가 있다
                value, i = _parse_list(lines, j, indent)
                result[key] = value
                continue
            result[key] = None
            i = j
            continue
        if rest.startswith('"') and rest.count('"') % 2 == 1:
            full, i = _join_folded(lines, i, indent)
            rest = full.split(":", 1)[1].strip()
        result[key] = _scalar(rest)
        i += 1
    return result, i


def _parse_list(lines, i, indent):
    result = []
    while i < len(lines):
        line = lines[i]
        if line.strip() == "":
            i += 1
            continue
        ind = _indent_of(line)
        if ind != indent or not line[indent:].startswith("- "):
            break
        item_text = line[indent + 2:]
        if item_text.strip().startswith("{"):
            result.append(_scalar(item_text))
            i += 1
            continue
        # "- key: value" 로 시작하는 dict 항목: 첫 줄을 indent+2 로 당겨서 dict로 파싱
        lines[i] = " " * (indent + 2) + item_text
        value, i = _parse_dict(lines, i, indent + 2)
        result.append(value)
    return result, i


def parse_asset(path):
    with open(path, encoding="utf-8") as f:
        text = f.read()
    idx = text.find("MonoBehaviour:")
    if idx < 0:
        return None
    body = text[idx + len("MonoBehaviour:"):]
    lines = body.split("\n")
    lines = [ln.rstrip("\r") for ln in lines]
    # 첫 비어있지 않은 줄의 들여쓰기(보통 2)
    j = 0
    while j < len(lines) and lines[j].strip() == "":
        j += 1
    if j >= len(lines):
        return {}
    data, _ = _parse_dict(lines, j, _indent_of(lines[j]))
    return data


# ───────────────────────── 변환 ─────────────────────────

def ref_name(value, guid_map):
    """{fileID, guid, type} 참조를 에셋 이름으로. 빈 참조면 ''."""
    if not isinstance(value, dict):
        return ""
    guid = value.get("guid")
    if not guid:
        return ""
    return guid_map.get(guid, "")


def num(d, key, default=0.0):
    v = d.get(key, default)
    if v is None or v == "":
        return default
    return v


def export(unity_root, out_dir):
    so_root = os.path.join(unity_root, "Assets", "ScriptableObjects")
    guid_map = {}
    assets = {}
    for dirpath, _, files in os.walk(so_root):
        for fn in files:
            if fn.endswith(".asset.meta"):
                with open(os.path.join(dirpath, fn), encoding="utf-8") as f:
                    m = re.search(r"guid:\s*([0-9a-f]+)", f.read())
                if m:
                    guid_map[m.group(1)] = fn[:-len(".asset.meta")]
            elif fn.endswith(".asset"):
                assets[fn[:-len(".asset")]] = os.path.join(dirpath, fn)

    def load_dir(sub):
        result = {}
        d = os.path.join(so_root, sub)
        if not os.path.isdir(d):
            return result
        for fn in sorted(os.listdir(d)):
            if fn.endswith(".asset"):
                name = fn[:-len(".asset")]
                result[name] = parse_asset(os.path.join(d, fn)) or {}
        return result

    enemies = []
    for name, d in load_dir("Enemies").items():
        enemies.append({
            "id": name,
            "displayName": d.get("displayName", name),
            "role": ROLE_NAMES[int(num(d, "role", 0))],
            "scale": num(d, "scale", 1.0),
            "moveSpeed": num(d, "moveSpeed", 1.5),
            "maxHp": num(d, "maxHp", 10),
            "contactDamage": num(d, "contactDamage", 5),
            "expReward": num(d, "expReward", 1),
            "chargeInterval": num(d, "chargeInterval", 0.0),
            "chargeWindup": num(d, "chargeWindup", 0.6),
            "chargeDuration": num(d, "chargeDuration", 0.8),
            "chargeSpeedMultiplier": num(d, "chargeSpeedMultiplier", 3.0),
            "attackRange": num(d, "attackRange", 0.0),
            "fireInterval": num(d, "fireInterval", 2.4),
            "projectileSpeed": num(d, "projectileSpeed", 6.0),
            "projectileDamage": num(d, "projectileDamage", 6),
            "projectileLifetime": num(d, "projectileLifetime", 3.0),
            "projectileSize": num(d, "projectileSize", 0.18),
            "colliderRadius": num(d, "colliderRadius", 0.4),
        })

    weapons = []
    for name, d in load_dir("Weapons").items():
        weapons.append({
            "id": name,
            "displayName": d.get("displayName", name),
            "description": d.get("description", ""),
            "type": WEAPON_TYPES[int(num(d, "type", 0))],
            "damage": num(d, "damage", 10.0),
            "cooldown": num(d, "cooldown", 1.0),
            "range": num(d, "range", 8.0),
            "count": int(num(d, "count", 1)),
            "projectileSpeed": num(d, "projectileSpeed", 10.0),
            "pierce": int(num(d, "pierce", 1)),
            "duration": num(d, "duration", 2.0),
            "size": num(d, "size", 0.25),
            "rotateSpeed": num(d, "rotateSpeed", 180.0),
            "tickInterval": num(d, "tickInterval", 0.3),
            "knockback": num(d, "knockback", 0.0),
            "swingArcDegrees": num(d, "swingArcDegrees", 0.0),
            "comboSwings": bool(num(d, "comboSwings", 0)),
            "swingDuration": num(d, "swingDuration", 0.0),
            "trailTravel": num(d, "trailTravel", 0.0),
            "trailScale": num(d, "trailScale", 0.0),
            "trailDamageRatio": num(d, "trailDamageRatio", 0.0),
            "maxLevel": int(num(d, "maxLevel", 8)),
            "damagePerLevel": num(d, "damagePerLevel", 0.2),
            "cooldownReductionPerLevel": num(d, "cooldownReductionPerLevel", 0.05),
            "levelsPerExtraCount": int(num(d, "levelsPerExtraCount", 2)),
        })

    passives = []
    for name, d in load_dir("Passives").items():
        passives.append({
            "id": name,
            "displayName": d.get("displayName", name),
            "description": d.get("description", ""),
            "type": PASSIVE_TYPES[int(num(d, "type", 0))],
            "valuePerLevel": num(d, "valuePerLevel", 0.1),
            "maxLevel": int(num(d, "maxLevel", 5)),
        })

    evolutions = []
    for name, d in load_dir("Evolutions").items():
        evolutions.append({
            "id": name,
            "baseWeapon": ref_name(d.get("baseWeapon"), guid_map),
            "requiredPassive": ref_name(d.get("requiredPassive"), guid_map),
            "evolvedWeapon": ref_name(d.get("evolvedWeapon"), guid_map),
            "requiredLevel": int(num(d, "requiredLevel", 5)),
            "requiredPassiveLevel": int(num(d, "requiredPassiveLevel", 1)),
        })

    heroes = []
    for name, d in load_dir("Heroes").items():
        heroes.append({
            "id": name,
            "displayName": d.get("displayName", name),
            "maxHpBonus": num(d, "maxHpBonus", 0.0),
            "damageMultiplier": num(d, "damageMultiplier", 1.0),
            "moveSpeedMultiplier": num(d, "moveSpeedMultiplier", 1.0),
            "expMultiplier": num(d, "expMultiplier", 1.0),
            "pickupRadiusMultiplier": num(d, "pickupRadiusMultiplier", 1.0),
            "startingWeapon": ref_name(d.get("startingWeapon"), guid_map),
            "skill": ref_name(d.get("skill"), guid_map),
            "weapons": [ref_name(w, guid_map) for w in (d.get("weapons") or []) if ref_name(w, guid_map)],
        })

    stages = []
    for name, d in load_dir("Stage").items():
        waves = []
        for w in d.get("waves") or []:
            waves.append({
                "name": w.get("name", ""),
                "startTime": num(w, "startTime", 0.0),
                "spawnPerSecond": num(w, "spawnPerSecond", 5.0),
                "maxAlive": int(num(w, "maxAlive", 150)),
                "enemies": [
                    {"enemy": ref_name(e.get("data"), guid_map), "weight": num(e, "weight", 1.0)}
                    for e in (w.get("enemies") or []) if ref_name(e.get("data"), guid_map)
                ],
            })
        events = []
        for e in d.get("events") or []:
            events.append({
                "time": num(e, "time", 0.0),
                "enemy": ref_name(e.get("enemy"), guid_map),
                "count": int(num(e, "count", 1)),
                "isBoss": bool(num(e, "isBoss", 0)),
                "message": e.get("message", ""),
            })
        stages.append({
            "id": name,
            "displayName": d.get("displayName", name),
            "duration": num(d, "duration", 60.0),
            "waves": waves,
            "events": events,
        })

    catalog = {}
    cat_path = assets.get("UpgradeCatalog")
    if cat_path:
        d = parse_asset(cat_path) or {}
        catalog = {
            "weapons": [ref_name(w, guid_map) for w in (d.get("weapons") or []) if ref_name(w, guid_map)],
            "passives": [ref_name(p, guid_map) for p in (d.get("passives") or []) if ref_name(p, guid_map)],
            "evolutions": [ref_name(e, guid_map) for e in (d.get("evolutions") or []) if ref_name(e, guid_map)],
            "weaponLevelUpWeight": num(d, "weaponLevelUpWeight", 4.0),
            "passiveWeight": num(d, "passiveWeight", 2.0),
            "newWeaponWeight": num(d, "newWeaponWeight", 1.0),
        }

    # 씬/프리팹에 직렬화된 값은 이 스크립트가 읽지 못하므로 코드 기본값을 쓴다.
    # 유니티 쪽에서 실제 값을 알려 주면 여기나 결과 파일을 고친다.
    balance = {
        "playerMaxHp": 100.0,
        "playerMoveSpeed": 4.0,
        "playerBodyRadius": 0.32,
        "invulnerableTime": 0.5,
        "regenTickSeconds": 0.5,
        "separationStrength": 3.0,
        "maxSeparationSpeed": 3.0,
        "tooFarDistance": 25.0,
        "playerRadius": 0.4,
        "cellSize": 1.5,
        "contactPadding": 0.15,
        "spawnRingRadius": 13.5,
        "maxGems": 300,
        "magnetRadius": 2.2,
        "collectRadius": 0.35,
        "gemStartSpeed": 4.0,
        "gemAcceleration": 30.0,
        "gemMaxSpeed": 20.0,
        "expBase": 7,
        "expStep": 6,
        "choiceCount": 3,
        "maxWeapons": 4,
        "healRatio": 0.3,
        "knockbackDecay": 8.0,
        "maxKnockbackSpeed": 14.0,
        "shotWarningSeconds": 0.4,
        "enemyProjectilePlayerRadius": 0.4,
        "arrowSpreadDegrees": 12.0,
    }

    os.makedirs(out_dir, exist_ok=True)
    outputs = {
        "enemies.json": enemies,
        "weapons.json": weapons,
        "passives.json": passives,
        "evolutions.json": evolutions,
        "heroes.json": heroes,
        "stages.json": stages,
        "upgrade_catalog.json": catalog,
        "balance.json": balance,
    }
    for fn, data in outputs.items():
        with open(os.path.join(out_dir, fn), "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
            f.write("\n")
        count = len(data) if isinstance(data, list) else 1
        print(f"{fn}: {count}")


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    unity = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "..", "..", "unity_samkuk")
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(here, "..", "GameServer", "Data")
    export(os.path.abspath(unity), os.path.abspath(out))

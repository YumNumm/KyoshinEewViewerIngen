#!/usr/bin/env python3
#
# EQMonitor Backend の OpenAPI 仕様を取得し、NSwag が扱える形へ正規化して
# src/KyoshinEewViewer.EqMonitorApi/openapi.json へ書き出す。
#
# 使い方:
#   ./scripts/update-eqmonitor-openapi.py              # gh 経由で最新を取得して更新
#   ./scripts/update-eqmonitor-openapi.py path/to.json # 手元のファイルから更新
#
# 正規化が必要な理由 (いずれも NSwag 14.6.3 で生成失敗・誤生成を確認済み):
#
#   1. servers を除去する
#      NSwag は servers[0].url を BaseUrl の初期値としてソースへ焼き込む。
#      KEVi はサーバの URL を一切保持せず利用者に入力させる方針のため許容できない。
#      生成側でも /UseBaseUrl:false を指定し、HttpClient.BaseAddress のみを使う。
#
#   2. 不要なパスと到達不能なスキーマを削る
#      KEVi が使うのは地震情報と緊急地震速報のみ。全体を生成すると 20,000 行を超え、
#      端末設定まわりの enum が後述 3. の衝突を起こす。
#
#   3. C# 識別子として衝突する enum 値に x-enumNames を付ける
#      NSwag の既定の名前付けでは震度の "!5-" と "5-" が共に _5 となり CS0102 で失敗する。
#      大文字小文字だけが異なる値 ("D" と "d" など) も同様に衝突する。
#
#   4. OpenAPI 3.1 の anyOf を 3.0 風の nullable へ畳み込む
#      NSwag は anyOf を解決できず、中身のない空クラスを生成してしまう。
#      その結果 headline が string? にならず、statuses クエリも指定不能になる。
#
import copy
import json
import re
import subprocess
import sys
from pathlib import Path

REPO = "YumNumm/eqmonitor-backend"
SPEC_PATH = "api/api/openapi.json"
OUTPUT = (
    Path(__file__).resolve().parent.parent
    / "src"
    / "KyoshinEewViewer.EqMonitorApi"
    / "openapi.json"
)

# KEVi が利用するパスの接頭辞
KEEP_PREFIXES = ("/v2/earthquake", "/v2/eew")

# enum 値に含まれる記号を C# 識別子へ置き換えるための対応表
CHAR_WORDS = {"+": "Plus", "-": "Minus", "!": "Over", ".": "Dot", "/": "Slash"}


def fetch_spec() -> dict:
    """gh CLI 経由で最新の openapi.json を取得する"""
    result = subprocess.run(
        [
            "gh",
            "api",
            "-H",
            "Accept: application/vnd.github.raw",
            f"/repos/{REPO}/contents/{SPEC_PATH}?ref=main",
        ],
        check=True,
        capture_output=True,
    )
    return json.loads(result.stdout)


def collect_refs(node, acc: set) -> None:
    """node 以下から #/components/... への参照を集める"""
    if isinstance(node, list):
        for value in node:
            collect_refs(value, acc)
        return
    if not isinstance(node, dict):
        return
    ref = node.get("$ref")
    if isinstance(ref, str) and ref.startswith("#/components/"):
        acc.add(ref)
    for value in node.values():
        collect_refs(value, acc)


def prune(doc: dict) -> dict:
    """利用するパスのみ残し、そこから到達できない components を削除する"""
    doc["paths"] = {
        path: value
        for path, value in doc["paths"].items()
        if path.startswith(KEEP_PREFIXES)
    }

    reachable: set[str] = set()
    frontier: set[str] = set()
    collect_refs(doc["paths"], frontier)
    while frontier:
        ref = frontier.pop()
        if ref in reachable:
            continue
        reachable.add(ref)
        _, _, section, name = ref.split("/")
        target = doc["components"].get(section, {}).get(name)
        if target is None:
            continue
        nested: set[str] = set()
        collect_refs(target, nested)
        frontier |= nested - reachable

    for section, items in list(doc["components"].items()):
        doc["components"][section] = {
            name: value
            for name, value in items.items()
            if f"#/components/{section}/{name}" in reachable
        }
    return doc


def is_null_schema(schema) -> bool:
    return isinstance(schema, dict) and schema.get("type") == "null"


def fold_any_of(node):
    """anyOf を単一の型 + nullable へ畳み込む"""
    if isinstance(node, list):
        return [fold_any_of(value) for value in node]
    if not isinstance(node, dict):
        return node

    node = {key: fold_any_of(value) for key, value in node.items()}
    if not isinstance(node.get("anyOf"), list):
        return node

    variants = node["anyOf"]
    non_null = [v for v in variants if not is_null_schema(v)]
    nullable = len(non_null) != len(variants)

    # anyOf: [array<X>, X] は単数指定も許容するクエリパラメータ。配列側に寄せる
    arrays = [v for v in non_null if v.get("type") == "array"]
    if len(non_null) == 2 and len(arrays) == 1:
        non_null = arrays

    # 畳み込めない (真の union) 場合はそのまま返す
    if len(non_null) != 1:
        return node

    picked = copy.deepcopy(non_null[0])
    siblings = {key: value for key, value in node.items() if key != "anyOf"}

    # $ref は兄弟キーを持てないため allOf で包む
    if "$ref" in picked:
        folded = {"allOf": [picked]}
        folded.update(siblings)
    else:
        picked.update(siblings)
        folded = picked

    if nullable:
        folded["nullable"] = True
    return folded


def csharp_enum_name(value: str, disambiguate_case: bool) -> str:
    """enum 値から C# 識別子を作る"""
    name = "".join(CHAR_WORDS.get(char, char) for char in value)
    name = re.sub(r"[^A-Za-z0-9_]", "_", name)
    if name and name[0].isdigit():
        name = "_" + name
    # "D" と "d" のように大文字化すると衝突する場合は接尾辞で区別する
    if disambiguate_case and value.isupper():
        name += "_Upper"
    return name


def inject_enum_names(node) -> None:
    """C# 識別子として安全な名前を x-enumNames として与える"""
    if isinstance(node, list):
        for value in node:
            inject_enum_names(value)
        return
    if not isinstance(node, dict):
        return

    values = node.get("enum")
    if isinstance(values, list) and values and all(isinstance(v, str) for v in values):
        disambiguate_case = len({v.lower() for v in values}) != len(values)
        names: list[str] = []
        used: set[str] = set()
        for value in values:
            base = csharp_enum_name(value, disambiguate_case)
            name, suffix = base, 2
            while name in used:
                name, suffix = f"{base}_{suffix}", suffix + 1
            used.add(name)
            names.append(name)
        if names != values:
            node["x-enumNames"] = names

    for value in node.values():
        inject_enum_names(value)


def main() -> int:
    if len(sys.argv) > 1:
        doc = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    else:
        doc = fetch_spec()

    # サーバの URL は KEVi 側に一切持たせない
    doc.pop("servers", None)
    doc = prune(doc)
    doc = fold_any_of(doc)
    inject_enum_names(doc)

    OUTPUT.write_text(
        json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(
        f"{OUTPUT} を更新しました "
        f"(パス {len(doc['paths'])} 件 / スキーマ {len(doc['components'].get('schemas', {}))} 件)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())

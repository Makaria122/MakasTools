"""Releases に載せた zip と package.json から VPM リスティング（vpm.json）を作る。

使い方: python3 build_listing.py <出力フォルダ>
環境変数: GH_TOKEN, REPOSITORY（owner/name）
"""
import hashlib
import json
import os
import sys
import urllib.request

LISTING_NAME = "Maka's Tools"
LISTING_ID = "jp.makaria.vpm"
AUTHOR = "Makaria"


def fetch(url, accept="application/vnd.github+json"):
    request = urllib.request.Request(url, headers={
        "Accept": accept,
        "Authorization": f"Bearer {os.environ['GH_TOKEN']}",
        "User-Agent": "makas-tools-listing",
    })
    with urllib.request.urlopen(request) as response:
        return response.read()


def releases(repository):
    page = 1
    while True:
        items = json.loads(fetch(f"https://api.github.com/repos/{repository}/releases?per_page=100&page={page}"))
        if not items:
            return
        yield from items
        page += 1


def main():
    out_dir = sys.argv[1]
    repository = os.environ["REPOSITORY"]
    owner, name = repository.split("/")
    listing_url = f"https://{owner.lower()}.github.io/{name}/vpm.json"

    packages = {}
    for release in releases(repository):
        if release["draft"] or release["prerelease"]:
            continue
        assets = {a["name"]: a for a in release["assets"]}
        manifest_asset = assets.get("package.json")
        zips = [a for n, a in assets.items() if n.endswith(".zip")]
        if manifest_asset is None or len(zips) != 1:
            print(f"skip {release['tag_name']}: package.json と zip が揃っていない")
            continue

        manifest = json.loads(fetch(manifest_asset["url"], "application/octet-stream"))
        zip_data = fetch(zips[0]["url"], "application/octet-stream")
        manifest["url"] = zips[0]["browser_download_url"]
        manifest["zipSHA256"] = hashlib.sha256(zip_data).hexdigest()
        manifest["repo"] = listing_url
        packages.setdefault(manifest["name"], {"versions": {}})["versions"][manifest["version"]] = manifest
        print(f"add {manifest['name']} {manifest['version']}")

    listing = {
        "name": LISTING_NAME,
        "id": LISTING_ID,
        "url": listing_url,
        "author": AUTHOR,
        "packages": packages,
    }

    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "vpm.json"), "w", encoding="utf-8") as f:
        json.dump(listing, f, ensure_ascii=False, indent=2)

    add_url = f"vcc://vpm/addRepo?url={listing_url}"
    with open(os.path.join(out_dir, "index.html"), "w", encoding="utf-8") as f:
        f.write(f"""<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>{LISTING_NAME}</title>
<style>body{{font-family:sans-serif;max-width:640px;margin:40px auto;padding:0 16px;line-height:1.7}}
a.button{{display:inline-block;padding:10px 18px;background:#2b6cb0;color:#fff;border-radius:6px;text-decoration:none}}
code{{background:#eee;padding:2px 6px;border-radius:4px;word-break:break-all}}</style></head>
<body><h1>{LISTING_NAME}</h1>
<p>VRChat アバター改変用のエディタ拡張ツール集です。</p>
<p><a class="button" href="{add_url}">VCC / ALCOM に追加</a></p>
<p>ボタンが動かない場合は、次の URL を「Add Repository」に登録してください。<br><code>{listing_url}</code></p>
<p><a href="https://github.com/{repository}">GitHub</a></p>
</body></html>
""")


if __name__ == "__main__":
    main()

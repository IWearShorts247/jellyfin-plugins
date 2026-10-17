#!/usr/bin/env python3
"""Helpers for build.sh: fill a plugin's meta.json and maintain manifest.json.

  manifest.py stage  <meta.json> <version> <timestamp>
      Print meta.json with version and timestamp filled in (goes in the zip).
  manifest.py update <manifest.json> <meta.json> <version> <timestamp> <md5> <sourceUrl>
      Insert or replace this version in the plugin's entry in manifest.json.
"""
import json
import sys


def load(path, default):
    try:
        with open(path) as f:
            return json.load(f)
    except FileNotFoundError:
        return default


def version_key(v):
    return tuple(int(p) for p in v.split("."))


def stage(meta_path, version, timestamp):
    meta = load(meta_path, None)
    meta["version"] = version
    meta["timestamp"] = timestamp
    print(json.dumps(meta, indent=2))


def update(manifest_path, meta_path, version, timestamp, checksum, url):
    meta = load(meta_path, None)
    manifest = load(manifest_path, [])
    old = next((p for p in manifest if p["guid"] == meta["guid"]), None)
    entry = {"guid": meta["guid"]}
    for key in ("name", "description", "overview", "owner", "category", "imageUrl"):
        if key in meta:
            entry[key] = meta[key]
    entry["versions"] = [v for v in (old or {}).get("versions", []) if v["version"] != version]
    if old is not None:
        manifest[manifest.index(old)] = entry
    else:
        manifest.append(entry)
    entry["versions"].append({
        "version": version,
        "changelog": meta.get("changelog", ""),
        "targetAbi": meta["targetAbi"],
        "sourceUrl": url,
        "checksum": checksum,
        "timestamp": timestamp,
    })
    entry["versions"].sort(key=lambda v: version_key(v["version"]), reverse=True)
    manifest.sort(key=lambda p: p["name"].lower())
    with open(manifest_path, "w") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    cmd, args = sys.argv[1], sys.argv[2:]
    {"stage": stage, "update": update}[cmd](*args)

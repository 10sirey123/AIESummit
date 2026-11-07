"""Fetch pinned public benchmark releases; requires only Python's standard library."""

import argparse
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import time
import urllib.request
import zipfile
from datetime import datetime, timezone


PROJECT = Path(__file__).resolve().parent.parent
ROOT = PROJECT / "corporea"
# Several original contract filenames exceed the legacy Windows path limit.
if os.name == "nt":
    ROOT = Path("\\\\?\\" + str(ROOT))


def digest(path, algorithm):
    hasher = hashlib.new("sha1" if algorithm == "git-sha1" else algorithm)
    if algorithm == "git-sha1":
        hasher.update(f"blob {path.stat().st_size}\0".encode())
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(4 * 1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def valid(path, entry):
    algorithm, expected = entry["checksum"].split(":", 1)
    return (path.is_file() and path.stat().st_size == entry["size"]
            and digest(path, algorithm) == expected)


def extract(archive, entry, verify_only):
    destination = ROOT / entry["extract_to"]
    marker = destination / (".verified-" + archive.name + ".json")
    with zipfile.ZipFile(archive) as bundle:
        members = bundle.infolist()
        for member in members:
            relative = PurePosixPath(member.filename.replace("\\", "/"))
            if (relative.is_absolute() or ".." in relative.parts
                    or any(":" in part for part in relative.parts)):
                raise ValueError(f"Unsafe archive path: {member.filename}")
        if verify_only:
            # Check extracted bytes against the archive CRC, not just file presence.
            import zlib
            for member in members:
                if member.is_dir():
                    continue
                target = destination.joinpath(*PurePosixPath(member.filename).parts)
                if not target.is_file() or target.stat().st_size != member.file_size:
                    raise ValueError(f"Missing or incomplete extraction: {target}")
                crc = 0
                with target.open("rb") as source:
                    for chunk in iter(lambda: source.read(4 * 1024 * 1024), b""):
                        crc = zlib.crc32(chunk, crc)
                if crc != member.CRC:
                    raise ValueError(f"Extracted file CRC mismatch: {target}")
        elif not marker.exists():
            destination.mkdir(parents=True, exist_ok=True)
            # ZipFile verifies each member's CRC as it is read.
            for member in members:
                target = destination.joinpath(*PurePosixPath(member.filename).parts)
                if member.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with bundle.open(member) as source, target.open("wb") as output:
                        shutil.copyfileobj(source, output, 4 * 1024 * 1024)
            marker.write_text(json.dumps({"checksum": entry["checksum"]}), encoding="utf-8")
        else:
            if json.loads(marker.read_text(encoding="utf-8"))["checksum"] != entry["checksum"]:
                raise ValueError(f"Extraction source changed: {destination}; use a fresh destination")
    return len([m for m in members if not m.is_dir()])


def fetch(entry, verify_only):
    path = ROOT / entry["path"]
    if not valid(path, entry):
        if verify_only:
            raise ValueError(f"Missing or invalid payload: {entry['path']}")
        path.parent.mkdir(parents=True, exist_ok=True)
        partial = path.with_name(path.name + ".part")
        for attempt in range(5):
            try:
                # Resume partial files when the upstream supports byte ranges.
                if valid(partial, entry):
                    partial.replace(path)
                    break
                offset = partial.stat().st_size if partial.exists() else 0
                if offset >= entry["size"]:
                    partial.unlink()
                    offset = 0
                headers = {"User-Agent": "AIESummit-BenchmarkBootstrap/1.0"}
                if offset:
                    headers["Range"] = f"bytes={offset}-"
                request = urllib.request.Request(entry["url"], headers=headers)
                with urllib.request.urlopen(request, timeout=120) as response:
                    append = offset and response.status == 206
                    if append and not response.headers.get("Content-Range", "").startswith(f"bytes {offset}-"):
                        raise ValueError("Unexpected download range response")
                    with partial.open("ab" if append else "wb") as out:
                        shutil.copyfileobj(response, out, 4 * 1024 * 1024)
                if partial.stat().st_size < entry["size"]:
                    # A server can end a response early without raising an exception.
                    # Keep the received prefix available for a range retry.
                    raise OSError(f"Incomplete transfer: {partial.stat().st_size}/{entry['size']} bytes")
                if not valid(partial, entry):
                    partial.unlink()
                    raise ValueError(f"Download checksum mismatch: {entry['path']}")
                partial.replace(path)
                break
            except Exception as error:
                if attempt == 4:
                    raise
                print(f"Retry {attempt + 1}/4 {entry['path']}: {error}", flush=True)
                time.sleep(min(2 ** attempt, 16))
    result = {"path": entry["path"], "bytes": path.stat().st_size,
              "upstream_checksum": entry["checksum"], "sha256": digest(path, "sha256")}
    if "extract_to" in entry:
        result["extracted_files"] = extract(path, entry, verify_only)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify-only", action="store_true")
    parser.add_argument("--workers", type=int, default=6)
    args = parser.parse_args()
    if args.workers < 1:
        parser.error("--workers must be at least 1")
    lock = json.loads((ROOT / "sources.lock.json").read_text(encoding="utf-8"))
    files = lock["files"]
    report = {"sources": lock["sources"], "files": [], "errors": []}
    print(f"{'Verifying' if args.verify_only else 'Downloading/verifying'} {len(files)} files "
          f"({sum(e['size'] for e in files):,} bytes)", flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        pending = {pool.submit(fetch, e, args.verify_only): e for e in files}
        for i, future in enumerate(concurrent.futures.as_completed(pending), 1):
            entry = pending[future]
            try:
                result = future.result()
                report["files"].append(result)
                if i % 50 == 0 or "extract_to" in entry or i == len(files):
                    print(f"[{i}/{len(files)}] verified {entry['path']}", flush=True)
            except Exception as error:
                report["errors"].append({"path": entry["path"], "error": str(error)})
                print(f"ERROR {entry['path']}: {error}", flush=True)
    report["verified_at_utc"] = datetime.now(timezone.utc).isoformat()
    report["files"].sort(key=lambda e: e["path"])
    report["complete"] = not report["errors"] and len(report["files"]) == len(files)
    temporary = ROOT / "manifest.json.tmp"
    temporary.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    temporary.replace(ROOT / "manifest.json")
    print(f"Complete: {report['complete']}; verified {len(report['files'])}/{len(files)} files", flush=True)
    return 0 if report["complete"] else 1


if __name__ == "__main__":
    raise SystemExit(main())

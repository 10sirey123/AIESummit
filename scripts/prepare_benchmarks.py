"""Build numbered PDF/label pairs from already-downloaded benchmark sources.

Never downloads data or calls an AI service. Requires reportlab and pypdf.
"""

import argparse
from collections import defaultdict
import csv
from functools import lru_cache
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import unicodedata
from xml.sax.saxutils import escape

from pypdf import PdfReader
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas
from reportlab.platypus import Paragraph, SimpleDocTemplate, Spacer


ROOT = Path(__file__).resolve().parent.parent / "corporea"
if os.name == "nt":
    ROOT = Path("\\\\?\\" + str(ROOT))
OUTPUT = ROOT / "benchmarks"


def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(4 * 1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def relative(path):
    return path.relative_to(ROOT).as_posix()


@lru_cache(maxsize=None)
def source_sha(path):
    return sha(path)


def write_json(path, data):
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def render_pdf(path, text, dataset, sample_id):
    if "BenchmarkBody" not in pdfmetrics.getRegisteredFontNames():
        windows_font = Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts/arial.ttf"
        if windows_font.is_file():
            font = windows_font
        else:
            import reportlab
            font = Path(reportlab.__file__).parent / "fonts/Vera.ttf"
        pdfmetrics.registerFont(TTFont("BenchmarkBody", str(font)))
    style = ParagraphStyle("Contract", fontName="BenchmarkBody", fontSize=9.5,
                           leading=13.2, spaceAfter=6, splitLongWords=True)
    document = SimpleDocTemplate(str(path), pagesize=(612, 792),
                                 leftMargin=44, rightMargin=44,
                                 topMargin=38, bottomMargin=38,
                                 title=f"{dataset} {sample_id} - rendered source text",
                                 author="AIESummit benchmark preparation")
    story = []
    # The companion TXT preserves the exact source. Only presentation whitespace
    # and nonprinting control characters are normalized in the rendered PDF.
    clean = re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f\ufeff]", "", text)
    for block in re.split(r"\n\s*\n", clean.replace("\r\n", "\n")):
        words = block.split()
        # Bound individual flowables for very large unbroken source paragraphs.
        for start in range(0, len(words), 700):
            story.append(Paragraph(escape(" ".join(words[start:start + 700])), style))
        if words:
            story.append(Spacer(1, 3))
    if not story:
        raise ValueError(f"Empty source: {dataset}/{sample_id}")

    def footer(canvas, doc):
        canvas.saveState()
        canvas.setFont("Helvetica", 7)
        canvas.setFillColorRGB(0.4, 0.4, 0.4)
        canvas.drawString(44, 20, f"{dataset} {sample_id} - rendered from dataset text")
        canvas.drawRightString(568, 20, f"Page {doc.page}")
        canvas.restoreState()

    document.build(story, onFirstPage=footer, onLaterPages=footer,
                   canvasmaker=lambda *a, **k: Canvas(*a, **{**k, "invariant": 1}))


def make_pair(dataset, number, source, text, detections, source_metadata,
              original_pdf=None):
    sample_id = f"{number:04d}"
    folder = OUTPUT / dataset
    folder.mkdir(parents=True, exist_ok=True)
    pdf = folder / f"{sample_id}.pdf"
    labels = folder / f"{sample_id}.json"
    txt = folder / f"{sample_id}.txt"
    source_hash = source_sha(source)
    text_hash = hashlib.sha256(text.encode("utf-8")).hexdigest()
    status = "unlabeled" if detections is None else "gold"
    if labels.exists():
        previous = json.loads(labels.read_text(encoding="utf-8"))
        if (previous["source"]["sha256"] != source_hash
                or previous["expected_detections"] != detections
                or previous["annotation_status"] != status
                or not pdf.is_file() or not txt.is_file()
                or sha(pdf) != previous["document"]["sha256"]
                or sha(txt) != text_hash):
            raise ValueError(f"Existing pair differs from source; preserve/review it: {labels}")
        return previous
    if pdf.exists() or txt.exists():
        raise ValueError(f"Unfinished or unowned output requires review: {folder}/{sample_id}")
    temporary_pdf = pdf.with_suffix(".pdf.tmp")
    if original_pdf is not None:
        shutil.copyfile(original_pdf, temporary_pdf)
    else:
        render_pdf(temporary_pdf, text, dataset, sample_id)
    pages = len(PdfReader(str(temporary_pdf)).pages)
    if not pages:
        raise ValueError(f"PDF contains no pages: {temporary_pdf}")
    temporary_pdf.replace(pdf)
    txt.write_text(text, encoding="utf-8", newline="")
    record = {
        "schema_version": 1,
        "dataset": dataset,
        "sample_id": sample_id,
        "annotation_status": status,
        "document": {"file": pdf.name, "sha256": sha(pdf), "pages": pages,
                     "origin": "original_pdf" if original_pdf else "rendered_from_text"},
        "source": {"path": relative(source), "sha256": source_hash, **source_metadata},
        "reference_text": {"file": txt.name, "sha256": text_hash,
                           "offset_unit": "unicode_code_points",
                           "note": "Offsets refer to this text, never to PDF bytes or PDF pages."},
        "expected_detections": detections,
    }
    if original_pdf:
        record["source"]["original_pdf_path"] = relative(original_pdf)
        record["source"]["original_pdf_sha256"] = sha(original_pdf)
    write_json(labels, record)
    return record


def prepare_cuad(limit):
    source = ROOT / "cuad/raw/CUAD_v1/CUAD_v1.json"
    data = json.loads(source.read_text(encoding="utf-8"))["data"]
    norm = lambda value: unicodedata.normalize("NFKC", value).strip().casefold()
    pdfs = {norm(p.stem): p for p in (ROOT / "cuad/raw/CUAD_v1/full_contract_pdf").rglob("*") if p.is_file()}
    aliases = {
        "NETGEAR,INC_04_21_2003-EX-10.16-AMENDMENT TO THE DISTRIBUTOR AGREEMENT BETWEEN INGRAM MICRO AND NETGEAR":
            "NETGEAR,INC_04_21_2003-EX-10.16-AMENDMENT TO THE DISTRIBUTOR AGREEMENT BETWEEN INGRAM MICRO AND NETGEAR-",
        "HarpoonTherapeuticsInc_20200312_10-K_EX-10.18_12051356_EX-10.18_Development Agreement":
            "HarpoonTherapeuticsInc_20200312_10-K_EX-10.18_12051356_EX-10.18_Development Agreement_Option Agreement",
    }
    splits = {}
    for split, filename in [("train", "train_separate_questions.json"), ("test", "test.json")]:
        for item in json.loads((ROOT / "cuad/official-splits" / filename).read_text())["data"]:
            if item["title"] in splits:
                raise ValueError("CUAD split overlap")
            splits[item["title"]] = split
    records = []
    for number, item in enumerate(sorted(data, key=lambda d: norm(d["title"])), 1):
        if limit and number > limit:
            break
        paragraph, = item["paragraphs"]
        context = paragraph["context"]
        detections = []
        for qa in paragraph["qas"]:
            answers = []
            for answer in qa["answers"]:
                start = answer["answer_start"]
                end = start + len(answer["text"])
                if context[start:end] != answer["text"]:
                    raise ValueError(f"CUAD source span mismatch: {qa['id']}")
                answers.append({"text": answer["text"], "start": start, "end": end})
            detections.append({"category": qa["id"].rsplit("__", 1)[-1],
                               "question": qa["question"], "source_question_id": qa["id"],
                               "expected_present": not qa["is_impossible"], "answers": answers})
        title = aliases.get(item["title"], item["title"])
        pdf = pdfs[norm(title)]
        records.append(make_pair("cuad", number, source, context, detections,
                       {"title": item["title"], "upstream_split": splits[item["title"]],
                        "annotation_type": "clause_spans", "license": "CC-BY-4.0"}, pdf))
        if number % 50 == 0:
            print(f"CUAD: {number} pairs", flush=True)
    return records


def prepare_maud(limit):
    grouped = defaultdict(list)
    for source_csv in sorted((ROOT / "maud/raw/data").glob("MAUD_*.csv")):
        split = source_csv.stem.removeprefix("MAUD_")
        with source_csv.open(encoding="utf-8-sig", newline="") as source:
            for row_number, row in enumerate(csv.DictReader(source), 2):
                # Abridged text is edited context, and rare_answers are synthetic.
                # Only original main annotations belong to full-document labels.
                if row["data_type"] != "main":
                    continue
                grouped[row["contract_name"]].append({
                    "question": row["question"], "subquestion": row["subquestion"],
                    "category": row["category"], "text_type": row["text_type"],
                    "answer": row["answer"], "label": row["label"],
                    "source_excerpt": row["text"], "source_id": row["id"],
                    "source_csv": relative(source_csv), "source_csv_row": row_number,
                    "upstream_split": split, "offsets": None})
    records = []
    contracts = sorted((ROOT / "maud/raw/data/contracts").glob("*.txt"),
                       key=lambda p: int(p.stem.removeprefix("contract_")))
    for number, source in enumerate(contracts, 1):
        if limit and number > limit:
            break
        if not grouped[source.stem]:
            raise ValueError(f"No main MAUD annotations for {source.stem}")
        text = source.read_text(encoding="utf-8")
        records.append(make_pair("maud", number, source, text, grouped[source.stem],
                       {"contract_name": source.stem, "annotation_type": "deal_point_answers",
                        "license": "CC-BY-4.0", "offset_note":
                        "Upstream excerpts include page hints and formatting differences; no PDF page or text-offset mapping is asserted."}))
        if number % 10 == 0:
            print(f"MAUD: {number} pairs", flush=True)
    return records


def prepare_edgar(limit):
    count = limit or 100
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    years = sorted(Path(e["path"]).stem for e in manifest["files"]
                   if e["path"].startswith("edgar-corpus/archives/") and e["path"].endswith(".zip"))
    if not years:
        raise ValueError("No verified EDGAR archives")
    records = []
    for index, year in enumerate(years):
        quota = count // len(years) + (index < count % len(years))
        sources = sorted((ROOT / "edgar-corpus/raw" / year).glob("*.json"),
                         key=lambda p: hashlib.sha256(p.name.encode()).hexdigest())
        selected = 0
        for source in sources:
            if selected == quota:
                break
            item = json.loads(source.read_text(encoding="utf-8"))
            sections = [(key, value) for key, value in item.items()
                        if key.startswith("section_") and isinstance(value, str) and value.strip()]
            text = "\n\n".join(value for _, value in sections)
            if not text.strip():
                continue
            records.append(make_pair("edgar-corpus", len(records) + 1, source, text, None,
                           {"filename": item["filename"], "cik": item["cik"], "year": item["year"],
                            "annotation_type": "none", "license": "CC-BY-4.0",
                            "source_sections": [key for key, _ in sections],
                            "selection": "Equal year quotas, sorted by SHA-256 of original JSON filename; empty reports skipped.",
                            "label_note": "No upstream clause labels. Null detections mean unknown, not absence."}))
            selected += 1
            if len(records) % 10 == 0:
                print(f"EDGAR-CORPUS: {len(records)} pairs", flush=True)
        if selected != quota:
            raise ValueError(f"Not enough nonempty EDGAR reports in {year}")
    return records


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset", choices=["all", "cuad", "maud", "edgar-corpus"], default="all")
    parser.add_argument("--limit", type=int, help="Optional per-dataset cap; EDGAR defaults to 100")
    args = parser.parse_args()
    if args.limit is not None and args.limit < 1:
        parser.error("--limit must be positive")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    records = []
    for name, action in [("cuad", prepare_cuad), ("maud", prepare_maud), ("edgar-corpus", prepare_edgar)]:
        if args.dataset in ("all", name):
            result = action(args.limit)
            records.extend(result)
            print(f"{name}: {len(result)} verified pairs", flush=True)
    if args.dataset == "all":
        write_json(OUTPUT / "manifest.json", {
            "schema_version": 1, "documents": [
                {"dataset": r["dataset"], "sample_id": r["sample_id"],
                 "pdf": f"{r['dataset']}/{r['document']['file']}",
                 "labels": f"{r['dataset']}/{r['sample_id']}.json",
                 "annotation_status": r["annotation_status"],
                 "pdf_origin": r["document"]["origin"]} for r in records]})
    print(f"Prepared {len(records)} pairs without downloading or calling models.", flush=True)


if __name__ == "__main__":
    main()

"""Build the TP4 Word report and evidence figures from preserved test records.

Usage: python scripts/generate_tp4_report.py [--workspace-copy path.docx]
Dependencies: python-docx, Pillow. Run from any directory.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from docx import Document
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Inches, Pt, RGBColor
from docx.opc.constants import RELATIONSHIP_TYPE as RT

ROOT = Path(__file__).resolve().parents[1]
REPORT = ROOT / "docs" / "tp4"
EVIDENCE = REPORT / "evidencias"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def font(size: int, bold: bool = False):
    candidates = [Path("C:/Windows/Fonts") / ("consolab.ttf" if bold else "consola.ttf"),
                  Path("/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf")]
    for path in candidates:
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def panel(path: Path, title: str, subtitle: str, lines: list[tuple[str, str]], footer: str):
    width = 1500
    height = 190 + len(lines) * 51
    canvas = Image.new("RGB", (width, height), "#f7f9fc")
    draw = ImageDraw.Draw(canvas)
    draw.text((45, 28), title, fill="#172c48", font=font(33, True))
    draw.text((45, 84), subtitle, fill="#45546b", font=font(22))
    y = 142
    for content, color in lines:
        draw.text((45, y), content, fill=color, font=font(25))
        y += 51
    draw.text((45, height - 36), footer, fill="#59677a", font=font(17))
    canvas.save(path)


def build_figures():
    totals = []
    cases = []
    for path in sorted(EVIDENCE.glob("*.trx")):
        tree = ET.parse(path)
        counter = tree.find("t:ResultSummary/t:Counters", NS)
        if counter is None:
            raise ValueError(f"TRX without counters: {path}")
        total = int(counter.get("total", "0"))
        passed = int(counter.get("passed", "0"))
        if total != passed:
            raise ValueError(f"Local verification did not pass: {path}")
        kind = "Unitarias" if total == 24 else "Integración y E2E API"
        totals.append((kind, total))
        for result in tree.findall("t:Results/t:UnitTestResult", NS):
            cases.append((kind, result.get("testName"), result.get("outcome"), result.get("duration")))
    if sorted(total for _, total in totals) != [15, 24]:
        raise ValueError(f"Expected the two final local TRX files, found {totals}")
    with (EVIDENCE / "casos-ejecutados.csv").open("w", newline="", encoding="utf-8-sig") as stream:
        writer = csv.writer(stream)
        writer.writerow(["Nivel", "Caso", "Resultado", "Duración"])
        writer.writerows(cases)
    panel(EVIDENCE / "resultados-locales.png", "Pruebas locales verificadas", ".NET 8.0.34 | SDK 8.0.428 | 21 noviembre 2026", [
        ("Unitarias                 24 aprobadas / 24 ejecutadas", "#16633d"),
        ("Integración y E2E API      15 aprobadas / 15 ejecutadas", "#16633d"),
        ("Total                     39 aprobadas | 0 fallos | 0 omitidas", "#16633d"),
        ("Fuente                    Archivos TRX adjuntos a la entrega", "#33455e")
    ], "Visualización de registros originales; no es una captura de interfaz.")
    for filename, expected, title in [
        ("ci-exitosa", "success", "GitHub Actions con pruebas aprobadas"),
        ("ci-bloqueada", "failure", "GitHub Actions con despliegue bloqueado")
    ]:
        data = json.loads((EVIDENCE / f"{filename}.json").read_text(encoding="utf-8-sig"))
        if data["status"] != "completed" or data["conclusion"] != expected:
            raise ValueError(f"Unexpected workflow state: {filename}")
        test_job, build_job = data["jobs"]
        if expected == "failure":
            assert test_job["conclusion"] == "failure" and build_job["conclusion"] == "skipped"
            assert not build_job["steps"]
        lines = [(f"run-tests                 {test_job['conclusion']}", "#16633d" if expected == "success" else "#ad2626")]
        for step in test_job["steps"]:
            if step["name"] in {"Restaurar", "Compilar", "Ejecutar todas las pruebas"}:
                lines.append((f"  {step['name']:<30} {step['conclusion']}", "#33455e"))
        lines.append((f"build-and-deploy          {build_job['conclusion']}", "#16633d" if expected == "success" else "#ad2626"))
        lines.append(("Docker construido; Azure omitido por rama feature" if expected == "success" else "Docker y Azure sin ejecutar por dependencia needs", "#33455e"))
        panel(EVIDENCE / f"{filename}.png", title, f"Run {data['url'].rsplit('/', 1)[-1]} | Estado global: {expected}", lines,
              "Fuente: API de GitHub Actions. JSON y log originales incluidos.")


def inline(paragraph, text: str):
    pattern = r"\[([^\]]+)\]\(([^)]+)\)|`([^`]+)`|\*\*([^*]+)\*\*"
    cursor = 0
    for match in re.finditer(pattern, text):
        paragraph.add_run(text[cursor:match.start()])
        if match.group(1):
            hyperlink = OxmlElement("w:hyperlink")
            hyperlink.set(qn("r:id"), paragraph.part.relate_to(match.group(2), RT.HYPERLINK, is_external=True))
            run = OxmlElement("w:r")
            properties = OxmlElement("w:rPr")
            color = OxmlElement("w:color"); color.set(qn("w:val"), "245785"); properties.append(color)
            run.append(properties)
            value = OxmlElement("w:t"); value.text = match.group(1); run.append(value)
            hyperlink.append(run); paragraph._p.append(hyperlink)
        else:
            run = paragraph.add_run(match.group(3) or match.group(4))
            if match.group(3): run.font.name = "Consolas"; run.font.size = Pt(9)
            else: run.bold = True
        cursor = match.end()
    paragraph.add_run(text[cursor:])


def table(document, rows: list[list[str]]):
    result = document.add_table(rows=1, cols=len(rows[0]))
    result.alignment = WD_TABLE_ALIGNMENT.CENTER
    result.autofit = False
    widths = [0.54, 2.35, 3.91] if len(rows[0]) == 3 and rows[0][0] == "ID" else [1.15, 2.35, 3.30]
    if len(rows[0]) != 3: widths = [6.8 / len(rows[0])] * len(rows[0])
    for column, width in zip(result.columns, widths): column.width = Inches(width)
    for index, values in enumerate(rows):
        row = result.rows[0] if index == 0 else result.add_row()
        for cell, value, width in zip(row.cells, values, widths):
            cell.width = Inches(width)
            cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
            properties = cell._tc.get_or_add_tcPr()
            shade = OxmlElement("w:shd"); shade.set(qn("w:fill"), "203A58" if index == 0 else ("EFF3F7" if index % 2 == 0 else "FFFFFF")); properties.append(shade)
            margins = OxmlElement("w:tcMar")
            for direction in ("top", "bottom", "left", "right"):
                margin = OxmlElement("w:" + direction); margin.set(qn("w:w"), "90"); margin.set(qn("w:type"), "dxa"); margins.append(margin)
            properties.append(margins)
            p = cell.paragraphs[0]
            p.paragraph_format.space_after = Pt(0)
            p.paragraph_format.line_spacing = 1.02
            inline(p, value)
            for run in p.runs:
                run.font.size = Pt(10)
                if index == 0: run.bold = True; run.font.color.rgb = RGBColor.from_string("FFFFFF")
        if index == 0:
            repeat = OxmlElement("w:tblHeader"); row._tr.get_or_add_trPr().append(repeat)
    borders = OxmlElement("w:tblBorders")
    for direction in ("top", "left", "bottom", "right", "insideH", "insideV"):
        border = OxmlElement("w:" + direction); border.set(qn("w:val"), "single"); border.set(qn("w:sz"), "4"); border.set(qn("w:color"), "D9D9D9"); borders.append(border)
    result._tbl.tblPr.append(borders)
    document.add_paragraph().paragraph_format.space_after = Pt(2)


def build_document():
    # Screenshots are evidence: validate the original files before embedding them.
    capture_root = EVIDENCE / "capturas"
    manifest = json.loads((capture_root / "manifest.json").read_text(encoding="utf-8"))
    for capture in manifest:
        original = capture_root / capture["archivo"]
        if hashlib.sha256(original.read_bytes()).hexdigest() != capture["sha256"]:
            raise ValueError(f"Screenshot differs from capture manifest: {original}")
    document = Document()
    section = document.sections[0]
    section.page_width, section.page_height = Inches(8.5), Inches(11)
    section.top_margin = section.bottom_margin = Inches(0.7)
    section.left_margin = section.right_margin = Inches(0.85)
    normal = document.styles["Normal"]
    normal.font.name = "Calibri"; normal.font.size = Pt(11)
    normal.paragraph_format.line_spacing = 1.10
    normal.paragraph_format.space_after = Pt(8)
    for style, size in [("Title", 25), ("Heading 1", 17), ("Heading 2", 12)]:
        current = document.styles[style]
        current.font.name = "Calibri"; current.font.size = Pt(size)
        current.font.color.rgb = RGBColor.from_string("000000" if style == "Title" else "203A58")
        current.font.bold = style != "Title"
        current.paragraph_format.space_before = Pt(12 if style != "Title" else 0)
        current.paragraph_format.space_after = Pt(9)
    document.core_properties.title = "Resolución del trabajo práctico 4"
    document.core_properties.subject = "Testing automatizado de API .NET y quality gate CI/CD"
    document.core_properties.author = "Comisión 4K1 - Equipo TP4"
    document.core_properties.keywords = "TP4 xUnit Moq SQLite GitHub Actions"
    # Remove paragraph borders inherited from the default Word template.
    for border in list(document.styles.element.iter(qn("w:pBdr"))):
        border.getparent().remove(border)
    footer = section.footer.paragraphs[0]
    footer.alignment = WD_ALIGN_PARAGRAPH.RIGHT
    footer.add_run("TP4  |  ").font.size = Pt(9)
    field = OxmlElement("w:fldSimple"); field.set(qn("w:instr"), "PAGE"); footer._p.append(field)
    lines = (REPORT / "informe.md").read_text(encoding="utf-8").splitlines()
    index = 0
    while index < len(lines):
        line = lines[index].strip()
        if not line: index += 1; continue
        if line == "<!-- pagebreak -->": document.add_page_break()
        elif line.startswith("# "): document.add_paragraph(line[2:], "Title")
        elif line.startswith("## "): document.add_heading(line[3:], 1)
        elif line.startswith("### "): document.add_heading(line[4:], 2)
        elif line.startswith("!["):
            match = re.match(r"!\[([^\]]*)\]\(([^)]+)\)", line)
            picture = document.add_picture(str(REPORT / match.group(2)), width=Inches(6.8))
            picture._inline.docPr.set("descr", match.group(1))
        elif line.startswith("|"):
            rows = []
            while index < len(lines) and lines[index].strip().startswith("|"):
                values = [item.strip() for item in lines[index].strip().strip("|").split("|")]
                if not all(re.fullmatch(r":?-+:?", item) for item in values): rows.append(values)
                index += 1
            table(document, rows)
            continue
        elif line.startswith("```"):
            index += 1
            while index < len(lines) and not lines[index].startswith("```"):
                p = document.add_paragraph()
                p.paragraph_format.space_after = Pt(3)
                run = p.add_run(lines[index]); run.font.name = "Consolas"; run.font.size = Pt(9)
                index += 1
        else:
            p = document.add_paragraph()
            inline(p, line)
        index += 1
    output = REPORT / "resolucion-tp4.docx"
    for border in list(document.element.iter(qn("w:pBdr"))):
        border.getparent().remove(border)
    document.save(output)
    return output


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace-copy", type=Path)
    args = parser.parse_args()
    build_figures()
    result = build_document()
    if args.workspace_copy:
        shutil.copy2(result, args.workspace_copy)
    print(result)

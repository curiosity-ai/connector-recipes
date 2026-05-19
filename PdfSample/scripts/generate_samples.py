#!/usr/bin/env python3
"""Generate sample industrial maintenance manuals + sidecar JSON metadata.

Produces a small but realistic corpus the PDF connector recipe can ingest:
each manual covers one piece of equipment (centrifugal pump, AC motor, etc.),
documents several maintenance procedures, references parts and manufacturers,
and lists technicians who authored / perform the procedures. Cross-references
between manuals (same parts, same manufacturers, shared technicians) give
the ingestion a real graph to build.

Dependencies:
    pip install reportlab

Usage:
    python scripts/generate_samples.py                     # writes data/manuals/
    python scripts/generate_samples.py --out path/manuals  # writes elsewhere
"""

import argparse
import json
import os
import textwrap
from pathlib import Path

from reportlab.lib.pagesizes import LETTER
from reportlab.lib.units import inch
from reportlab.pdfgen import canvas


# ---------- shared catalog of parts, manufacturers, technicians ----------

PART_CATALOG = {
    "BRG-6206-2RS":   ("Sealed Deep-Groove Ball Bearing 30mm",  "Bearings"),
    "BRG-7308BEP":    ("Angular Contact Bearing 40mm",          "Bearings"),
    "SEAL-MS-25-A":   ("Mechanical Shaft Seal 25mm Type A",     "Seals"),
    "SEAL-LIP-32":    ("Lip Seal 32mm Nitrile",                 "Seals"),
    "GSKT-ANSI-150":  ("ANSI 150 Spiral-Wound Gasket DN50",     "Gaskets"),
    "OIL-ISO-VG46":   ("ISO VG46 Hydraulic Oil 20L",            "Lubricants"),
    "GRSE-LITH-EP2":  ("Lithium EP2 Grease 400g Cartridge",     "Lubricants"),
    "FLT-HYD-10MIC":  ("Hydraulic Return-Line Filter 10 micron","Filters"),
    "CPLG-FLEX-32":   ("Flexible Jaw Coupling 32mm",            "Couplings"),
    "ROTOR-WND-460V": ("Replacement Wound Rotor 460V",          "Electrical"),
    "BRSH-CRBN-12":   ("Carbon Motor Brush 12mm Pair",          "Electrical"),
    "BLT-M16-HEX":    ("Hex Bolt M16 x 60 Grade 8.8",           "Fasteners"),
    "BLT-M8-FLG":     ("Flanged Bolt M8 x 30 Stainless",        "Fasteners"),
    "VLV-CHK-DN50":   ("DN50 Swing Check Valve",                "Valves"),
    "PR-GAUGE-10BAR": ("Bourdon Pressure Gauge 0-10 bar",       "Instrumentation"),
    "VFD-FUSE-32A":   ("VFD Input Fuse Class J 32A",            "Electrical"),
    "BELT-V-A56":     ("Classic V-Belt A56",                    "Drives"),
    "IMPLR-CAST-200": ("Cast Stainless Impeller 200mm",         "Wet End"),
}

MANUFACTURERS = {
    "Sulzer":    "Switzerland",
    "Grundfos":  "Denmark",
    "WEG":       "Brazil",
    "ABB":       "Switzerland",
    "Siemens":   "Germany",
    "Flowserve": "USA",
    "Atlas Copco": "Sweden",
    "Festo":     "Germany",
}

TECHNICIANS = {
    "T-1001": ("Jordan Reyes",  "Rotating Equipment"),
    "T-1042": ("Priya Subbu",   "Electrical"),
    "T-1117": ("Marek Janowski","Hydraulics"),
    "T-1208": ("Aisha Kone",    "Reliability Engineering"),
    "T-1290": ("Henrik Sand",   "Instrumentation"),
}


# ---------- manual definitions ----------

MANUALS = [
    {
        "documentNumber": "MAN-PMP-001",
        "title":          "Sulzer AHLSTAR A22-50 Centrifugal Pump — Maintenance Manual",
        "revision":       "Rev. C",
        "publishedAt":    "2024-03-12T00:00:00Z",
        "equipment": {
            "id":           "EQ-PMP-A22-50-01",
            "model":        "AHLSTAR A22-50",
            "category":     "Centrifugal Pump",
            "serialNumber": "AH-2023-44129",
            "plant":        "North Refinery Unit 4",
        },
        "manufacturer": ("Sulzer", "Switzerland"),
        "authors":      ["T-1001", "T-1208"],
        "hazards":      ["Stored mechanical energy", "Hot surfaces (process side)", "Pinch points"],
        "procedures": [
            {
                "id":        "PRC-PMP-001-A",
                "name":      "Replace mechanical shaft seal",
                "category":  "Corrective Maintenance",
                "severity":  "Medium",
                "page":      2,
                "heading":   "Section 4 — Seal Replacement",
                "performedBy": "T-1001",
                "parts":     ["SEAL-MS-25-A", "GSKT-ANSI-150", "BLT-M16-HEX"],
            },
            {
                "id":        "PRC-PMP-001-B",
                "name":      "Quarterly bearing lubrication",
                "category":  "Preventive Maintenance",
                "severity":  "Low",
                "page":      3,
                "heading":   "Section 5 — Lubrication Schedule",
                "performedBy": "T-1001",
                "parts":     ["BRG-6206-2RS", "GRSE-LITH-EP2"],
            },
            {
                "id":        "PRC-PMP-001-C",
                "name":      "Impeller balance and run-out check",
                "category":  "Reliability Inspection",
                "severity":  "Medium",
                "page":      4,
                "heading":   "Section 6 — Impeller Inspection",
                "performedBy": "T-1208",
                "parts":     ["IMPLR-CAST-200"],
            },
        ],
    },
    {
        "documentNumber": "MAN-MTR-002",
        "title":          "WEG W22 75kW Three-Phase Induction Motor — Service Manual",
        "revision":       "Rev. B",
        "publishedAt":    "2023-11-08T00:00:00Z",
        "equipment": {
            "id":           "EQ-MTR-W22-75-04",
            "model":        "W22 IE3 75kW 4P",
            "category":     "AC Induction Motor",
            "serialNumber": "WEG-22-99041-A",
            "plant":        "North Refinery Unit 4",
        },
        "manufacturer": ("WEG", "Brazil"),
        "authors":      ["T-1042", "T-1208"],
        "hazards":      ["Electrical shock (460V)", "Arc flash", "Suspended load during lift"],
        "procedures": [
            {
                "id":        "PRC-MTR-002-A",
                "name":      "Replace bearings (drive and non-drive end)",
                "category":  "Corrective Maintenance",
                "severity":  "High",
                "page":      2,
                "heading":   "Section 3 — Bearing Replacement",
                "performedBy": "T-1042",
                "parts":     ["BRG-7308BEP", "BRG-6206-2RS", "SEAL-LIP-32"],
            },
            {
                "id":        "PRC-MTR-002-B",
                "name":      "Megger insulation resistance test",
                "category":  "Predictive Maintenance",
                "severity":  "Low",
                "page":      3,
                "heading":   "Section 4 — Electrical Testing",
                "performedBy": "T-1042",
                "parts":     ["BRSH-CRBN-12"],
            },
            {
                "id":        "PRC-MTR-002-C",
                "name":      "Coupling alignment to driven pump",
                "category":  "Reliability Inspection",
                "severity":  "Medium",
                "page":      4,
                "heading":   "Section 5 — Alignment Procedure",
                "performedBy": "T-1208",
                "parts":     ["CPLG-FLEX-32", "BLT-M8-FLG"],
            },
        ],
    },
    {
        "documentNumber": "MAN-VFD-003",
        "title":          "ABB ACS580 VFD 75kW — Installation and Repair Guide",
        "revision":       "Rev. A",
        "publishedAt":    "2024-01-22T00:00:00Z",
        "equipment": {
            "id":           "EQ-VFD-ACS580-04",
            "model":        "ACS580-01-145A-4",
            "category":     "Variable Frequency Drive",
            "serialNumber": "ABB-580-77321",
            "plant":        "North Refinery Unit 4",
        },
        "manufacturer": ("ABB", "Switzerland"),
        "authors":      ["T-1042", "T-1290"],
        "hazards":      ["Capacitor discharge", "Arc flash", "DC bus residual voltage"],
        "procedures": [
            {
                "id":        "PRC-VFD-003-A",
                "name":      "Replace input fuses after fault trip",
                "category":  "Corrective Maintenance",
                "severity":  "High",
                "page":      2,
                "heading":   "Section 3 — Fuse Replacement",
                "performedBy": "T-1042",
                "parts":     ["VFD-FUSE-32A"],
            },
            {
                "id":        "PRC-VFD-003-B",
                "name":      "Calibrate output current measurement",
                "category":  "Predictive Maintenance",
                "severity":  "Low",
                "page":      3,
                "heading":   "Section 4 — Calibration",
                "performedBy": "T-1290",
                "parts":     ["PR-GAUGE-10BAR"],
            },
        ],
    },
    {
        "documentNumber": "MAN-HYD-004",
        "title":          "Atlas Copco GA75 Compressor Hydraulic Subsystem — Maintenance Manual",
        "revision":       "Rev. D",
        "publishedAt":    "2024-05-30T00:00:00Z",
        "equipment": {
            "id":           "EQ-CMP-GA75-02",
            "model":        "GA75 VSD+",
            "category":     "Rotary Screw Compressor",
            "serialNumber": "AC-GA75-55021",
            "plant":        "South Plant Utilities",
        },
        "manufacturer": ("Atlas Copco", "Sweden"),
        "authors":      ["T-1117", "T-1001"],
        "hazards":      ["High-pressure oil", "Hot surfaces", "Stored hydraulic energy"],
        "procedures": [
            {
                "id":        "PRC-HYD-004-A",
                "name":      "Replace return-line filter element",
                "category":  "Preventive Maintenance",
                "severity":  "Low",
                "page":      2,
                "heading":   "Section 4 — Hydraulic Filter Service",
                "performedBy": "T-1117",
                "parts":     ["FLT-HYD-10MIC", "GSKT-ANSI-150"],
            },
            {
                "id":        "PRC-HYD-004-B",
                "name":      "Drain and refill hydraulic reservoir",
                "category":  "Preventive Maintenance",
                "severity":  "Medium",
                "page":      3,
                "heading":   "Section 5 — Oil Change",
                "performedBy": "T-1117",
                "parts":     ["OIL-ISO-VG46"],
            },
            {
                "id":        "PRC-HYD-004-C",
                "name":      "Inspect and lap check valve seat",
                "category":  "Corrective Maintenance",
                "severity":  "Medium",
                "page":      4,
                "heading":   "Section 6 — Check-Valve Service",
                "performedBy": "T-1001",
                "parts":     ["VLV-CHK-DN50"],
            },
        ],
    },
    {
        "documentNumber": "MAN-PMP-005",
        "title":          "Grundfos CR45-3 Vertical Multistage Pump — Repair Manual",
        "revision":       "Rev. A",
        "publishedAt":    "2024-07-04T00:00:00Z",
        "equipment": {
            "id":           "EQ-PMP-CR45-01",
            "model":        "CR45-3-2 A-F-A-E-HQQE",
            "category":     "Vertical Multistage Pump",
            "serialNumber": "GF-CR45-30022",
            "plant":        "South Plant Utilities",
        },
        "manufacturer": ("Grundfos", "Denmark"),
        "authors":      ["T-1001", "T-1208"],
        "hazards":      ["High-pressure water", "Pinch points", "Suspended load during lift"],
        "procedures": [
            {
                "id":        "PRC-PMP-005-A",
                "name":      "Replace stage chamber gaskets",
                "category":  "Corrective Maintenance",
                "severity":  "Medium",
                "page":      2,
                "heading":   "Section 3 — Stage Disassembly",
                "performedBy": "T-1001",
                "parts":     ["GSKT-ANSI-150", "SEAL-MS-25-A"],
            },
            {
                "id":        "PRC-PMP-005-B",
                "name":      "V-belt drive replacement",
                "category":  "Preventive Maintenance",
                "severity":  "Low",
                "page":      3,
                "heading":   "Section 4 — Belt Service",
                "performedBy": "T-1208",
                "parts":     ["BELT-V-A56"],
            },
        ],
    },
]


# ---------- PDF + JSON writers ----------

def part_blurb(part_id):
    name, category = PART_CATALOG[part_id]
    return f"{part_id}  —  {name}  ({category})"


def draw_page(c, title, body_lines):
    width, height = LETTER
    c.setFont("Helvetica-Bold", 14)
    c.drawString(1 * inch, height - 1 * inch, title)
    c.setFont("Helvetica", 10)
    y = height - 1.4 * inch
    for line in body_lines:
        for wrapped in textwrap.wrap(line, width=95) or [""]:
            c.drawString(1 * inch, y, wrapped)
            y -= 0.18 * inch
            if y < 1 * inch:
                c.showPage()
                c.setFont("Helvetica", 10)
                y = height - 1 * inch
    c.showPage()


def write_pdf(manual, out_pdf):
    c = canvas.Canvas(str(out_pdf), pagesize=LETTER)

    eq = manual["equipment"]
    mfg_name, mfg_country = manual["manufacturer"]

    # Page 1: cover + overview
    draw_page(c,
        manual["title"],
        [
            f"Document number: {manual['documentNumber']}    Revision: {manual['revision']}",
            f"Published: {manual['publishedAt']}",
            "",
            "EQUIPMENT",
            f"  Tag: {eq['id']}    Plant: {eq['plant']}",
            f"  Model: {eq['model']}    Category: {eq['category']}",
            f"  Serial: {eq['serialNumber']}",
            f"  Manufacturer: {mfg_name} ({mfg_country})",
            "",
            "SAFETY HAZARDS",
            *[f"  - {h}" for h in manual["hazards"]],
            "",
            "AUTHORS",
            *[f"  - {tid}  {TECHNICIANS[tid][0]}  ({TECHNICIANS[tid][1]})"
              for tid in manual["authors"]],
            "",
            "CONTENTS",
            *[f"  {p['page']:>2}  {p['heading']}" for p in manual["procedures"]],
        ])

    # One page per procedure
    for proc in manual["procedures"]:
        tech_id   = proc["performedBy"]
        tech_name, tech_trade = TECHNICIANS[tech_id]
        body = [
            f"Procedure ID: {proc['id']}",
            f"Category: {proc['category']}    Severity: {proc['severity']}",
            f"Performed by: {tech_id}  {tech_name}  ({tech_trade})",
            "",
            "OVERVIEW",
            f"This procedure covers {proc['name'].lower()} on equipment {eq['id']} "
            f"({eq['model']}). Follow the safety hazards listed on page 1 before "
            f"starting. Lock out and tag out upstream isolation before disassembly.",
            "",
            "PARTS REQUIRED",
            *[f"  - {part_blurb(p)}" for p in proc["parts"]],
            "",
            "STEPS",
            f"  1. Confirm isolation per plant LOTO procedure. Verify zero energy state.",
            f"  2. Stage the parts listed above plus a calibrated torque wrench and clean rags.",
            f"  3. Disassemble per Section {proc['heading'].split('—')[0].strip().split()[-1]} of this manual.",
            f"  4. Inspect mating surfaces for wear, scoring, or corrosion. Photograph any anomalies.",
            f"  5. Install replacement parts in the order listed. Torque fasteners per Table 2.",
            f"  6. Reassemble and reinstate auxiliaries (lubrication, cooling, instrumentation).",
            f"  7. Remove isolation and perform a no-load function test.",
            f"  8. Record completion against work order in CMMS.",
            "",
            "ACCEPTANCE CRITERIA",
            "  - No abnormal vibration or noise during no-load run.",
            "  - All fasteners torqued and witness-marked.",
            "  - No leaks at static or dynamic seals after a 5-minute run.",
        ]
        draw_page(c, proc["heading"], body)

    c.save()


def write_metadata(manual, out_json):
    eq = manual["equipment"]
    mfg_name, mfg_country = manual["manufacturer"]

    doc = {
        "documentNumber": manual["documentNumber"],
        "title":          manual["title"],
        "revision":       manual["revision"],
        "publishedAt":    manual["publishedAt"],
        "equipment":      eq,
        "manufacturer":   { "name": mfg_name, "country": mfg_country },
        "authors": [
            {
                "employeeId": tid,
                "name":       TECHNICIANS[tid][0],
                "trade":      TECHNICIANS[tid][1],
            }
            for tid in manual["authors"]
        ],
        "hazards":   manual["hazards"],
        "procedures": [
            {
                "id":          p["id"],
                "name":        p["name"],
                "category":    p["category"],
                "severity":    p["severity"],
                "page":        p["page"],
                "heading":     p["heading"],
                "performedBy": p["performedBy"],
                "parts": [
                    {
                        "partNumber": pid,
                        "name":       PART_CATALOG[pid][0],
                        "category":   PART_CATALOG[pid][1],
                    }
                    for pid in p["parts"]
                ],
            }
            for p in manual["procedures"]
        ],
    }

    out_json.write_text(json.dumps(doc, indent=2), encoding="utf-8")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=None, help="Output directory (default: ../data/manuals)")
    args = ap.parse_args()

    out = Path(args.out) if args.out else Path(__file__).parent.parent / "data" / "manuals"
    out.mkdir(parents=True, exist_ok=True)

    for manual in MANUALS:
        stem = manual["documentNumber"]
        write_pdf(manual,      out / f"{stem}.pdf")
        write_metadata(manual, out / f"{stem}.json")
        print(f"wrote {stem}.pdf + {stem}.json")

    print(f"\n{len(MANUALS)} manuals written to {out}")


if __name__ == "__main__":
    main()

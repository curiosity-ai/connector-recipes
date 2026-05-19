#!/usr/bin/env python3
"""Generate sample Parquet + Avro files of course grades.

A small but realistic columnar dataset the Parquet connector recipe can
ingest. Student IDs, course codes, subjects, and terms all match the
existing CSV / REST / Kafka recipes so the resulting graph merges with the
rest of the workspace.

Dependencies:
    pip install pandas pyarrow fastavro

Usage:
    python scripts/generate_samples.py                     # writes data/
    python scripts/generate_samples.py --rows 5000         # bigger sample
"""

import argparse
import os
import random
from pathlib import Path

import pandas as pd
import pyarrow as pa
import pyarrow.parquet as pq
import fastavro


COURSES = [
    ("MATH101", "Calculus",          4, "Fall 2024"),
    ("MATH101", "Calculus",          4, "Spring 2025"),
    ("MATH210", "Linear Algebra",    3, "Fall 2024"),
    ("CS210",   "Data Structures",   4, "Fall 2024"),
    ("CS344",   "Machine Learning",  4, "Spring 2025"),
    ("CS544",   "Deep Learning",     4, "Spring 2025"),
    ("STAT220", "Statistics",        3, "Fall 2024"),
    ("STAT220", "Statistics",        3, "Spring 2025"),
    ("ECON201", "Microeconomics",    3, "Fall 2024"),
    ("PHIL110", "Logic",             3, "Spring 2025"),
    ("PHYS221", "Quantum Mechanics", 4, "Fall 2024"),
    ("BIO150",  "Molecular Biology", 4, "Spring 2025"),
    ("CS660",   "Distributed Systems", 4, "Fall 2024"),
]

LETTER_TO_POINTS = {
    "A":  4.0, "A-": 3.7,
    "B+": 3.3, "B":  3.0, "B-": 2.7,
    "C+": 2.3, "C":  2.0, "C-": 1.7,
    "D":  1.0, "F":  0.0,
}
LETTERS = list(LETTER_TO_POINTS.keys())
# Skewed toward As and Bs the way real transcripts are.
LETTER_WEIGHTS = [30, 22, 14, 10, 7, 5, 4, 3, 2, 2, 1]


def make_rows(n_students, seed):
    rng = random.Random(seed)
    rows = []
    for s in range(1, n_students + 1):
        student_id = f"S{s:03d}"
        # Each student takes 4-7 courses across both terms.
        n = rng.randint(4, 7)
        for code, subject, credits, term in rng.sample(COURSES, n):
            letter = rng.choices(LETTERS, weights=LETTER_WEIGHTS[: len(LETTERS)])[0]
            rows.append({
                "student_id":   student_id,
                "course_code":  code,
                "subject":      subject,
                "term":         term,
                "letter_grade": letter,
                "gpa_points":   LETTER_TO_POINTS[letter],
                "credit_hours": credits,
            })
    return rows


def write_parquet(rows, path):
    df    = pd.DataFrame(rows)
    table = pa.Table.from_pandas(df, preserve_index=False)
    pq.write_table(table, path, compression="snappy", row_group_size=500)
    print(f"wrote {path}  ({len(rows)} rows, {os.path.getsize(path)} bytes)")


AVRO_SCHEMA = {
    "type": "record",
    "name": "Grade",
    "fields": [
        {"name": "student_id",   "type": "string"},
        {"name": "course_code",  "type": "string"},
        {"name": "subject",      "type": "string"},
        {"name": "term",         "type": "string"},
        {"name": "letter_grade", "type": "string"},
        {"name": "gpa_points",   "type": "double"},
        {"name": "credit_hours", "type": "int"},
    ],
}


def write_avro(rows, path):
    parsed = fastavro.parse_schema(AVRO_SCHEMA)
    with open(path, "wb") as f:
        fastavro.writer(f, parsed, rows, codec="deflate")
    print(f"wrote {path}  ({len(rows)} rows, {os.path.getsize(path)} bytes)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=None, help="Output dir (default: ../data)")
    ap.add_argument("--rows", type=int, default=30, help="Number of students (default 30)")
    ap.add_argument("--seed", type=int, default=42, help="RNG seed (default 42)")
    args = ap.parse_args()

    out = Path(args.out) if args.out else Path(__file__).parent.parent / "data"
    out.mkdir(parents=True, exist_ok=True)

    rows = make_rows(args.rows, args.seed)
    write_parquet(rows, str(out / "grades.parquet"))
    write_avro   (rows, str(out / "grades.avro"))


if __name__ == "__main__":
    main()

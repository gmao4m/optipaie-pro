#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check-0028-dataloss.py  —  Forensic answer to ONE question:

    Did the OLD performance/evaluation tables (removed by migration 0028) ever
    contain data on THIS customer's installation?

Migration 0028 (PerformanceRebuild) DROPs the old evaluation module's tables
without copying their rows. If a customer had entered evaluations on the old
module before 0028 ran, that data is gone. This script looks for any surviving
trace so the owner can decide, instead of guessing.

It is READ-ONLY (opens every database with mode=ro) and modifies nothing.

Usage:
    python check-0028-dataloss.py  "C:\\path\\to\\optipaie.db"
    python check-0028-dataloss.py  "C:\\path\\to\\optipaie.db"  --backups "C:\\path\\to\\backups"

If --backups is omitted it also tries the "backups" folder next to the database.
Python 3 only (uses the standard-library sqlite3 module — nothing to install).
"""

import os
import sys
import glob
import argparse
import sqlite3

# The tables the OLD evaluation module owned (migrations 0014 / 0022 / 0027),
# dropped by 0028. Any of these holding rows == pre-0028 evaluation data existed.
OLD_TABLES = [
    "PerformanceReviews", "PerformanceCycles", "PerformanceGoals",
    "PerformanceGoalTemplates", "PerformanceCareerEvents", "PerformanceCriteria",
    "PerformanceTemplates", "PerformanceTemplateCriteria", "PerformanceDeptSettings",
]


def open_ro(path):
    uri = "file:" + path.replace("?", "%3f").replace("#", "%23") + "?mode=ro"
    return sqlite3.connect(uri, uri=True)


def table_exists(con, name):
    cur = con.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (name,))
    return cur.fetchone() is not None


def count_rows(con, name):
    try:
        return con.execute("SELECT COUNT(*) FROM \"%s\"" % name).fetchone()[0]
    except sqlite3.Error:
        return None


def migration_info(con):
    applied28, maxver = None, None
    if table_exists(con, "SchemaMigrations"):
        row = con.execute("SELECT AppliedAtUtc FROM SchemaMigrations WHERE Version=28").fetchone()
        applied28 = row[0] if row else None
        row = con.execute("SELECT MAX(Version) FROM SchemaMigrations").fetchone()
        maxver = row[0] if row else None
    return applied28, maxver


def scan(path, label):
    print("\n--- %s ---" % label)
    print("    file: %s" % path)
    if not os.path.isfile(path):
        print("    (introuvable)")
        return False
    found_data = False
    try:
        con = open_ro(path)
    except sqlite3.Error as e:
        print("    ERREUR d'ouverture: %s" % e)
        return False
    try:
        applied28, maxver = migration_info(con)
        print("    version de schema max = %s ; migration 0028 appliquee le = %s"
              % (maxver, applied28 if applied28 else "NON (0028 pas encore appliquee)"))
        present = [t for t in OLD_TABLES if table_exists(con, t)]
        if not present:
            print("    aucune ancienne table d'evaluation presente (supprimees par 0028, ou jamais creees).")
        for t in present:
            n = count_rows(con, t)
            flag = "  <<< DONNEES PRESENTES" if (n or 0) > 0 else ""
            print("    %-28s rows = %s%s" % (t, n, flag))
            if (n or 0) > 0:
                found_data = True
    finally:
        con.close()
    return found_data


def main():
    ap = argparse.ArgumentParser(description="Forensic 0028 data-loss check (read-only).")
    ap.add_argument("db", help="chemin de la base optipaie.db du client")
    ap.add_argument("--backups", help="dossier des sauvegardes (.db) a scanner aussi")
    args = ap.parse_args()

    print("=" * 72)
    print("  Verification 0028 : les anciennes tables d'evaluation avaient-elles des donnees ?")
    print("=" * 72)

    any_data = scan(args.db, "Base de donnees actuelle")

    backups_dir = args.backups or os.path.join(os.path.dirname(os.path.abspath(args.db)), "backups")
    if os.path.isdir(backups_dir):
        files = sorted(glob.glob(os.path.join(backups_dir, "*.db")))
        if not files:
            print("\n(aucun fichier .db dans le dossier de sauvegardes: %s)" % backups_dir)
        for f in files:
            if scan(f, "Sauvegarde"):
                any_data = True
    else:
        print("\n(dossier de sauvegardes introuvable: %s)" % backups_dir)

    print("\n" + "=" * 72)
    if any_data:
        print("  VERDICT : des DONNEES d'evaluation existaient (voir lignes marquees ci-dessus).")
        print("            La perte via 0028 est donc REELLE pour ce client.")
    else:
        print("  VERDICT : aucune trace de donnees d'evaluation anciennes dans les fichiers scannes.")
        print("            NB: si 0028 a deja supprime les tables et qu'AUCUNE sauvegarde ANTERIEURE")
        print("            a sa date d'application n'a ete scannee, l'absence n'est pas une preuve.")
        print("            Fournir une sauvegarde datant d'AVANT la date 0028 ci-dessus pour conclure.")
    print("=" * 72)
    return 0


if __name__ == "__main__":
    sys.exit(main())

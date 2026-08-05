#!/usr/bin/env python3
"""
Real end-to-end activation test for OptiPaie PRO — run AFTER deploying the Edge
Functions. It calls the live `activate` function with a license key exactly like the
desktop app does, then decodes the returned Ed25519-signed token and shows what the
client will see: the company scope (Mono vs Multi), whether every feature is unlocked,
the duration, and the offline grace window.

Usage:
    python test-activation.py PAY-XXXX-XXXX-XXXX
    python test-activation.py PAY-XXXX-XXXX-XXXX  --device my-test-device

Generate the two keys first in "OptiPaie PRO Admin" (one Mono, one Multi), then run
this once per key. A green ✅ line means activation works end to end.
"""
import argparse
import base64
import json
import os
import sys
import urllib.request
import urllib.error

# Public project values (safe to embed — anon/publishable key + project URL).
BASE_URL = os.environ.get("OPTIPAIE_BASE_URL", "https://bajiomgtkpdqyvgpigsc.supabase.co/functions/v1")
ANON_KEY = os.environ.get("OPTIPAIE_ANON_KEY", "sb_publishable_AAbnVY311vyP8hfD5WcEUg_0RDiYqxb")


def b64url_decode(segment: str) -> bytes:
    return base64.urlsafe_b64decode(segment + "=" * (-len(segment) % 4))


def activate(key: str, device: str):
    body = json.dumps({
        "productKey": "payroll",
        "licenseKey": key.strip().upper(),
        "deviceId": device,
        "companyName": "Test Activation",
        "email": "test-activation@optipaie.dz",
        "appVersion": "1.16.0",
    }).encode("utf-8")

    req = urllib.request.Request(
        BASE_URL.rstrip("/") + "/activate",
        data=body,
        headers={"Content-Type": "application/json", "apikey": ANON_KEY,
                 "Authorization": "Bearer " + ANON_KEY},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return resp.getcode(), json.load(resp)
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", "replace")
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, {"raw": raw}
    except urllib.error.URLError as e:
        return 0, {"error": "network", "message": str(e)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("key", help="license key, e.g. PAY-XXXX-XXXX-XXXX")
    ap.add_argument("--device", default="test-device-" + base64.urlsafe_b64encode(os.urandom(6)).decode().rstrip("="))
    args = ap.parse_args()

    print(f"→ activate {args.key}  (device {args.device})")
    code, data = activate(args.key, args.device)

    if code == 404:
        print("❌ HTTP 404 — the Edge Functions are NOT deployed yet. Deploy activate/validate first.")
        sys.exit(2)
    if code != 200 or "token" not in data:
        print(f"❌ activation failed (HTTP {code}): {data.get('error') or data}")
        print(f"   message: {data.get('message', '')}")
        sys.exit(1)

    payload = json.loads(b64url_decode(data["token"].split(".")[0]))
    mc = payload.get("maxCompanies")
    scope = "Multi-sociétés (illimité)" if mc == 0 else ("Mono-société (1 entreprise)" if (mc is None or mc == 1) else f"{mc} sociétés")
    modules = payload.get("modules", [])

    print("✅ ACTIVATION OK")
    print(f"   société    : {payload.get('companyName')}")
    print(f"   type/portée: {scope}   (maxCompanies={mc})")
    print(f"   durée      : {payload.get('type')}   expiresAt={payload.get('expiresAt') or 'jamais (permanente)'}")
    print(f"   modules    : {len(modules)} débloqués  →  {', '.join(modules) if modules else '(aucun)'}")
    print(f"   statut     : {payload.get('status')}   graceUntil={payload.get('graceUntil')}")
    print("   (le client stockera ce jeton chiffré et fonctionnera hors-ligne ensuite)")


if __name__ == "__main__":
    main()

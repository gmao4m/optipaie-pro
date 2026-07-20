// Generates the Ed25519 signing keypair for license tokens.
//
//   Run:  deno run backend/scripts/generate-keypair.ts
//
//   - PRIVATE key  -> Supabase secret  LICENSE_SIGNING_PRIVATE_KEY
//                     (backend/README.md step 4). NEVER commit it, never ship it.
//   - PUBLIC  key  -> embed in the desktop app (Phase 2) to verify tokens offline.
//
// Keep the private key safe: if you keep the SAME keypair when migrating away
// from Supabase, every already-activated customer keeps working with no re-issue.

import * as ed from "https://esm.sh/@noble/ed25519@2.1.0";
import { sha512 } from "https://esm.sh/@noble/hashes@1.4.0/sha512";

ed.etc.sha512Sync = (...m: Uint8Array[]) => sha512(ed.etc.concatBytes(...m));

const priv = ed.utils.randomPrivateKey();
const pub = ed.getPublicKey(priv);

console.log("=== OptiPaie DZ license signing keypair (Ed25519) ===\n");
console.log("PRIVATE KEY (hex)  -> Supabase secret LICENSE_SIGNING_PRIVATE_KEY:");
console.log("  " + ed.etc.bytesToHex(priv) + "\n");
console.log("PUBLIC KEY (hex)   -> embed in the desktop app (Phase 2):");
console.log("  " + ed.etc.bytesToHex(pub) + "\n");
console.log("Store the private key somewhere safe (password manager). Do NOT commit it.");

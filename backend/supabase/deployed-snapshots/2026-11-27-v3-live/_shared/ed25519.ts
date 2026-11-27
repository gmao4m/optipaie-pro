// Ed25519 signing for license tokens.
//
// Token format (compact, JWT-like but Ed25519):
//     base64url(payloadJson) + "." + base64url(signature)
//
// The signature is computed over the ASCII bytes of the base64url(payloadJson)
// string — NOT over re-serialised JSON. This avoids any JSON-canonicalisation
// mismatch between this Deno signer and the .NET (BouncyCastle) verifier: the
// desktop verifies the signature over the exact left-hand string it receives,
// then base64url-decodes the payload for reading. Tamper with the payload and
// the signature no longer matches → the desktop rejects the license.

import * as ed from "https://esm.sh/@noble/ed25519@2.1.0";
import { sha512 } from "https://esm.sh/@noble/hashes@1.4.0/sha512";

// noble-ed25519 v2 needs a synchronous SHA-512 hook for the sync sign() API.
ed.etc.sha512Sync = (...m: Uint8Array[]) =>
  sha512(ed.etc.concatBytes(...m));

export function base64url(bytes: Uint8Array): string {
  let bin = "";
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** Signs a payload object and returns the compact token string. */
export function signToken(payload: unknown, privateKeyHex: string): string {
  const json = JSON.stringify(payload);
  const b64Payload = base64url(new TextEncoder().encode(json));
  const message = new TextEncoder().encode(b64Payload);
  const signature = ed.sign(message, privateKeyHex);
  return b64Payload + "." + base64url(signature);
}

export function signingKey(): string {
  const priv = Deno.env.get("LICENSE_SIGNING_PRIVATE_KEY");
  if (!priv) throw new Error("Missing LICENSE_SIGNING_PRIVATE_KEY env.");
  return priv.trim();
}

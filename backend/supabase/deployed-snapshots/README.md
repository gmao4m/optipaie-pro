# What is actually deployed on the licence server (project bajiomgtkpdqyvgpigsc)

The live functions are NOT built from `backend/supabase/functions/` as-is: the live `_shared/license.ts`
is older than the repo (module resolution differs). Always diff against `supabase functions download`
before deploying, and deploy with `--no-verify-jwt` (clients send the non-JWT publishable key).

| Folder | What | Deployed |
|---|---|---|
| `2026-11-28-v4-live/` | LIVE now (validate, activate, activate-module = version 4): no offline deadline for ANY licence (`graceUntil` always null); a dated licence stops at `expiresAt` | 2026-11-28 |
| `2026-11-27-v3-rollback/` | version 3: lifetime licences without a deadline, annual ones kept the 30-day grace | 2026-11-27 |
| `2026-11-27-v2-rollback/` | version 2: the 2026-08-05 code + server_error instead of a masked `unknown_product` | 2026-11-26 |

Rollback (from inside one of these folders' parent, with the folder copied to `supabase/functions/`):

    supabase functions deploy validate activate activate-module --project-ref bajiomgtkpdqyvgpigsc --no-verify-jwt --use-api

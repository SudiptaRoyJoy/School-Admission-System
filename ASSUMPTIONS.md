# Assumptions and decisions

The items below resolve areas the assignment does not fully specify. They are implementation choices, not extra requirements imposed by the assignment.

1. **A class row represents one grade + shift combination.** Each combination has its own independent total seat capacity and FIFO admission pool.
2. **The one-active-application rule is per student per class/shift.** A student can apply to a different class/shift while holding or having confirmed an application elsewhere.
3. **Only unexpired `HELD` and all `CONFIRMED` rows consume seats.** A `HELD` row stops consuming a seat when `expires_at <= PostgreSQL time`, even before the background cleanup worker changes its stored status.
4. **`EXPIRED` and `CANCELLED` rows remain as history.** A student can apply again after either terminal state.
5. **Repeated confirmation by the owner is idempotent.** It returns `200 CONFIRMED`; no extra application or seat count is created.
6. **A confirmed admission cannot be cancelled.** Cancellation of a confirmed admission/refund is outside the described assignment, so the API returns `409 HOLD_ALREADY_CONFIRMED`.
7. **The payment action is simulated.** Clicking it represents successful fee confirmation. No real payment, gateway, webhook, receipt or refund is implemented.
8. **There is no login/authentication.** The brief uses the 17-digit birth-registration number as `X-Student-Id` and specifies no account flow. This header is a demo identifier, not secure authentication.
9. **The admin UI is open.** No administrator login, role or authorization API is described. This is not suitable for production.
10. **Class names need not be unique.** A generated class ID differentiates pools; the assignment does not require unique labels.
11. **`GET /holds/{holdId}` needs no student header.** The published contract does not specify `X-Student-Id` for this route. Hold IDs use 128 bits of cryptographic randomness, and the response includes only the published minimal fields.
12. **The student ID is kept in browser `localStorage` for recovery.** This allows the portal to restore My Applications after refresh/reopen as requested. It is acceptable only for this no-auth demo and must not be copied into a production system with real national identifiers.
13. **PostgreSQL is the authoritative time source.** Capacity and confirmation decisions use database time. The worker only materializes expired states and is not required to free seats. The database host clock should be synchronized; wall-clock time is not mathematically monotonic.
14. **The default API host port is 8080.** The optional bonus starts a second independent container on host port 8081. Both containers use the same PostgreSQL database and no process-local coordination.
15. **`HOLD_DURATION_SECONDS` defaults to 600.** It must be a positive integer. The frontend countdown is a visual aid; the backend/database always decides whether a hold is valid.
16. **UTC is used end to end.** PostgreSQL uses `timestamptz`; API timestamps are UTC ISO-8601 values.
17. **The default UI origin is `http://localhost:3000`.** The API CORS allow-list is configurable with `FRONTEND_ORIGIN`.
18. **Generated IDs are `c_` or `h_` plus 32 lowercase hexadecimal characters.** This gives 128 bits of randomness and keeps IDs opaque.

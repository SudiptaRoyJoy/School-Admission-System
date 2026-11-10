# School Admission System

A minimal, responsive school-admission application for the Assistant Software Engineer take-home assignment.

**Stack:** ASP.NET Core / .NET 10 LTS · PostgreSQL 16 · React 18 + TypeScript · Vite · Docker Compose

## 1. Architecture reference

The consolidated architecture and implementation blueprint is included under `docs/` in both editable DOCX and PDF formats. The source code in this repository is the runnable implementation; `ASSUMPTIONS.md` records implementation decisions where the assignment is silent.

## 2. Run the complete application

The only required runtime dependency is Docker Desktop (or Docker Engine with the Compose plugin).

```bash
docker compose up --build
```

Open:

- **Parent/admin UI:** http://localhost:3000
- **API:** http://localhost:8080
- **API health:** http://localhost:8080/health

The first Docker build downloads the .NET and Node packages and container images. The `migrate` one-shot service waits for PostgreSQL to become healthy, applies the schema migration, and exits. The API starts only after that migration succeeds. The web container serves the compiled React application through Nginx.

### Optional local configuration

The Compose file has safe-for-local-demo fallback values, so `.env` is not required. To customize settings:

```bash
cp .env.example .env
```

Set a local PostgreSQL password in `.env`. Do not commit `.env` or production secrets. Useful variables:

| Variable | Default | Purpose |
|---|---|---|
| `POSTGRES_DB` | `admission` | Database name |
| `POSTGRES_USER` | `admission_app` | Local database user |
| `POSTGRES_PASSWORD` | `local-dev-only-change-me` | Local-only password |
| `HOLD_DURATION_SECONDS` | `600` | Seat-hold lifetime; must be a positive integer |
| `API_BASE_URL` | `http://localhost:8080` | Browser-reachable API URL injected into the frontend at container startup |
| `FRONTEND_ORIGIN` | `http://localhost:3000` | Allowed browser origin for API CORS |

**Important:** `API_BASE_URL` is used by the browser. Use a host-reachable address such as `http://localhost:8080`, not the Compose-only hostname `http://api:8080`.

## 3. Stop, restart and reset

```bash
# Stop containers and preserve admission data
docker compose down

# Start again; classes and applications remain in PostgreSQL
docker compose up -d

# Intentionally erase the database volume and all demo data
docker compose down -v
```

The database is stored in the named Docker volume `pgdata`. Ordinary `docker compose down` does not delete it. `down -v` is destructive.

## 4. Parent portal

1. Open the Parent portal.
2. Enter exactly 17 English digits for the student's birth-registration number and select **Use this ID**.
3. Browse class/shift groups and available seat counts.
4. Select **Apply for a seat**. A successful request creates a temporary `HELD` application.
5. The application appears under **My applications** with a countdown.
6. Select **Simulate fee payment** before the countdown reaches zero to move the application to `CONFIRMED`.
7. A `HELD` application may be cancelled; its seat is then released.

The fee button is deliberately simulated. No real payment, money transfer, gateway, webhook or refund is integrated.

## 5. School admin

Select **School admin**, enter a class/shift name and a positive whole-number seat count, then create the group. Each created group is an independent seat pool. Duplicate names are allowed because the generated ID distinguishes pools.

The admin view is intentionally open because the assignment defines no admin authentication or role API. This is a disclosed demo limitation, not a production-ready access-control design.

## 6. API contract

All request and response bodies use JSON and camelCase field names. Times are UTC ISO-8601 instants. The `X-Student-Id` header is required only on endpoints where listed.

| Method | Path | Header | Success |
|---|---|---|---|
| `POST` | `/classes` | — | `201 { id, name, totalSeats }` |
| `GET` | `/classes` | — | `200` array with `id`, `name`, `totalSeats`, `heldSeats`, `confirmedSeats`, `availableSeats` |
| `GET` | `/classes/{classId}` | — | `200` class with the same seat fields |
| `POST` | `/classes/{classId}/holds` | `X-Student-Id` | `201 { holdId, classId, studentId, status, expiresAt }` |
| `GET` | `/holds/{holdId}` | — | `200 { holdId, classId, status, expiresAt }` |
| `POST` | `/holds/{holdId}/confirm` | `X-Student-Id` | `200 { holdId, status }` |
| `DELETE` | `/holds/{holdId}` | `X-Student-Id` | `204` empty body |
| `GET` | `/me/holds` | `X-Student-Id` | `200` array with `holdId`, `classId`, `className`, `status`, `expiresAt` |

Additional operational route: `GET /health` returns `{ "status": "ok" }` when PostgreSQL is reachable.

### Statuses and relevant conflicts

- `HELD`: application temporarily reserves a seat before expiry.
- `CONFIRMED`: simulated fee confirmation completed; the seat remains occupied.
- `EXPIRED`: hold time elapsed; the seat is available again.
- `CANCELLED`: owner cancelled an unexpired hold; the seat is available again.

Typical HTTP errors: `400 INVALID_REQUEST` / `MISSING_STUDENT_ID` / `INVALID_STUDENT_ID`; `403 NOT_HOLD_OWNER`; `404 CLASS_NOT_FOUND` / `HOLD_NOT_FOUND`; `409 NO_SEATS_AVAILABLE` / `ALREADY_APPLIED` / `ALREADY_ADMITTED` / `HOLD_EXPIRED` / `HOLD_CANCELLED` / `HOLD_ALREADY_CONFIRMED`; `503 SERVICE_UNAVAILABLE`; `500 INTERNAL_ERROR`.

## 7. Why concurrent requests cannot overbook seats

- PostgreSQL is the source of truth for classes, holds and expiry time.
- There are no mutable held/confirmed counter columns. Seat totals are derived in one aggregate SQL statement.
- Creating a hold starts a `READ COMMITTED` transaction and locks the target `classes` row using `SELECT ... FOR UPDATE`.
- The service reads `clock_timestamp()` only after acquiring that lock, expires the current student's stale hold, checks for a prior active/confirmed application, counts occupied seats, and inserts the new hold before committing.
- All API processes share the same PostgreSQL row lock and partial unique index. No in-process semaphore or cache controls correctness.
- Confirmation is a conditional `UPDATE` from unexpired `HELD` to `CONFIRMED`; this does not increase occupied seats. Repeated confirmation by the same student returns success without adding another row.
- Cancellation and expiry only decrease occupancy.
- The expiry worker marks old `HELD` rows as `EXPIRED` in bounded batches using `FOR UPDATE SKIP LOCKED`. Seat capacity does **not** depend on the worker: every capacity/count query excludes `HELD` rows whose `expires_at` is no longer in the future.

The database clock is authoritative for business decisions. `clock_timestamp()` is a wall-clock reading, not a mathematically monotonic clock; the database host clock should be synchronized.

## 8. Optional multi-process bonus

Run two independent API containers against the same PostgreSQL database:

```bash
docker compose --profile bonus up --build
```

- API process A: http://localhost:8080
- API process B: http://localhost:8081

The second process is a separate container running the same image and schema. It is not one process listening on two ports. Both use the same `ConnectionStrings__Default`, class-row lock, database clock and unique index.

Run the concurrency script against both processes (PowerShell 7+):

```powershell
./scripts/concurrency-test.ps1 -ApiBaseUrls @("http://localhost:8080", "http://localhost:8081") -Seats 20 -Applicants 250
```

## 9. Automated tests

### Backend integration tests (real PostgreSQL)

Requirements: .NET 10 SDK and Docker available to Testcontainers.

```bash
cd backend
dotnet test Admission.Tests/Admission.Tests.csproj
```

The xUnit test fixture launches a real `postgres:16-alpine` container, applies the EF migration, and exercises the HTTP API. It covers class creation/counts, duplicate active application, owner checks, idempotent confirmation, cancellation, expiry, leading-zero student IDs, different class pools, the headerless hold-lookup contract, health, My Applications, and 250 simultaneous hold requests against a 20-seat pool.

Do not replace the concurrency test database with EF's in-memory provider or SQLite. Neither reproduces PostgreSQL row locks and partial unique indexes.

### Frontend test and production build

Requirements: Node.js 22.16+ and npm.

```bash
cd frontend
npm install
npm test
npm run build
```

The frontend test verifies that the portal loads server-provided class information. Use the manual viewport checklist below for responsive behavior.

### Manual acceptance checklist

- [ ] `docker compose up --build` succeeds from a clean checkout.
- [ ] Parent portal loads at 360px, 768px and 1280px viewport widths without horizontal page overflow.
- [ ] Admin can create a class/shift from the UI.
- [ ] A valid student ID can apply from the UI; a repeated application is rejected.
- [ ] Fee confirmation is disabled after hold expiry; expired capacity is reusable without requiring an API call.
- [ ] Wrong student ID cannot confirm/cancel another student's hold.
- [ ] Refresh/reopen restores the selected student ID and My Applications.
- [ ] Class counts refresh without reloading the page.
- [ ] Stop and restart Compose without `-v`; data remains.
- [ ] Real-PostgreSQL concurrency test passes; successful holds never exceed total seats.
- [ ] With the `bonus` profile, test requests against both API URLs and confirm the combined count remains within capacity.

## 10. Repository layout

```text
SchoolAdmissionSystem/
├── backend/
│   ├── Admission.Api/
│   │   ├── Common/                 # HTTP errors and validation
│   │   ├── Data/                   # EF Core context and migration
│   │   ├── Domain/                 # Class, hold and status
│   │   ├── Features/Classes/       # Create/list/get class endpoints
│   │   ├── Features/Holds/         # Hold/apply/confirm/cancel endpoints
│   │   ├── Infrastructure/        # IDs and expiry worker
│   │   └── Program.cs
│   ├── Admission.Tests/            # xUnit + Testcontainers PostgreSQL
│   └── Dockerfile
├── frontend/
│   ├── src/api/                    # Typed API contracts/client
│   ├── src/app/                    # Application UI and error boundary
│   ├── src/styles/                 # Responsive design system
│   ├── Dockerfile
│   └── nginx.conf
├── docs/Architecture_Blueprint.docx
├── docs/Architecture_Blueprint.pdf
├── scripts/concurrency-test.ps1
├── docker-compose.yml
├── .env.example
├── ASSUMPTIONS.md
├── AI_USAGE.md
└── README.md
```

## 11. Security and production limitations

Its not a production admissions system. There is no authentication: `X-Student-Id` is caller-supplied identification, not proof of identity. The admin view is open. For the demo, the browser stores the selected 17-digit ID in `localStorage` to restore applications after refresh. Do not use this pattern with real national identifiers in production. Production would need verified parent/student identity, authenticated admin roles, privacy controls, rate limiting, audit logging, secure payment integration and a formal data-retention policy.

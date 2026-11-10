# AI usage disclosure

This project was prepared with assistance from OpenAI ChatGPT (GPT-6) and was informed by the supplied School Admission System take-home PDF and the consolidated architecture blueprint.

## Areas where AI assistance was used

- Requirement extraction and architecture reconciliation.
- PostgreSQL schema, hold lifecycle and concurrency strategy.
- ASP.NET Core / EF Core API structure and code drafting.
- React / TypeScript UI, responsive styles and API client drafting.
- Docker Compose, test structure, README and acceptance checklist.

## Design suggestions reviewed or corrected

- A single backend process listening on two ports is not the two-process bonus. The bonus uses two independent API containers sharing PostgreSQL.
- Seat counts are derived from hold rows; no mutable count columns are maintained.
- Only create-hold takes the class row lock because confirmation preserves occupancy and cancel/expiry reduce it.
- The expiry worker is cleanup only. Capacity queries use PostgreSQL time predicates and do not rely on worker scheduling.
- Hold identifiers use 128 bits of cryptographic randomness because the published GET-by-hold-ID route does not require an owner header.
- No unsupported throughput claims are made.

## Verification status

The authoring environment used to generate this source archive did not have the .NET SDK or Docker installed and had no package-registry network access. Consequently, `dotnet test`, `npm install`, `npm test`, `npm run build`, and `docker compose up --build` were **not executed in that environment**. Static source checks performed during archive preparation: JSON and project XML parsing, Docker Compose YAML parsing, CSS syntax parsing, and TypeScript/TSX syntax transpilation. These are not substitutes for a semantic TypeScript type-check, C# compilation, database execution, or runtime UI verification. The repository includes backend integration/UI tests and exact verification commands, but their pass status must be filled in only after they are run locally.

## Developer responsibility

AI-generated output is a starting point, not proof of correctness. Review the code, run the test suite and Docker acceptance checklist, correct any environment-specific issues, and update this document with the commands and actual test results before submission.

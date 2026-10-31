# expense-insights

Self-hosted expense analysis from bank CSV exports. ING Bank Śląski first, with other banks possible later.
It imports transactions, classifies them with rules, similarity search and local embeddings,
lets you correct categories in one tap, and shows clear, mobile-first summaries and insights.
The UI is in Polish first.

**Status:** planning. See [`docs/plans/`](docs/plans/README.md).

- Architecture: [`docs/architecture.md`](docs/architecture.md)
- Contributor and agent guide: [`AGENTS.md`](AGENTS.md)
- Fake test data: [`test-data/`](test-data/)

Stack: .NET 10 · ASP.NET Core · EF Core · PostgreSQL · Angular (PWA) · ECharts · ONNX Runtime

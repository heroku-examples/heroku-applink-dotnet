# Heroku Applink (.NET)

Applink SDK for Heroku apps on .NET 8. It resolves org auth from Heroku AppLink env vars and exposes simple helpers for Salesforce Data API, Bulk API v2, and optional Data Cloud.

## What’s Included

- Authorization: `ApplinkAuth.GetAuthorizationAsync("<devname>")`
- Core model: `Org` with `DataApi`, `BulkApi`, and `DataCloudApi?`
- Helpers for CRUD/SOQL, Bulk ingest/query (CSV), and Data Cloud queries

## Quick Start

```csharp
using Heroku.Applink;
using Heroku.Applink.Models;

Org org = await ApplinkAuth.GetAuthorizationAsync("myDevName");
// Use org.DataApi / org.BulkApi / org.DataCloudApi
```

## Required Environment Variables

- `HEROKU_APP_ID`
- One of:
  - By attachment: `<ATTACHMENT>_API_URL` and `<ATTACHMENT>_TOKEN`
  - By color (under default addon): `HEROKU_APPLINK_<COLOR>_API_URL` and `HEROKU_APPLINK_<COLOR>_TOKEN`
- Optional: `HEROKU_APPLINK_ADDON_NAME` (defaults to `HEROKU_APPLINK`)

Example local setup:

```bash
export HEROKU_APP_ID=...
export HEROKU_APPLINK_PURPLE_API_URL=https://...
export HEROKU_APPLINK_PURPLE_TOKEN=...
```

## Build & Pack

- `dotnet restore`
- `dotnet build -c Debug`
- `dotnet pack -c Release -o ./nupkg`

## Project Layout

- `Models/`: `Org`, `User`
- `Data/`: Data API (CRUD, SOQL, composite/graph DTOs)
- `Bulk/`: Bulk v2 ingest/query + CSV helpers
- `DataCloud/`: Data Cloud queries/ingest
- `ApplinkAuth.cs`: Resolves AppLink auth via env vars

For full examples and more details, see the repository root `README.md`.

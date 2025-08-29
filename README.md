# Heroku Applink (.NET)

A lightweight .NET class library that retrieves Salesforce/Data Cloud org authorizations from Heroku AppLink and returns a strongly-typed `Org` object suitable for use in web projects.

## Install

Add the project to your solution or package it as needed. The library targets `net8.0`.

## Usage

```
using Heroku.Applink;
using Heroku.Applink.Models;

// Option A: resolve using default addon name (HEROKU_APPLINK)
Org org = await ApplinkAuth.GetAuthorizationAsync("myDevName");

// Option B: resolve by attachment, color, or full API URL
Org org2 = await ApplinkAuth.GetAuthorizationAsync("myDevName", "HEROKU_APPLINK_PURPLE");
Org org3 = await ApplinkAuth.GetAuthorizationAsync("myDevName", "https://applink.example.com");

// Example A: make an authenticated request
using var httpClient = new HttpClient();
using var request = new HttpRequestMessage(HttpMethod.Get, "services/data/v62.0/sobjects/Account");
var response = await org.RequestAsync(httpClient, "services/data/v62.0/sobjects/Account", request);

// Example B: Data API (CRUD, SOQL)
var query = await org.DataApi.QueryAsync("SELECT Id, Name FROM Account LIMIT 10");

// Example C: Bulk API v2 (ingest CSV and query)
var dt = org.BulkApi.CreateDataTableBuilder("Name", "Phone")
  .AddRow(new[] { "Acme", "555-1212" })
  .AddRow(new[] { "Globex", "555-3434" })
  .Build();
var ingestJobs = await org.BulkApi.IngestAsync("Account", dt);

var qjob = await org.BulkApi.QueryAsync("SELECT Id, Name FROM Account");
var qres = await org.BulkApi.GetQueryResultsAsync(qjob, maxRecords: 5000);
if (!qres.Done) { qres = await org.BulkApi.GetMoreQueryResultsAsync(qres); }

// Example D: Data Cloud API
if (org.DataCloudApi is not null)
{
  var dc = await org.DataCloudApi.QueryAsync("SELECT * FROM TABLE");
}
```

## Required environment variables

- `HEROKU_APP_ID`: The Heroku App UUID.
- One of the following sets for the addon you want to use:
  - By attachment name: `<ATTACHMENT>_API_URL` and `<ATTACHMENT>_TOKEN`
  - By color under default addon prefix: `HEROKU_APPLINK_<COLOR>_API_URL` and `HEROKU_APPLINK_<COLOR>_TOKEN`
- Optional: `HEROKU_APPLINK_ADDON_NAME` (defaults to `HEROKU_APPLINK`).

## Notes

- The library mirrors the Node.js project's `getAuthorization` behavior and returns core org info without the full data/bulk APIs. Extend as needed.

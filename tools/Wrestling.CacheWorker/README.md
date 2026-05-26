# Wrestling Cache Worker

Visible browser companion for the Jellyfin Wrestling plugin.

The worker:

- Fetches the selected-library queue from `GET /Wrestling/Cache/Queue`.
- Opens normal Edge or Chrome visibly with a local DevTools port.
- Searches CageMatch by title/year.
- Opens the best event candidate.
- Parses the match card.
- Syncs normalized cache data to `POST /Wrestling/Cache/Sync`.

It respects the CageMatch crawl delay and stops on forbidden, login, JavaScript,
CAPTCHA, or Cloudflare-style gates. It does not bypass CageMatch access controls.

## Run

```powershell
.\.dotnet9\dotnet.exe run --project .\tools\Wrestling.CacheWorker -- `
  --jellyfin-url http://localhost:8096 `
  --api-key YOUR_JELLYFIN_API_KEY `
  --limit 3
```

Options:

- `--browser-path`: explicit path to `msedge.exe` or `chrome.exe`.
- `--remote-debugging-port`: local DevTools port, default `9222`.
- `--crawl-delay-seconds`: delay between CageMatch page opens, default `527`.
- `--limit`: maximum queue items to process for a test run.

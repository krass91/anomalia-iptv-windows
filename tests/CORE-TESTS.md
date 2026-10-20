# Core integration tests

`CoreTests.cs` is a standalone C# 5 test executable compiled together with the current `src/Core.cs`. It uses only synthetic data and a Python HTTP server bound to `127.0.0.1` on an OS-selected port. The server never proxies or fetches outside URLs. Fixture account fields are intentionally synthetic and exercise query escaping; request evidence omits their values and all authorization/cookie values.

Latest result: **18 / 18 passed**. The exact tested Core SHA-256 and individual cases are in `evidence/core-tests.json`. `evidence/fixture-requests.json` records the protocol phases of the successful integration run.

Coverage: M3U HTTP loading, BOM, Unicode, quoted commas, single-quoted attributes, child/root/scheme-relative URLs, local relative paths, user-agent/referrer options and inline headers, metadata-preserving export; HTTP gzip and response-size limit; Xtream account, category joins, live/VOD/series and episode expansion, default/explicit extensions and four account rejection cases; Stalker handshake/profile/genres, authorization and protocol headers, pagination, deduplication, create_link command encoding, and independent sessions after profile switching; storage previous-write backup, repeated writes, missing/corrupt/empty files; XMLTV now/next, case-insensitive IDs, positive/negative time zones and GZIP.

The first run found that a blank settings file returned null; `Storage.Read` was fixed by the main agent, and the suite passed on the rerun. The test worker did not edit `src/Core.cs`.

Compile with the built-in .NET Framework C# compiler:

```powershell
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /out:tests\CoreTests.exe /r:System.Web.Extensions.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll src\Core.cs tests\CoreTests.cs
```

Start fixture server in another terminal; inspect the generated state file to obtain its current port:

```powershell
python -X utf8 tests\fixture_server.py --state evidence\fixture-state.json --requests evidence\fixture-requests.json
```

Run tests with a fresh local work directory each time:

```powershell
$fixtureState = Get-Content evidence\fixture-state.json -Raw | ConvertFrom-Json
tests\CoreTests.exe $fixtureState.base_url evidence\core-tests.json tests\work-new src\Core.cs
```

The server also serves synthetic H264/AAC HLS and MPEG-TS files from `tests/fixtures/hls/` through `/media/hls/index.m3u8` and `/media/hls/index0.ts`. `/lists/playback.m3u` includes both channels. These files were generated locally with FFmpeg (320×180, 25fps, 8 seconds), without outside content. `evidence/fixture-playback-ready.json` contains the route checks and MIME types. These assets support a separate actual-player smoke test; the integration suite itself does not claim decoded playback.

Stop the fixture server with an HTTP request to its `/shutdown` route. It writes its final sanitized request log on exit.

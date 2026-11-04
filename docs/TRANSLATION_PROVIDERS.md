# Translation providers — development branch

Karaoke Studio translation to Russian now uses a provider router; no changes to
the AIMP native DLL, lyrics discovery or Go Bridge are required.

## How to configure

Open **Настройки → ПЕРЕВОД → Источник перевода**:

- **Авто** (default): configured LibreTranslate, then configured DeepL, then MyMemory.
- **MyMemory**: public anonymous HTTPS service, no key required; existing limits apply.
- **LibreTranslate**: enter an HTTPS base URL or a localhost HTTP endpoint. Subpaths are supported.
- **DeepL**: official API; provide the key through the `DEEPL_AUTH_KEY` process environment variable.
  Free keys (suffix `:fx`) use api-free.deepl.com, other keys use api.deepl.com.

Optional `LIBRETRANSLATE_API_KEY` environment variable supplies the LibreTranslate
credential. For Windows PowerShell, set user-scoped variables with:

```powershell
[Environment]::SetEnvironmentVariable("DEEPL_AUTH_KEY", "<your-key>", "User")
[Environment]::SetEnvironmentVariable("LIBRETRANSLATE_API_KEY", "<your-key>", "User")
```

Restart AIMP/Karaoke Studio after setting the variables. The URL is stored
in settings.json; **keys are never written there**. Never commit credentials.

## Behavior and limits

When the translation pane is enabled, each lyric line is sent to an external
service over HTTPS (or HTTP only for loopback LibreTranslate installations).
No audio files or audio file paths are transmitted. An unavailable service
(HTTP 429, 401/403, malformed or empty JSON, network failure, or timeout)
is disabled for the remainder of that *translation session*. The router falls
back automatically to the next configured service; retries start with a new
router. Explicit provider selection does not silently switch to other services.

Translation results remain aligned with original lyric rows. Existing
MyMemory-only cache entries retain their original hashes. Additional provider
chains are separately namespaced to prevent reusing a translation from an
unrelated service configuration. Completed lines are saved to disk in batches.
The original cancellation behavior and current-line priority remain unchanged.

The translation target remains Russian (RU); source language can be automatic
or selected from the existing language dropdown. API requests are limited to
480 UTF-8 bytes per text chunk for MyMemory compatibility; other providers
use the same safe chunks. Auto mode does not require paid provider keys.

## Tests

Run `dotnet run --project tests/TranslationTests/TranslationTests.csproj -c Release`.
Mock HTTP regressions cover provider order, quota fallback, failure quarantine,
manual LibreTranslate/DeepL selection, auth headers, Japanese/Chinese codes,
malformed server URL, cancellation, cache and Unicode chunks.

Interactive QA: confirm automatic and manual modes in AIMP, correct translation
status, URL edit commit on focus loss, restart persistence, provider quota
handling, and preservation of karaoke timing while switching providers.

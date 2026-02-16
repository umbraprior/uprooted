# CLAUDE.md

## Repository Isolation — CRITICAL

This is the **PUBLIC** repository (`watchthelight/uprooted`). There is a separate **PRIVATE** repository (`watchthelight/uprooted-private`).

**NEVER cross content between these repositories:**
- Do NOT copy private-only code (research/, pentesting/, exploits/, src_dump/, legacy/, docs/findings/) into this repo
- Do NOT reference private repo internals in commits or code
- The TypeScript source (src/) lives in the private repo. Only prebuilt dist/ goes here
- All .cs, .rs, .c, .ts files in this repo should be obfuscated (comments stripped)

## What belongs here

- `hook/` — Obfuscated C# source (compiled by CI)
- `installer/` — Obfuscated Tauri installer (compiled by CI)
- `dist/` — Prebuilt, minified JS + CSS
- `tools/` — Obfuscated C profiler source
- `.github/workflows/` — CI/CD pipelines
- `packaging/` — Distribution packaging (Arch PKGBUILD)
- Install/uninstall scripts (PowerShell + Bash, comment-stripped)
- `src/plugins/themes/themes.json` — Theme definitions only

## What NEVER belongs here

- Pentesting scripts, exploit code, token extractors
- Extracted source code (src_dump/, source maps)
- Security findings, vulnerability reports
- Hardcoded credentials or tokens
- Internal documentation (HOW_IT_WORKS, FRAMEWORK_GUIDE)
- Test harnesses, dev scripts, site source
- Research directory or any of its contents
- .map files (source maps)

## Build

```bash
pnpm install --frozen-lockfile
dotnet build hook -c Release
pnpm --filter uprooted-installer tauri build
```

## Version

All version strings must stay in sync. Locations:
- `package.json`, `installer/package.json`
- `installer/src-tauri/tauri.conf.json`, `installer/src-tauri/Cargo.toml`
- `hook/UprootedSettings.cs`, `hook/StartupHook.cs`, `hook/ContentPages.cs`
- `Install-Uprooted.ps1`, `Uninstall-Uprooted.ps1`
- `install-uprooted-linux.sh`
- `README.md` version badge
- `dist/uprooted-preload.js`

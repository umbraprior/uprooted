<p align="center">
  <img src="https://uprooted.sh/og.png" width="700" alt="uprooted" />
</p>

<p align="center">a client mod framework for root communications</p>

<p align="center">
  <a href="https://uprooted.sh">website</a> &middot;
  <a href="https://github.com/watchthelight/uprooted/releases/latest">download</a>
</p>

---

## features

- custom themes with runtime switching and presets
- plugin system with lifecycle hooks
- sentry blocker (privacy protection)
- bridge api for internal interfaces
- native settings UI injection

## install

download the latest release from the [releases page](https://github.com/watchthelight/uprooted/releases/latest) and run the installer.

## build from source

```bash
pnpm install
dotnet build hook -c Release
pnpm installer:build
```

## license

[uprooted license v1.0](LICENSE)

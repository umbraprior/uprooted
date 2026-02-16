# Contributing to Uprooted

Thanks for your interest in contributing! Here's how to get started.

## Branch Rules

> **IMPORTANT: All contributors must push to the `contrib` branch.**
>
> - **DO NOT push directly to `main`.** It is protected and direct pushes will be rejected.
> - Clone the repo, check out `contrib`, and push your changes there.
> - When your work is ready, open a Pull Request from `contrib` (or a feature branch off `contrib`) into `main`.
> - Only @watchthelight can approve and merge PRs into `main`.

```bash
git clone https://github.com/watchthelight/uprooted-private.git
cd uprooted-private
git checkout contrib
# make your changes, then:
git push origin contrib
```

If you're working on a larger feature, create a branch off `contrib`:

```bash
git checkout contrib
git checkout -b my-feature
# work on your feature, then:
git push origin my-feature
# open a PR targeting main
```

## Status

Uprooted is currently **awaiting approval** from Root's developers. We're accepting contributions to the framework scaffold, type definitions, documentation, and the landing page. We are **not** accepting contributions that distribute working injection code until approval is granted.

## Development Setup

```bash
git clone https://github.com/watchthelight/uprooted-private.git
cd uprooted-private
git checkout contrib
pnpm install
pnpm build
```

## Code Style

- TypeScript strict mode
- ES modules (`import`/`export`)
- No default exports except for plugin definitions
- Use descriptive variable names -- no abbreviations

## Pull Requests

1. Push your changes to the `contrib` branch or a feature branch off `contrib`
2. Open a Pull Request targeting `main`
3. If you've added code, add or update types accordingly
4. Make sure `pnpm build` succeeds without errors
5. Write a clear PR description explaining what changed and why
6. Link any related issues
7. Wait for @watchthelight to review and approve

## Reporting Bugs

Use the [bug report template](https://github.com/watchthelight/uprooted/issues/new?template=bug-report.yml) on GitHub.

## Suggesting Features

Use the [feature request template](https://github.com/watchthelight/uprooted/issues/new?template=feature-request.yml) on GitHub.

## License

By contributing, you agree that your contributions will be licensed under the GPL-3.0 license.

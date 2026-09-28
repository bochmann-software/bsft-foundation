# Contributing

Thank you for helping. Bug reports, documentation fixes and pull requests are all welcome.

## How contributions flow

This repository is published from an upstream repository at Bochmann Software, one snapshot commit
per release. A pull request here is therefore **not merged in this repository**. When it is
accepted, it is imported into the upstream with you as its author, released with the next version,
and the pull request is closed with a reference to that release. Your authorship is kept in the
upstream history and credited in the release notes.

Because of that flow, every contributor has to agree to the
[Bsft Foundation Contributor License Agreement](CLA.md) before a pull request can be imported. The
CLA Assistant comments on your first pull request with a link where you sign it, once for all your
pull requests. The agreement lets Bochmann Software distribute your contribution under
this repository's MIT License and in its commercial products built on it; you keep the copyright to
your work.

## Before you open a pull request

- For anything larger than a small fix, open an issue first so the design can be agreed before you
  invest in it.
- Read [docs/package-conventions.md](docs/package-conventions.md) and
  [docs/naming.md](docs/naming.md). They are binding for every package, and the structure check
  enforces most of them.
- Keep the gates green locally:

  ```bash
  dotnet build Bsft.sln -c Release
  dotnet test Bsft.sln -c Release --no-build
  node .github/verify-structure.mjs
  ```

  The CI additionally holds every package at 100 % line coverage.
- Every public type carries an XML `<summary>`, and a change to a package's behaviour updates its
  README and its topic page under `docs/`.

## Reporting a security problem

Never in a public issue or pull request; see [SECURITY.md](SECURITY.md).

# Plan 07 — Signed module packages (Stage 7.4, finding 04 §9.5)

**Status:** decided 2026-11-09 (asked in two rounds). Part 1 (verification, install record, re-check on every load) implemented; part 2 (API + UI) and part 3 (CLI + docs) to follow.

## Problem

`Modules:Security:RequireSigned` doesn't prove who built a module. Three gaps:

1. **The check proves nothing about authorship.** `StrongNameVerifier` compares the entry DLL's
   public key token against `TrustedPublicKeyTokens`. A token isn't secret, and .NET never verifies
   strong-name signatures, so anyone can build a DLL carrying a trusted token. `ContentHashes` in
   the manifest arrive in the same untrusted upload, so they add no trust.
2. **Only the entry DLL is checked.** Other DLLs in the package load without any check.
3. **Two load paths skip the check.** Re-loading installed packages at startup
   (`ModulePackageInstaller`, around line 334) and hot reload (`ModuleReloadService`) call the loader
   directly. A DLL swapped on disk after install loads unchecked.

Mitigation today: uploading a package needs the Admin role.

## Decisions (2026-11-09)

| Question | Decision | Alternatives not taken |
| --- | --- | --- |
| Direction | **D. Signed package:** the publisher signs `module.json`, which must list the SHA-256 of every file in the package | A. docs-only downgrade; B. Windows `WinVerifyTrust` Authenticode; C. cross-platform Authenticode re-implementation; E. admin approves a package hash |
| Platforms | **Windows and Linux** | — |
| Signature format | **CMS/PKCS#7 detached** (`signature.p7s`) over the exact bytes of `module.json`, X.509 code-signing certificate. Verified with .NET `SignedCms`; can be produced with plain `openssl cms` | raw ECDSA public keys (no certificates, no expiry or revocation, needs our own tool) |
| Trust | **Pinned publisher certificates** (thumbprint or PEM) **plus optional CA roots.** For a CA root, the chain is built and validity checked. Revocation checking is opt-in | pinned certificates only; CA roots only |
| Load paths | **Every load is re-checked** (install, startup re-load, hot reload) against the hashes verified at install. A mismatch **warns**, unless `RequireSigned` is on, in which case the load is **refused** | install only; refuse on every load |
| Old token check | **Kept as an accepted option:** a `TrustedPublicKeyTokens` match still satisfies `RequireSigned`. It's labelled "key pinning (not proof of authorship)" everywhere it shows | weak extra check only (not satisfying `RequireSigned`); remove it |
| Tooling | **A small CLI** (a dotnet tool) that packs a folder into a `.wfmod` with every file's hash and signs it (pfx or PEM key), **plus docs** with the equivalent `openssl` commands | openssl docs only; an MSBuild target |
| UI | **All three:** a publisher badge per module in Module Manager (Verified: publisher / Unsigned / Key pin only / Changed on disk); the signing result in the upload dialog; a **Trusted publishers** settings page (Admins add or remove; values from server config are locked, like the outbound HTTP screen) | — |

## Design sketch (to refine when implementing)

- **Package layout:** `module.json` (with `ContentHashes` covering every file except
  `signature.p7s`) and `signature.p7s` (detached CMS over the exact `module.json` bytes). A signed
  package whose hashes don't cover every file is refused.
- **Verification** (`IPackageSignatureVerifier`, beside `IAssemblyVerifier`):
  1. verify the CMS signature over `module.json`;
  2. match the signer certificate against pinned thumbprints or PEMs, or build its chain to a trusted
     CA root (validity at signing time or now; revocation opt-in);
  3. check every file against `ContentHashes`.
  - The result is publisher, thumbprint, trust source (pinned / CA / token / none) and messages.
- **Policy:**
  - Satisfied by a trusted signature, **or** by a strong-name token match (the kept legacy option,
    labelled weak).
  - Unsigned or untrusted packages warn by default; `RequireSigned` blocks them (as today).
- **Install record:** store the verified file hashes, the publisher and the trust source with the
  installed package state.
- **Re-check on load:** startup re-load and hot reload re-hash the files and compare them with the
  install record. A mismatch logs a warning and marks the module "Changed on disk"; with
  `RequireSigned`, the load is refused.
- **Trusted publishers config:** `Modules:Security:TrustedPublishers` (host config, locked in the UI)
  merged with UI-saved entries (persisted and audited like the outbound HTTP policy).
- **API:**
  - Module list and upload responses gain `signature { status, publisher, thumbprint, trustSource }`.
  - Trusted-publisher endpoints: GET (read access), PUT (Admin), audit (Admin).
- **CLI:** `dotflow-module pack <folder> --out x.wfmod [--sign --cert pfx|pem --key …]`.
- **Docs:** `docs/module-signing.md` (how to sign, with the openssl recipe, trust config, what each
  badge means, alternatives), and update 04 §9.5 and the findings table.

## Tests to write first (§2 loop)

- A package signed by a pinned certificate installs as "Verified".
- A copied public key token without a signature still passes `RequireSigned` (the kept option) but
  shows "Key pin only".
- Each of these is refused under `RequireSigned`:
  - a tampered file;
  - a tampered manifest;
  - a signature by an untrusted certificate;
  - a signed package whose hashes don't cover every file.
- CA-rooted chain: accepted, and an expired or wrong-root certificate is refused.
- A DLL swapped on disk after install:
  - startup re-load and hot reload warn (default);
  - with `RequireSigned`, the load is refused.
- UI: badges, the upload result, and the trusted-publishers page (locked fields, Admin save).

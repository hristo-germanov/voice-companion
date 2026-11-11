# Prepare and publish to GitHub

The repository is prepared for a source-only upload. Local application data and credentials remain on this computer. Preparing these files does not publish anything to GitHub.

## Included and excluded files

Include source, documentation, launch scripts, the API project, frontend package/lockfiles, and `apps/api/appsettings.example.json`. The example contains the placeholder capture token `change-me`.

The root `.gitignore` excludes:

- The real API `appsettings.json` and environment-specific configurations.
- SQLite databases and their sidecars, which can contain conversations and a Cloud API key.
- Dependencies, SDK caches, Angular cache, `bin*`/`obj*` trees, build output, and test results.
- Logs, environment files, private-key/certificate files, machine-specific editor settings, and local agent state.
- The local `bugs/` screenshots, which may contain private conversation text.

Keep the actual local configuration in place. Git exclusions do not delete files or change the running application. Do not use `git add -f` on excluded data. A private repository still should not contain these files.

## Fresh clone setup

Copy `apps/api/appsettings.example.json` to `apps/api/appsettings.json`, replace `Api:ApiKey` with a private token, and enter that token in extension Options. Follow [Run locally](local-run.md) to restore/build the API, install frontend dependencies, and start the services. A fresh SQLite database is created on first API startup.

## Review and publish

From the repository root, inspect what will be included:

```powershell
git status --short
git ls-files --others --exclude-standard
git diff --cached --stat
```

After reviewing the files, create the first commit if it has not been created:

```powershell
git add .
git diff --cached --stat
git commit -m "Initial source import"
```

If Git asks for identity, configure your intended name and email locally in this repository; use your GitHub-provided no-reply email if preferred. Do not put a personal access token in a remote URL or source file.

Create an empty repository in GitHub under your intended account, choosing its name and visibility. Leave the remote README/license/gitignore initialization unchecked because these local files already exist. Then replace the example URL below with that repository's URL:

```powershell
git remote add origin https://github.com/YOUR-ACCOUNT/YOUR-REPOSITORY.git
git push -u origin main
```

Authenticate through your configured Git credential manager when prompted. If `origin` already exists, inspect it with `git remote -v` before changing anything. If the remote already has commits, reconcile its history before pushing; do not force-push as a routine setup step.

GitHub publishing requires the intended account/repository destination. No repository visibility or license is selected automatically. If you intend public reuse, choose an appropriate license separately.

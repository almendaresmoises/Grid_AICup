# Working on GRID

Use a feature branch and a pull request into `master`. Keep one logical task per branch after the initial local-code baseline has been merged. Public visibility and branch protection are separate; GitHub enforces the repository's configured reviews and checks.

## Start a task

First check for unfinished work:

```powershell
git status
```

Continue or save that work on its current branch before changing tasks. Once the working tree is clean:

```powershell
git switch master
git pull --ff-only origin master
git switch -c feat/short-description
```

Use descriptive branch names such as `feat/ai-improvements`, `fix/parser-dates`, or `docs/system-overview`.

## Review, validate, and commit

Check the files and diff before staging. Include new helpers, contracts, migrations, fixtures, or other dependencies required by the change.

```powershell
git status --short
git diff --stat
git diff
```

Run checks relevant to the changed behavior. Common checks from the repository root are:

```powershell
npm.cmd run build --workspace web
npm.cmd run lint --workspace web
dotnet test backend/Grid/test/Grid.Domain.Tests/Grid.Domain.Tests.csproj -c Release
dotnet test backend/Grid/test/Grid.Application.Tests/Grid.Application.Tests.csproj -c Release
dotnet test backend/Grid/test/Grid.EntityFrameworkCore.Tests/Grid.EntityFrameworkCore.Tests.csproj -c Release
python -m pytest workers/tests/test_canonical_pipeline.py -q
```

Run .NET commands sequentially when they share build output. Unit tests and builds do not establish browser, production infrastructure, migration deployment, or live AI-provider behavior; describe any unverified areas in the PR.

Stage the intended paths explicitly, replacing the example path below:

```powershell
git add -- path/to/changed-file
git diff --cached --stat
git diff --cached
git diff --cached --check
git commit -m "Describe the completed change"
```

If a file was staged accidentally, this keeps its local edits while removing it from the next commit:

```powershell
git restore --staged path/to/file
```

Local environment files, generated Python bytecode, Firebase caches, dependencies, and build output do not belong in a source commit. Existing `appsettings.secrets.json` files are empty build-required templates; keep them empty in Git and supply actual configuration through the documented local/deployment mechanisms. The currently ignored `handoffs/` and `guides/` folders remain local documents unless their inclusion is explicitly decided.

## Push and open a pull request

For the first push of the example branch:

```powershell
git push -u origin feat/short-description
gh pr create --base master --head feat/short-description
```

Use `gh auth status` to check GitHub CLI authentication and `gh auth login` if authentication is needed. The PR description should explain the behavior changed, checks actually run, and any limitations. For unfinished work, add `--draft` to `gh pr create`.

To inspect the PR:

```powershell
gh pr view --web
gh pr checks
```

Checks appear only if the repository/integrations configure them. Follow the actual required approvals and checks shown by GitHub.

## Address feedback

Make corrections on the same feature branch, then stage, commit, and push them:

```powershell
git add -- path/to/corrected-file
git commit -m "Address review feedback"
git push
```

The existing PR updates automatically. No new PR is needed for each correction.

If GitHub reports a conflict with `master`, begin with a clean working tree, fetch the target branch, and merge it into the feature branch:

```powershell
git fetch origin
git merge origin/master
```

Resolve any conflicts, stage the resolved paths, finish the merge commit if required, rerun relevant checks, and push. Do not force-push `master` or discard local changes to resolve a conflict.

## Merge and begin the next task

When the PR is ready and repository requirements are satisfied:

```powershell
gh pr merge --squash
```

After the PR has merged, and with a clean working tree:

```powershell
git switch master
git pull --ff-only origin master
git switch -c feat/next-task
```

Merging source changes does not itself guarantee a production deployment. Follow the repository's deployment process separately.

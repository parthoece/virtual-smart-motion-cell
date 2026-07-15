#!/usr/bin/env bash
set -euo pipefail
: "${GITHUB_REPOSITORY_NAME:=virtual-smart-motion-cell}"
if ! command -v gh >/dev/null; then echo "Install GitHub CLI first." >&2; exit 1; fi
if grep -R "YOUR_GITHUB_HANDLE\|SECURITY_CONTACT@example.com" README.md SECURITY.md MAINTAINERS.md CODEOWNERS .github >/dev/null; then
  echo "Replace publishing placeholders before creating the repository." >&2
  exit 1
fi
git init
git add .
git commit -m "Initial open-source architecture release"
git branch -M main
gh repo create "$GITHUB_REPOSITORY_NAME" --public --source=. --remote=origin --push
echo "Now configure branch protection, Discussions, vulnerability reporting, topics, and labels using docs/publishing-checklist.md."

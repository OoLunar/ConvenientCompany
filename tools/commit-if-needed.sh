#!/bin/bash

# Identify as GitHub Actions
git config --global user.email "github-actions[bot]@users.noreply.github.com"
git config --global user.name "github-actions[bot]"

# Check if there are any changes
if ! git diff --exit-code; then
  git add . > /dev/null
  git commit -m "Update \`manifest.json\`." > /dev/null
  git push > /dev/null
  echo "Committed \`manifest.json\`."
fi
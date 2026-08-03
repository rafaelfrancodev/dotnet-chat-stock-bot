---
description: Quality-gated commit — format, build, test, secret scan, then commit with a proper message
argument-hint: [optional commit message]
---

Steps (do them, don't just describe):
1. dotnet format
2. dotnet build (fail -> fix before continuing)
3. dotnet test (fail -> fix before continuing)
4. Secret scan: grep -riE "password|secret|apikey|connectionstring" on staged changes; confirm appsettings contain placeholders only.
5. git add the relevant files (never blanket-add build artifacts), git status review.
6. Commit with: $ARGUMENTS (if empty, write a small imperative message describing the change).

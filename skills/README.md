# Agent skills

Skills in this directory teach coding agents (GitHub Copilot CLI and compatible agents) how to use
this project's tools. Each skill is a folder containing a `SKILL.md` with YAML frontmatter
(`name` + `description`) and a markdown body.

## `msix`

Use the MSIX Core (.NET) library and the `msixkit` CLI to **inspect**, **validate**, **unpack**, and
(on Windows) **install** MSIX/APPX packages. See [`msix/SKILL.md`](msix/SKILL.md).

## Installing a skill for GitHub Copilot CLI

Copy (or symlink) the skill folder into your Copilot skills directory so the agent discovers it:

```powershell
# Windows
Copy-Item -Recurse -Force .\skills\msix "$env:USERPROFILE\.copilot\skills\msix"
```

```bash
# Linux / macOS
cp -r ./skills/msix ~/.copilot/skills/msix
```

The agent picks the skill up on its next session and invokes it automatically when a request matches
the skill's `description`.

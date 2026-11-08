# Title

## Introduction

We are working on Vakage, a Tekhnexos companion desktop app built in C# and Avalonia.

Vakage is an archive for exported WhatsApp conversations: a chat exported from the phone (the zip of *Export chat, Include media*) is imported into Vakage and can then be deleted from the phone, and the conversation, its photos, voice notes and documents stay readable and manageable on the computer. It keeps a separate **account** per person whose phone the exports come from (Tiago's, later his wife's), reads a conversation as a stream with photos inline, **joins** chats of one account into one continuous conversation (a contact's old number and new number), lists every **file** of an account largest first with a filter by sender, and stores **each file once** by its content, so the same photo sent to several people, sent back, or exported from both sides of a conversation is one file that every chat points at. Later: both sides of one conversation from two accounts, lossless image conversion and PDF shrinking, an AI model's help in judging which files are worth keeping, and other messaging apps (iPhone Messages first). WhatsApp comes first.

The plan for the product, with the decisions taken so far and the library's tables, is:

```text
L:\Library\Coding\CSharp\Vakage\documents\initial-plan.md
```

Read it before changing the shape of the app. Things to keep in mind on every change:

- **Vakage is open source and public** (`https://github.com/tsalmeida/Vakage`, MIT), unlike the other companions. Nothing private goes into the repository: no exports, no libraries, no real chats in tests or screenshots, no real names of the people in Tiago's conversations, no keys. Tests build their exports in code with made-up names. Private notes about the project belong in its All My Projects row (below), not in the repo. Keep the README, `CONTRIBUTING.md` and `LICENSE` true as the app changes.
- **Accounts never mix.** Every chat belongs to one account; a join never crosses accounts; nothing merges two people's archives by accident. Showing both sides of one conversation is a planned feature, to be built as a view over two accounts, never as a merge.
- **One copy of each file.** The file store is content-addressed (`files\ab\<sha256>.ext`); deduplication happens at import. A chat is deleted with its messages; a stored file goes only when no message in any chat or account still uses it. Imports copy and never move or change the export.
- **Nothing leaves the computer** unless Tiago asks for it: Vakage makes no network calls today. The AI file review, when it comes, is opt-in and says what it sends.
- **Core vs. UI split is deliberate.** `src/Vakage.Core` has zero Avalonia dependencies: the export parser (`Sources\WhatsApp`), the library and the file store (`Library\ArchiveLibrary.cs`) live there, with tests. `src/Vakage` is the window and the command line (`--import`, `--list`, `--files`, each with `--library`), which is how an import is checked without the window.
- **Not a WhatsApp look-alike. The look is not yours to invent.** Vakage follows the Tekhnexos companion design standard at `L:\Library\Coding\Laravel\Tekhnexos\documents\companion-design-standard.md`. `src\Vakage\Design\` (`Palette.cs`, `Typography.cs`, `Design.axaml`, `ActionIcons.cs`) and the Roboto fonts in `src\Vakage\Assets\Fonts` are copied from Elfwida (`L:\Library\Coding\CSharp\Elfwida`), the canonical implementation; re-copy them rather than editing them, and put anything missing back in Elfwida first. The identity colour is stone (`#57534d`, Tailwind stone-600) and belongs on the icon and the app's own name alone; inside the app, lime is positive and orange is negative, conversations are indigo and files amber. Your messages use the standard's amber bubble, everyone else's the indigo one. New UI uses the existing classes (`page`, `section`, `section bar`, `pill`, area colour classes, `caption`, `prose`, `subheading`, `heading`, `title`, `columnheader`, `mono`, `positive`, `negative`).
- **Font Awesome Free only.** A public repository cannot carry the Pro face the private companions embed, so Vakage embeds Font Awesome Free 6 Solid (`Assets\Fonts\fa-solid-900.ttf`, family `Font Awesome 6 Free Solid`, licence beside it). Pick icons in Eikonotheke's catalogue as usual, but check that the glyph exists in the Free set before using it.

The project lives at:

```text
L:\Library\Coding\CSharp\Vakage
```

## Working on the app

Build, test, publish and install from the repo root:

```powershell
dotnet build Vakage.slnx -c Release      # expect 0 warnings, 0 errors
dotnet test  Vakage.slnx -c Release
.\install.bat
```

`install.bat` wraps `install.ps1`, which calls `Publish-CompanionApp` from `L:\Sync\CompanionFamily.ps1`: it **closes a running Vakage by itself**, publishes, rewrites the Start Menu shortcut and adds `C:\TKHN\Vakage` to the user PATH. Do not stop to ask whether to close it. Once the new build is installed, **relaunch it**, so the user is not left on a stale executable.

That publishes to:

```text
C:\TKHN\Vakage\Vakage.exe
```

**Do not install to `%LOCALAPPDATA%\Programs\Vakage` or `L:\Apps`.** The office workstation's Defender ASR rule refuses a fresh unsigned exe there. `C:\TKHN` is the shared, Defender-excluded root for the companion family, catalogued in `L:\Sync\companion-family.json` (Vakage's entry: stone, `box-archive`), which is also what puts it in Nauarchos. Settings and the log stay in `%LOCALAPPDATA%\Vakage`; the library is wherever `LibraryPath` in its `settings.json` says (default `Documents\Vakage`).

An iPhone's whole WhatsApp comes from its backup: the shared container `group.net.whatsapp.WhatsApp.shared` (`ChatStorage.sqlite`, `Message\Media`), read by `--import-ios`. An encrypted backup is decrypted outside Vakage, and its password is typed by Tiago into a window of its own, never into the conversation or a file. Decrypted phone data stays off `L:` (it is synced) until Tiago says where it lives.

Check an import from a terminal before looking at the window, on a scratch library when testing:

```powershell
Vakage --import "<export.zip>" --account Tiago --library <scratch folder>
Vakage --list --library <scratch folder>
Vakage --files --account Tiago --library <scratch folder>
```

Never drive the running app with synthetic clicks or `SendKeys`. Screenshots via per-window `PrintWindow` are welcome instead. Screenshots of real conversations stay off GitHub.

## Finishing a piece of work

When the work is done, built, installed and relaunched, **commit it and push it**. Do not leave a finished change sitting in the working tree waiting to be asked for, and report honestly what was completed and what remains.

- The repo is `https://github.com/tsalmeida/Vakage` (**public**) and work happens on `main` directly. Commit there; no branch, no PR. Before every commit, look at what is staged: nothing private may be pushed.
- If the working tree holds unrelated changes that were already there when you started, read them, understand them, and give them their own commit with their own message rather than folding them into yours.
- Write the message as a plain sentence saying what changed, then a body explaining *why* — not a changelog of files touched.
- When a fix belongs in an `L:\Sync` script or in `companion-family.json` rather than in the app, commit it in `L:\Sync` — a git repository of its own, whose pre-commit hook refuses non-ASCII in a `.ps1` — with its own message, and add an entry to `L:\Vault\Documents\Others\Logarktos-changelog.md` for what the other computer must do.
- Update the README in the same commit when the change is something to look for there.
- If a task fails or ends half-done, say so plainly instead of dressing it up.

## Database prompts

At the start of every session, **before working on the main request in `## Request` below**, check this database for extra prompts for this project.

Process **only** database rows whose `datapoint_2` is exactly `Pending`; never alter rows marked `Finished`, `Postponed`, or `Draft`. This Pending-only restriction applies **only to rows read from this database**. It does not suppress, replace, or limit the main request supplied below: after completing any applicable Pending database work (or finding none), also carry out the main `## Request`.

In addition to the request below, there might be extra requests stored in:

```text
L:\Library\Coding\project-ai-prompts_package.sqlite
```

Find Vakage prompts by querying for `datapoint_1='Vakage'` and `datapoint_2='Pending'`; the prompt itself is in `longtext_1`. Process them one by one and finish each properly before marking it `Finished` — implemented, built, installed, committed and pushed — stamping `updated_at` (UTC, `YYYY-MM-DDTHH:MM:SS.000000Z`). If a task fails or ends half-done, leave its row `Pending` and say so; a wrongly-Finished row becomes invisible to every future session. That sqlite file is kept in sync by TKHN Sync while you work, so write updates one small `UPDATE` at a time, committing immediately, and never rewrite the file wholesale.

## Current situation

The live briefing for this project is the Current Situation field of its row in All My Projects (`L:\Vault\Obras\all-my-projects_package.sqlite`, column `longtext_3`). This row is titled "Vakage" (id `63494`). Tiago almost never reads it. Models do. Read it at the start of work on this project. It is the mirror of what is happening, what is waiting, what to do next, and what the intended direction is. It is private: unlike the repository, it may name the people in Tiago's conversations.

**Keep it true without asking.** Do not wait for confirmation. Do not open a second current-situation note with a new timestamp, and do not store a second copy of this briefing. Edit that field in place.

When anything material changes, do both of these in the same turn:

1. **Rewrite the present.** Change the snapshot and the do-now, waiting, and next lines so they match today. Delete or recast sentences that would be false if read tomorrow. A stale now line is a bug.
2. **Add what is new.** Put new facts in the section they belong to. If a plan changes, replace the old plan. Do not leave two competing plans.

**History.** Keep a short block of only what still explains the present. Move a fact there when it is no longer current but still explains a lock or a do-not-redo. Do not grow a diary. Detailed dated notes stay in their own files.

Do not copy whole emails or whole prompts into it. Do not leave advice that was already decided.

Write the field with one small `UPDATE` of `longtext_3`, commit immediately, and stamp `updated_at` (UTC, `YYYY-MM-DDTHH:MM:SS.000000Z`). Open a short-lived connection with pooling off. Re-read the row just before changing it. Never rewrite the database file wholesale. TKHN Sync carries the file.

After editing, stamp **Last rewritten** at the top (the date, and the one-line reason).

## After the work

When you have implemented the requested changes, write a brief report of what was done into Autarktos (`L:\Library\Coding\project-ai-prompts_package.sqlite`), in `longtext_2` of the entry this prompt came from. The commit message is fine as that report.

Set `datapoint_4` ("AI Model family that processed it") to exactly one of: GPT, Grok, Claude, Gemini, Other — according to which model family processed this prompt.

If a Dispatcher's note names the entry `id`, use that. If you processed rows from the Database prompts section, write the report on those rows. Do not pick up other Pending rows from the database; that remains the Database prompts section, used only for manual one-project prompting. If this session has no Autarktos entry, skip the write.

Write those fields with one small `UPDATE` at a time, commit immediately, and stamp `updated_at` (UTC, `YYYY-MM-DDTHH:MM:SS.000000Z`). The sqlite file is kept in sync by TKHN Sync; never rewrite it wholesale.

## Request

*template_focus*

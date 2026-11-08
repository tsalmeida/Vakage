# Vakage — plan

Vakage keeps exported WhatsApp conversations readable and manageable on the computer, so they can be deleted from the phone. It is a Tekhnexos companion app (C#, Avalonia, the companion design standard) and, unlike the rest of the family, **open source from the first commit** (MIT, public at `https://github.com/tsalmeida/Vakage`): this is a tool everyone should already have.

## Decisions (Tiago, 2026-11-08)

- **WhatsApp first.** Other messaging apps (iPhone Messages first) come later, through the same library.
- **Not a WhatsApp look-alike.** Vakage looks like a Tekhnexos companion: Roboto, Tailwind colours, `top-icon-button` pills with their Font Awesome icon, white sections on a grey page.
- **Accounts never mix.** Each person's exports (Tiago's, his wife's) are a separate account, switched at any time, never merged by accident. Mixing them would be a disaster.
- **Chats can be joined.** A contact's chats under an old and a new account read as one continuous stream, as they never could in WhatsApp.
- **One copy of every file.** The same file sent to several people, sent back, or exported from both sides of a conversation is kept once, and every chat links to it.
- **Files by size, by sender.** List every attachment, largest first, and filter by who sent it. Later an AI model helps judge what is worth keeping.
- **Make files smaller.** Lossless image conversion (PNG to WebP and the like) and shrinking oversized PDFs.
- **Open source standards from the start**: licence, README, contributing guide, no private data in the repository.

## Decisions taken by the model in the first build (to confirm or overturn)

- **Licence: MIT**, as Tiago's other public repositories (logarktos.nvim, lexarkhos).
- **Icons: Font Awesome Free**, embedded as `Font Awesome 6 Free Solid`. The Pro face the private companions embed is not redistributable, so it cannot be in a public repository. Every glyph used must exist in the Free set.
- **Identity colour: stone** (`#57534d`), the last unused Tailwind 600 hue in the family.
- **Deduplication at import, by content.** Files are stored under their SHA-256 (`files\ab\<hash>.ext`), so a file is never stored twice in the first place; there is no "delete one set of copies" step to run afterwards. This covers the same chat exported from two phones, a file forwarded between chats, and a file sent back.
- **The library lives in `Documents\Vakage`** by default (a setting, `LibraryPath`). Where Tiago's library should live, given its size and what TKHN Sync carries, is his call.
- **The owner of an account** is the sender found in every one of its chats (it takes two chats to tell), or the name picked in *You are*. Their messages sit on the right.

## The library

```text
<LibraryPath>\
  vakage.sqlite           accounts, threads, chats, imports, messages, files
  files\ab\<sha256>.ext   each file once, by content
```

| Table | Holds |
| --- | --- |
| `accounts` | One per person whose phone the exports come from; `self_name` is how that person appears as a sender. |
| `chats` | One per chat of an account (`source` = `whatsapp`), named after the contact or group. A later export of the same chat (same account, same title) adds only the messages not already there; repeated identical messages are counted, not collapsed. |
| `threads` | Joined conversations: chats of one account read as one stream. Joining never crosses accounts. |
| `messages` | Wall-clock time (exports carry no time zone), sender, text, kind (`text`, `system`, `media-omitted`), the attachment's name in the export, and the stored file. |
| `files` | SHA-256, size, extension, the first name it arrived under. |
| `imports` | Which export brought what into which chat, and when. |

Imports copy, never move: the export is left as it was, to be deleted by its owner once the import is checked. Deleting a chat deletes a stored file only when no message of any chat or account still refers to it.

## Reading exports

`WhatsAppChatParser` reads Android lines (`12/03/2020 14:22 - Name: text`) and iPhone lines (`[12/03/2020, 14:22:05] Name: text`), with the direction marks and narrow spaces WhatsApp scatters through them. Day-first or month-first is worked out from the whole file; a line without a date continues the message above. Attachments: Android's `IMG-…jpg (file attached)` in the phone's language, iPhone's `<attached: …>`, and a bare file name when the export ships that file. Omitted media (`<Media omitted>`, `imagem ocultada`) is kept as a marker.

`WhatsAppExport` opens a zip, a folder, or a bare `.txt` (with the files beside it), finds the chat text (`_chat.txt`, or the `WhatsApp Chat with …` / `Conversa do WhatsApp com …` file) and names the chat from the file or zip name.

## Reading an iPhone's own database

`WhatsAppIosDatabase` reads WhatsApp's shared container from an iPhone backup (`ChatStorage.sqlite`, `Message\Media`): `ZWACHATSESSION` (chats; type 0 a person, 1 a group, 3 a status feed, left out), `ZWAMESSAGE` (time in Core Data seconds since 2001 UTC, `ZISFROMME`, type, text), `ZWAMEDIAITEM` (local path under `Message`, title) and `ZWAGROUPMEMBER` (who wrote in a group). Types: 0 text, 1 image, 2 video, 3 voice note, 4 contact, 5 location, 6 group event, 7 link, 8 document (name in the text), 14 deleted, 15 sticker, 59 call; a photo's or video's caption is the media item's title. A chat is known by `ios:<JID>` in `chats.source_key`, so contacts saved under one name stay apart (their titles carry the number) and a later backup adds only what is new. A stored file takes the extension of WhatsApp's own file, never of a document's name. An encrypted backup is decrypted outside Vakage, with its password typed by its owner.

## Decisions (Tiago, 2026-11-08, second session)

- **The archive is `L:\Library\Chat-Archives`.** Everything WhatsApp holds goes there, so Tiago can delete it from the phone himself; Vakage never touches the phone. A backup a year later must join on as if it had never been cut off.
- **Precious and noise.** Junk (holiday greetings and the like) keeps only a tiny thumbnail; an image that is only text keeps its text (read by Eidokanon) and goes; where it was, a notice says what was kept. A PDF is turned into text the same way. The precious things (his father's photos of the farm, photos of his family) are kept whole, once, even when they appear in several chats. The Files tab shows the important files and the irrelevant files, and a file moves from one list to the other by marking it.

## Continuity: a later backup joins on

What makes the archive one thing however many backups feed it:

- Every message keeps WhatsApp's own id (`messages.source_id`, the stanza id with the direction) and its exact time in UTC (`sent_utc`), shown in whatever zone the computer is in. A later import skips the ids it has.
- Every chat keeps WhatsApp's id (`chats.source_key` = `ios:<JID>`); its title follows the phone's current name. When WhatsApp re-keys a chat (numbers moving to `@lid` ids), the chat whose old id is gone from the phone and which already holds nearly all (80 %) of the sampled messages is the same chat, and takes the new key. Message ids alone are not enough: a broadcast carries one id into several chats (two channels were merged that way on the first run and split again).
- Who wrote is kept by id too (`sender_key`: `me`, or a WhatsApp id), whatever name they are saved under.
- The phone's databases are kept as read (`sources\whatsapp-ios\<account>\<date>`), for anything a later Vakage wants from them.
- The library is a single SQLite file with a rollback journal (no WAL side files), and stored files are never rewritten, because `L:` is synced to the Suitcase.

## Sorting the files

`files.mark` is the person's decision (`important`, `irrelevant`); `files.suggestion` and `suggestion_reason` are Vakage's, recomputed after every import (`Suggest`), never overriding a mark. Rules, first match wins: sticker, GIF, channel, forwarded (WhatsApp's flag `0x100` on the message) and sent around 3+ chats are irrelevant; a picture shaped like a screenshot (long side 1.9 times the short, read from the header into `files.width/height`) is left unsorted; a photo or video Tiago sent himself, not forwarded, is important; a photo, video or voice note in a one-to-one chat of 100+ messages is important (those chats are his family and close friends). `Reduce` lets irrelevant files go: a picture with 20+ words keeps its words, any other a 240-pixel JPEG thumbnail (`thumbs\`), a video a thumbnail of its first second (ffmpeg), a PDF its text, audio a notice; the original is deleted. A file marked important is never reduced and never deleted with its chat, and one that was reduced and later marked important comes back whole if an import brings the original again. Text comes from `Eidokanon --extract <file> --stdout` (pictures by Tesseract, `--lang por+eng`; PDFs with `--raw`).

## Where it stands (2026-11-08)

Tiago's whole iPhone WhatsApp (backup of 2026-11-08) is archived in `L:\Library\Chat-Archives`: 700 chats, 309,981 messages, 29,374 files (11.5 GB), checked chat by chat against the phone's database; 919 media files were never downloaded by the phone. His wife's three numbers and a friend's three are joined. Suggestions: about 19,200 files important, 5,400 irrelevant, 4,800 unsorted; nothing reduced yet.

Version 0.1: accounts, import of exports (window and `--import`) and of an iPhone's WhatsApp database (window and `--import-ios`), the conversation stream with photos inline, join and unjoin, the files list by size with the sender filter, delete with file clean-up, 14 tests. Installed in `C:\TKHN\Vakage`, in Nauarchos.

The first real test is two of Tiago's chats (his father, a friend), read from a full iPhone backup of 2026-11-08: about 36,000 messages and 2,000 files, the friend's chat joined from three numbers. No export of them existed on the workstation.

This repository is public: real names, chats and libraries stay out of it. The private notes on the project are in the All My Projects row (see `documents/prompts/template.md`).

## Next

1. Tiago reviews the suggestions in the Files tab (important / irrelevant / unsorted), corrects them, and reduces the irrelevant files; then deletes WhatsApp's data from the phone himself.
2. Find the old, since-deleted chats with his father and a friend somewhere in his files (Tiago will look; not on L: by name or content).
3. Read pictures' text in bulk (OCR) to find images that are only words, and an opt-in AI look at pictures, to sharpen the suggestions.
4. Search inside a conversation and across an account.
3. Both sides of one conversation: Tiago's chat with his wife and hers with him, from two accounts, shown side by side or as one stream, without ever merging the accounts.
4. Delete single files (and the messages' links to them) from the Files tab, with the bytes freed.
5. Lossless image conversion and PDF shrinking, keeping the content hash of the original so a re-import still recognises it.
6. The AI review of files: what is meaningful, what is noise. Opt-in, saying what it sends.
7. iPhone Messages.

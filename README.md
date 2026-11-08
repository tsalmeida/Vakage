# Vakage

**An archive for your exported WhatsApp conversations.** Export a chat from your phone, import it into Vakage, and delete it from the phone: the conversation, its photos, voice notes and documents stay readable on your computer, in one place, under your control. Nothing is uploaded anywhere.

Stone icon (`#57534d`, Tailwind stone-600). A Tekhnexos companion app, C# and Avalonia, open source under the MIT licence.

## What it does now (0.1)

- **Accounts.** One account per person whose phone the exports come from (yours, your partner's), so two people's archives never mix. Switching accounts switches the whole window.
- **Import** an exported chat: the `.zip` WhatsApp makes with *Export chat → Include media*, the folder it was unzipped into, or the chat's `.txt` alone. Android and iPhone exports, in English, Portuguese, Spanish, German, French, Italian and Dutch, 24-hour and AM/PM clocks. A later export of the same chat adds only the messages that are new.
- **Import a whole phone** from an iPhone backup: WhatsApp's own database (`ChatStorage.sqlite` and its `Message\Media`), with every chat, exact times, documents under their real names and every file the phone downloaded. Contacts saved under one name stay apart, and a later backup adds only what is new. See *From an iPhone backup* below.
- **Read** a conversation as a stream: your messages on the right, everyone else's on the left, photos shown inline, every other file one click away. A **calendar** shows each day of a conversation coloured by how much was said against its typical day; click a day to go to it. **Media** lists every file exchanged with that person or group, by kind.
- **Clean up as you read.** Pick messages (a click picks one, another unpicks it) and mark their media important or irrelevant, compress it (documents to text, pictures to a thumbnail or their words, videos to their first frame), delete only the media, or delete the messages; the same is on a right click. A conversation's settings (noisy, separate, delete) sit behind its gear button.
- **The same person under several numbers.** Chats of one person (an old number and a new one) are tied together without being merged: the person shows once in the list, and each chat history can be read alone, or all of them together as one continuous stream, as it never was in WhatsApp. Separate unties them. Chats of different accounts are never tied.
- **Files**: every file in an account, largest first, filtered by who sent it (or by everyone but that person).
- **One copy of each file.** Files are stored by their content, so the same photo sent to three people, or forwarded back to you, or exported from both your phone and your partner's, is stored once and every chat points at it.
- **Delete** conversations; a file goes with them only when no other chat still uses it, and never when it is marked important.
- **Important or irrelevant.** Every file stands on one of three lists: important (the precious things, kept whole), irrelevant (the noise), or unsorted. Vakage suggests a standing, with its reason: stickers, GIFs, forwarded pictures and anything sent around several chats look irrelevant; your own photos, and the photos and voice notes of close one-to-one chats, look important; screenshots are left for you. Your own mark always wins, and the Files tab shows each list, with a preview of every picture.
- **Reduce the noise.** An irrelevant picture that is mostly words is kept as its words; any other is kept as a small thumbnail; an irrelevant PDF is kept as its text; the original is deleted, and where it was in the chat a notice says so. Reading text needs Eidokanon (a Tekhnexos companion) on the computer; without it, pictures keep a thumbnail and PDFs stay whole.
- **Everything the phone knew.** Besides the messages and files: what each message replies to (shown above it), stars, link previews, contact cards, the length of voice notes and videos, the preview of a file the phone never downloaded, the address book and the names people gave themselves, profile pictures, call history, groups' members and creators, blocked contacts, and WhatsApp's raw per-message metadata and receipts, kept whole for anything a later version learns to read. Nothing of WhatsApp's own needs to be kept once a phone is imported.
- **Documents.** Vakage reads the words of every picture and PDF (through Eidokanon) and tells documents apart by them: payment receipts, bills, tickets, identity papers, contracts, medical and school papers. Old receipts and tickets are suggested irrelevant (their text is enough), identity, legal and medical papers important. The Files tab has a Documents list.
- **Noisy chats.** A chat of a relative's daily forwards can be marked noisy: its pictures are pruned hard, keeping what looks like a real photo (a camera's shape, no words) and voice notes.
- **Deleting is for good.** A chat deleted in Vakage stays out of later imports; channels (broadcast feeds) are left out of imports.
- **Made to be continued.** A later backup of the same phone joins on as if the archive had never been cut off: messages are matched by WhatsApp's own ids, times are kept in UTC, and a contact renamed, a chat moved to WhatsApp's new ids, or a computer in another time zone adds nothing twice. Nothing of WhatsApp's own is kept: everything is in Vakage's archive.

## Coming

- The other side of the same conversation: your partner's export of a chat with you, read beside yours.
- Making files smaller: lossless image conversion (PNG to WebP), shrinking oversized PDFs.
- Text reading without Eidokanon (Tesseract and pdftotext directly), so the open-source build reduces files on its own.
- Help from an AI model in deciding which files are worth keeping and which are noise.
- Other messaging apps, starting with iPhone Messages.

## Exporting a chat

- **Android:** open the chat, ⋮ → More → Export chat → Include media. Save the zip to your computer.
- **iPhone:** open the chat, tap the contact's name → Export Chat → Attach Media. Save the zip to your computer.

Then **Import** in Vakage, into the account of the phone it came from. The export itself is never changed; once you are happy with the import you can delete it, and the chat on the phone.

## From an iPhone backup

An iPhone backup made by Apple Devices, Finder or iTunes holds WhatsApp's whole database. Vakage reads it once it is out of the backup: the folder `group.net.whatsapp.WhatsApp.shared` (WhatsApp's shared container, holding `ChatStorage.sqlite` and `Message`). An encrypted backup has to be decrypted first, with its password, by a tool such as [iphone_backup_decrypt](https://github.com/jsharkey13/iphone_backup_decrypt) (domain `AppDomainGroup-group.net.whatsapp.WhatsApp.shared`). Then **Import folder** on that folder imports every chat, after asking, or from a terminal:

```powershell
Vakage --import-ios "<…>\group.net.whatsapp.WhatsApp.shared" --account Tiago --chat "Ana,+5511"   # only the chats whose name or number match
```

Times are WhatsApp's own (UTC), shown in the computer's time zone. Media the phone never downloaded is not in the backup, and shows as a file left out.

## Where things are

The library is a folder (by default `Documents\Vakage`; set `LibraryPath` in `%LOCALAPPDATA%\Vakage\settings.json` to move it):

```text
Vakage\
  vakage.sqlite                         accounts, chats, messages, files and their standing (one file, no journal beside it)
  files\ab\<sha256>.jpg                 each file once, named by its content
  thumbs\ab\<sha256>.jpg                thumbnails: of reduced files, and previews of files never downloaded
  people\ab\<sha256>.jpg                profile pictures
```

Files are never rewritten once stored, so the folder syncs and backs up well.

## Command line

```powershell
Vakage --import "WhatsApp Chat - Ana.zip" --account Tiago [--title "Ana"] [--me "Tiago"]
Vakage --import-ios <WhatsApp shared container> --account Tiago [--chat <name or number>,…]
Vakage --join 4,2,1 --title "Ana"                 # chat ids, as --list shows them
Vakage --list [--account Tiago]
Vakage --files --account Tiago [--list important|irrelevant|unsorted] [--sender Ana] [--top 50]
Vakage --suggest                                   # work out the suggested standings again
Vakage --mark important|irrelevant|none --file 12,13
Vakage --reduce --account Tiago [--file 12,13] --yes   # keep text or a thumbnail, DELETE the originals
Vakage --read-text --account Tiago [--chat 6] [--jobs 6] # read the words in pictures and PDFs (Eidokanon)
Vakage --noisy 6 [--off]                           # a chat of daily forwards
Vakage --group-same-names --account Tiago          # tie together one person's chats under different numbers
Vakage --delete 699,700 --yes                      # delete chats for good; later imports leave them out
```

Every command takes `--library <folder>` to work on another library.

## Building

Needs the .NET 9 SDK.

```powershell
dotnet build Vakage.slnx -c Release
dotnet test  Vakage.slnx -c Release
dotnet publish src\Vakage\Vakage.csproj -c Release -o publish
```

`src/Vakage.Core` (parsing, the library, the file store) has no UI dependency and carries the tests; `src/Vakage` is the Avalonia window. `install.bat` is the Tekhnexos family's installer and needs that family's scripts; anyone else uses `dotnet publish`.

## Credits

Roboto, Roboto Slab, Roboto Condensed and Roboto Mono (SIL Open Font License); Font Awesome Free 6 (font under the SIL OFL 1.1, icons under CC BY 4.0). Vakage is not affiliated with WhatsApp or Meta.

## Licence

MIT. See [LICENSE](LICENSE). Contributions are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md).

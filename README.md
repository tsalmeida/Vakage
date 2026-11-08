# Vakage

**An archive for your exported WhatsApp conversations.** Export a chat from your phone, import it into Vakage, and delete it from the phone: the conversation, its photos, voice notes and documents stay readable on your computer, in one place, under your control. Nothing is uploaded anywhere.

Stone icon (`#57534d`, Tailwind stone-600). A Tekhnexos companion app, C# and Avalonia, open source under the MIT licence.

## What it does now (0.1)

- **Accounts.** One account per person whose phone the exports come from (yours, your partner's), so two people's archives never mix. Switching accounts switches the whole window.
- **Import** an exported chat: the `.zip` WhatsApp makes with *Export chat → Include media*, the folder it was unzipped into, or the chat's `.txt` alone. Android and iPhone exports, in English, Portuguese, Spanish, German, French, Italian and Dutch, 24-hour and AM/PM clocks. A later export of the same chat adds only the messages that are new.
- **Import a whole phone** from an iPhone backup: WhatsApp's own database (`ChatStorage.sqlite` and its `Message\Media`), with every chat, exact times, documents under their real names and every file the phone downloaded. Contacts saved under one name stay apart, and a later backup adds only what is new. See *From an iPhone backup* below.
- **Read** a conversation as a stream: your messages on the right, everyone else's on the left, photos shown inline, every other file one click away.
- **Join** chats into one conversation: a contact's old number and new number, read as one continuous stream as it never was in WhatsApp. Unjoin parts them again. Chats of different accounts are never joined.
- **Files**: every file in an account, largest first, filtered by who sent it (or by everyone but that person).
- **One copy of each file.** Files are stored by their content, so the same photo sent to three people, or forwarded back to you, or exported from both your phone and your partner's, is stored once and every chat points at it.
- **Delete** conversations; a file goes with them only when no other chat still uses it.

## Coming

- The other side of the same conversation: your partner's export of a chat with you, read beside yours.
- Making files smaller: lossless image conversion (PNG to WebP), shrinking oversized PDFs.
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
  vakage.sqlite            accounts, chats, messages
  files\ab\<sha256>.jpg    each file once, named by its content
```

## Command line

```powershell
Vakage --import "WhatsApp Chat - Ana.zip" --account Tiago [--title "Ana"] [--me "Tiago"]
Vakage --import-ios <WhatsApp shared container> --account Tiago [--chat <name or number>,…]
Vakage --join 4,2,1 --title "Ana"                 # chat ids, as --list shows them
Vakage --list [--account Tiago]
Vakage --files --account Tiago [--sender Ana] [--top 50]
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

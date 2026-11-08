# Contributing to Vakage

Thank you for helping. Vakage holds people's private conversations, so two rules come before anything else:

1. **Never commit a real conversation.** No exports, no libraries, no screenshots of real chats, not even your own. Tests build their exports in code with made-up names (see `tests/Vakage.Tests`). `.gitignore` refuses the usual file names, but it is not a guarantee.
2. **Nothing leaves the computer** unless the person asks for it in so many words. Vakage makes no network calls today; a feature that needs one (an AI model's help with files, say) is opt-in and says what it sends.

## Working on it

```powershell
dotnet build Vakage.slnx -c Release   # expect 0 warnings
dotnet test  Vakage.slnx -c Release
```

- Parsing and storage belong in `src/Vakage.Core`, with a test; the window (`src/Vakage`) stays thin.
- An export format Vakage does not read yet (another language, another phone) is a welcome contribution: add a made-up sample of its lines to `WhatsAppChatParserTests` and make it pass.
- The look follows the Tekhnexos companion design standard: Roboto, Tailwind colours, pills with a Font Awesome icon in the button's hue (lime for positive actions, orange for destructive ones). `src/Vakage/Design` is copied from the reference implementation and is not edited here.
- Only Font Awesome **Free** glyphs may be used; the Pro face is not redistributable.
- Write commit messages as a plain sentence saying what changed, then why.

## Reporting a bug in an import

Describe the phone (Android or iPhone), its language, and paste a few lines of the chat file with the names and text replaced. The shape of the lines is what matters.

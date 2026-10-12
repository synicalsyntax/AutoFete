# Status text

`StatusText.resx` is the English source for automation status messages and stage labels. Its keys are grouped by type: `Error_` for failures, `Stop_` for stop reasons, `Progress_` for ongoing work, `Result_` for completed work, `Ui_` for labels, and `Target_` for item names and fallback descriptions used in messages. Visible game objects supply their own names. The .NET SDK embeds the resource automatically. `StatusText.cs` looks up entries using the game's client language and falls back to English until a translated resource exists.

To add a translation, create `StatusText.<language>.resx` in this folder, such as `StatusText.fr.resx`. Keep the same keys. Numbered placeholders like `{0}` represent values supplied by the caller; translations may move them, but must keep their numbers. For example, `Progress_DeliveringItemTo` uses `{0}` for the item and `{1}` for the recipient name.

using System.Collections.ObjectModel;
using Island.Core.Models;

namespace Island.Core.Config;

public static class DefaultConfig
{
    public static IslandConfig Create()
    {
        var favApps = ButtonItem.Create("Fav apps", IconSpec.Glyph("E734"), new GroupAction(),
            ButtonItem.Create("File Explorer", IconSpec.App(@"%WINDIR%\explorer.exe"),
                new LaunchAppAction { Path = @"%WINDIR%\explorer.exe" }),
            ButtonItem.Create("Notepad", IconSpec.App(@"%WINDIR%\System32\notepad.exe"),
                new LaunchAppAction { Path = @"%WINDIR%\System32\notepad.exe" }),
            ButtonItem.Create("Calculator", IconSpec.App(@"%WINDIR%\System32\calc.exe"),
                new LaunchAppAction { Path = @"%WINDIR%\System32\calc.exe" }));

        return new IslandConfig
        {
            SchemaVersion = IslandConfig.CurrentSchemaVersion,
            General = new GeneralSettings(),
            Buttons = new ObservableCollection<ButtonItem>
            {
                favApps,
                ButtonItem.Create("Claude Code here", IconSpec.Glyph("E756"), new ClaudeCodeHereAction()),
                ButtonItem.Create("Screenshot", IconSpec.Glyph("E722"), new ScreenshotAction { Mode = ScreenshotMode.Snip }),
                ButtonItem.Create("Browser", IconSpec.Glyph("E774"), new OpenUrlAction()),
                ButtonItem.Create("Settings", IconSpec.Glyph("E713"), new OpenSettingsAction()),
            },
        };
    }
}

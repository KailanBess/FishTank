using System;
using System.IO;
using System.Linq;
using System.Drawing;
using FishTank;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
string temp = Path.Combine(Path.GetTempPath(), "FishTank-appearance-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp); Core.Data = temp;
try {
    var defaults = AppearanceStore.Load(); defaults.Validate();
    Check(defaults.Theme == "Aquarium Ocean" && defaults.Accent == "Aquarium Mint" && defaults.BackgroundStyle == "Gradient", "New profile did not use family defaults");
    Check(!File.Exists(AppearanceStore.FilePath), "Loading defaults silently saved a profile");
    // Older saved profiles must retain their choices, including missing optional field defaults.
    File.WriteAllText(AppearanceStore.FilePath, "{\"Version\":1,\"Theme\":\"Light\",\"Accent\":\"Pink\",\"FontFamily\":\"Verdana\",\"ReducedMotion\":true}");
    var legacy = AppearanceStore.Load();
    Check(legacy.Theme == "Light" && legacy.Accent == "Pink" && legacy.ReducedMotion && legacy.FontFamily == "Verdana", "Old saved settings were overwritten by family defaults");
    legacy.CustomPalette = true; legacy.TextColor = "#ABCDEF"; legacy.WindowFrameColor = "#13579B"; legacy.TextScale = 145; legacy.AdvancedMode = true;
    legacy.CardsVisible[1] = false;
    var family = legacy.WithAquariumStyle(); family.Validate();
    Check(legacy.Theme == "Light" && legacy.CustomPalette, "Preset mutated source settings");
    Check(family.FontFamily == legacy.FontFamily && family.TextScale == 145 && family.ReducedMotion && family.AdvancedMode && !family.CardsVisible[1] && family.WindowFrameColor == "#13579B", "Preset discarded personal/accessibility settings");
    Check(!family.CustomPalette && family.TextColor == "#ABCDEF", "Preset lost saved custom color values");
    family.CardsVisible[0] = false;
    Check(legacy.CardsVisible[0], "Preset shares mutable arrays with source");
    AppearanceStore.Save(family);
    Check(AppearanceStore.Load().Theme == "Aquarium Ocean" && AppearanceStore.Load().TextScale == 145, "Preset save/reload failed");
    File.WriteAllText(AppearanceStore.FilePath, "damaged");
    var recovered = AppearanceStore.Load();
    Check(recovered.Theme == "Light" && AppearanceStore.Notice.Contains("recovered"), "Valid old backup not recovered");
    File.WriteAllText(AppearanceStore.FilePath + ".bak", "damaged backup");
    Check(AppearanceStore.Load().Theme == "Aquarium Ocean" && AppearanceStore.Notice.Contains("could not be read"), "Recovery fallback defaults inconsistent");
    Check(File.ReadAllText(AppearanceStore.FilePath) == "damaged", "Loading damaged settings overwrote original");
    var backgrounds = new System.Collections.Generic.HashSet<int>();
    foreach (string theme in new[] { "Aquarium Ocean", "Aquarium Midnight", "Aquarium Slate" })
    foreach (string accent in new[] { "Aquarium Mint", "Aquarium Blue", "Aquarium Coral" }) {
        var style = Appearance.Defaults(); style.Theme = theme; style.Accent = accent; style.Validate();
        var colors = Palette.From(style, false); backgrounds.Add(colors.Background.ToArgb());
        Check(colors.Accent == Palette.AccentColor(accent), "Selected family accent was ignored");
        style.Blending = "Original"; var original = Palette.From(style, false);
        Check(colors.Surface != original.Surface && colors.Surface2 != original.Surface2, "Family theme ignored blending control");
        style.CustomPalette = true; style.TextColor = "#ABCDEF"; style.SurfaceColor = "#102030"; style.AccentColor = "#EDCBA9";
        colors = Palette.From(style, false);
        Check(colors.Text.ToArgb() == Color.FromArgb(171,205,239).ToArgb() && colors.Surface.ToArgb() == Color.FromArgb(16,32,48).ToArgb(), "Family theme ignored custom overrides");
        var accessible = Palette.From(style, true);
        Check(accessible.Background == SystemColors.Window && accessible.Text == SystemColors.WindowText && accessible.AccentText == SystemColors.HighlightText, "High contrast did not override family/custom colors");
    }
    Check(backgrounds.Count == 3, "Family backgrounds are not distinct");
    foreach (string theme in Appearance.Themes) { var style = new Appearance { Theme = theme }; style.Validate(); Palette.From(style, false); checks++; }
    Console.WriteLine(checks + " appearance/persistence/palette checks passed; Windows system detection/rendering is not executed.");
} finally { Directory.Delete(temp, true); }

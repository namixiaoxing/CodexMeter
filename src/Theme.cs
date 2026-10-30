using System.Drawing;
namespace CodexMeter;

internal sealed record Palette(Color Background, Color Card, Color Text, Color Muted, Color Accent, Color Blue, Color Border, Color Track);

internal static class Theme
{
    public static readonly Color Miku = Color.FromArgb(57, 197, 187);
    public static readonly Color Low = Color.FromArgb(230, 80, 91);
    public static Palette Get(string? name) => name switch
    {
        "dark" => new(Color.FromArgb(21,24,30), Color.FromArgb(31,35,44), Color.FromArgb(235,239,245), Color.FromArgb(153,164,181), Miku, Color.FromArgb(135,185,250), Color.FromArgb(57,65,78), Color.FromArgb(52,61,72)),
        "white" => new(Color.White, Color.FromArgb(247,248,250), Color.FromArgb(39,43,49), Color.FromArgb(128,135,146), Color.FromArgb(38,125,102), Color.FromArgb(72,111,175), Color.FromArgb(225,228,234), Color.FromArgb(231,235,240)),
        _ => new(Color.FromArgb(213,240,239), Color.FromArgb(194,234,232), Color.FromArgb(28,68,69), Color.FromArgb(78,125,124), Color.FromArgb(26,147,140), Color.FromArgb(34,115,127), Color.FromArgb(147,217,219), Color.FromArgb(170,219,216))
    };
    // Full remaining capacity is Miku teal; danger smoothly increases below 40%.
    public static Color RemainingColor(double remaining)
    {
        var t = Math.Clamp((40 - remaining) / 40, 0, 1);
        return Color.FromArgb((int)Math.Round(Miku.R + (Low.R - Miku.R) * t), (int)Math.Round(Miku.G + (Low.G - Miku.G) * t), (int)Math.Round(Miku.B + (Low.B - Miku.B) * t));
    }
}

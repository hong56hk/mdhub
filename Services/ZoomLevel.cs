namespace Mdhub.Services;

// UI zoom as an integer percent so repeated steps never drift. Pure; unit tested.
public static class ZoomLevel
{
    public const int Min = 50, Max = 200, Step = 10, Default = 100;
    public static int Clamp(int percent) => Math.Clamp(percent, Min, Max);
    // Off-grid values (hand-edited session files) snap onto the 10% grid first.
    public static int Normalize(int percent) => Clamp((int)Math.Round(percent / (double)Step, MidpointRounding.AwayFromZero) * Step);
    public static int In(int percent) => Clamp(Normalize(percent) + Step);
    public static int Out(int percent) => Clamp(Normalize(percent) - Step);
    public static int Wheel(int percent, double deltaY) => deltaY > 0 ? In(percent) : deltaY < 0 ? Out(percent) : Normalize(percent);
    public static double Scale(int percent) => Normalize(percent) / 100.0;
    public static string Label(int percent) => $"Zoom {Normalize(percent)}%";
}
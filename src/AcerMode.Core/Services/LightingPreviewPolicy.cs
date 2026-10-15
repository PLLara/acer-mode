namespace AcerMode.Core;

// A prévia só vai ao teclado quando o perfil editado é o que está valendo agora.
public static class LightingPreviewPolicy
{
    public static bool ShouldSendLive(ThermalMode editing, ThermalMode? active, bool independent) =>
        independent || (active is { } a && a == editing);
}

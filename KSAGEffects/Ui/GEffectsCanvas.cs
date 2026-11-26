using Brutal.Numerics;
using KSA;

namespace KSAGEffects.Ui;

// GaugeCanvas-hosted panel. The HUD layout system owns position, size, and enabled
// state: the panel can be dragged in-game or moved in the HUD editor and persists
// through saved HUD layouts.
internal sealed class GEffectsCanvas : GaugeCanvas
{
    private GEffectsPlayerUi _ui;

    public GEffectsCanvas(GEffectsPlayerUi ui)
    {
        _ui = ui;
    }

    internal void SetUi(GEffectsPlayerUi ui)
    {
        _ui = ui;
    }

    // Programmatic size change for the collapse/expand toggles. Update(resetWindow)
    // makes the next OnDrawUi skip a frame so the following Begin counts as appearing
    // and applies the new rect; this is the same sequence ApplyLayout uses.
    internal void SetSizeUv(float sizeX, float sizeY)
    {
        float2 current = Size.ToUv();
        if (Math.Abs(current.X - sizeX) <= 0.0001f && Math.Abs(current.Y - sizeY) <= 0.0001f)
        {
            return;
        }
        Size = new UvReference(sizeX, sizeY) { Locked = Size.Locked };
        Size.OnDataLoad(Mod.Empty);
        OnDataLoad(Mod.Empty);
        CacheTransform();
        Update(GetPixelsMin(), GetPixelsSize(), resetWindow: true);
        MarkDirty();
    }

    protected override void DrawHostedContent(float2 windowPosPixels, float2 windowSizePixels)
    {
        _ui.DrawCanvasContent(this, windowPosPixels, windowSizePixels);
    }
}

using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSAGEffects.Ui;

internal sealed class GEffectsPlayerUi
{
    private static float InterfaceScale => ImGuiHelper.InterfaceScale;

    private static float MinWindowHeight => 240.0f * InterfaceScale;
    private static float CrewDetailWindowWidth => 380.0f * InterfaceScale;
    private static float CrewDetailWindowHeight => 460.0f * InterfaceScale;

    private bool _expanded;
    private bool _crewPanelOpen;
    private readonly Dictionary<string, bool> _crewDetailWindows = new();

    private KSAGEffectsLogicInstance? _instance;
    private string _crewStatusMessage = string.Empty;
    private IReadOnlyList<KSAGEffectsLogicInstance> _crew = [];

    internal void OpenExpanded()
    {
        _expanded = true;
    }

    internal void Draw(
        KSAGEffectsLogicInstance? instance,
        string crewStatusMessage,
        IReadOnlyList<KSAGEffectsLogicInstance> crew)
    {
        _instance = instance;
        _crewStatusMessage = crewStatusMessage;
        _crew = crew;
        DrawCrewDetailWindows(crew);
    }

    // Canvas sizes in screen UV fractions. The collapsed width shows only the status pane;
    // half of expanded keeps the status pane the same width when toggling.
    internal const float SizeUvXCollapsed = 0.245f;
    internal const float SizeUvXExpanded = 0.49f;
    internal const float SizeUvY = 0.28f;
    internal const float SizeUvYDetails = 0.40f;

    // Runs inside the GEffectsCanvas window during the gauge host scope; pos/size is the canvas rect in pixels.
    internal void DrawCanvasContent(GEffectsCanvas canvas, float2 pos, float2 size)
    {
        // Leave the gauge host scope while drawing: it pushes fonts and style vars scaled by
        // gauge x resolution x canvas-content scale, and ConsoleWidgets re-push that scaled
        // label font internally, so a single compensated push cannot cover them. Drawing
        // outside the scope gives stock console window styling; re-entering below with the
        // same hostScale keeps the scope stack balanced for the canvas.
        float hostScale = MathF.Max(0.01f,
            GameSettings.GetGaugeScale()
            * ScreenReference.ResolutionScale()
            * Math.Clamp(canvas.ContentScale, 0.6f, 3f));
        ConsoleStyle.EndGaugeHostScope();
        ConsoleStyle.PushLabelFont();

        ConsoleStyle.DrawPanelBezel(pos, size, ImDrawFlags.RoundCornersAll, raised: false);

        // Canvas windows have zero window padding; inset content by the stock console
        // body margin so the panel fill forms a visible border, like the GameTime window.
        float margin = ConsoleStyle.ContentPaddingPx;
        float2 inset = new(margin, margin);
        float headerHeight = ImGui.GetFrameHeight();
        float2 bodySize = size - inset * 2f - new float2(0f, headerHeight + margin);
        if (bodySize.X >= 40f && bodySize.Y >= 20f)
        {
            ImGui.GetWindowDrawList().AddText(
                new float2(pos.X + margin, pos.Y + margin + (headerHeight - ImGui.GetTextLineHeight()) * 0.5f),
                ConsoleStyle.TextMutedU32,
                "G EFFECTS");

            // Size the toggle exactly like DrawButtonCore does: text + horizontal button padding.
            float2 toggleSize = new(
                ImGui.CalcTextSize(">").X + ConsoleWidgets.ButtonHorizontalPaddingPx * 2f,
                headerHeight);
            ImGui.SetCursorScreenPos(new float2(pos.X + size.X - margin - toggleSize.X, pos.Y + margin));
            if (ConsoleWidgets.Button(_crewPanelOpen ? "<" : ">"))
            {
                _crewPanelOpen = !_crewPanelOpen;
                canvas.SetSizeUv(
                    _crewPanelOpen ? SizeUvXExpanded : SizeUvXCollapsed,
                    _expanded ? SizeUvYDetails : SizeUvY);
            }
            ImGuiHelper.DrawTooltip(_crewPanelOpen ? "Hide crew"u8 : "Show crew"u8, () => ImGui.IsItemHovered());

            float2 bodyMin = new(pos.X + margin, pos.Y + margin + headerHeight + margin);
            if (_crewPanelOpen)
            {
                DrawCrewLayout(canvas, _instance, _crewStatusMessage, _crew, bodyMin, bodySize);
            }
            else
            {
                ImGui.SetCursorScreenPos(bodyMin);
                ImGui.BeginChild("##GEffectsStatusPane", bodySize, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
                DrawStatusPane(canvas, _instance, _crewStatusMessage);
                ImGui.EndChild();
            }
        }

        ImGui.PopFont();
        ConsoleStyle.BeginGaugeHostScope(hostScale);
    }

    private void DrawCrewLayout(
        GEffectsCanvas canvas,
        KSAGEffectsLogicInstance? instance,
        string crewStatusMessage,
        IReadOnlyList<KSAGEffectsLogicInstance> crew,
        float2 bodyMin,
        float2 bodySize)
    {
        float gap = ConsoleStyle.ContentPaddingPx;
        float statusWidth = (bodySize.X - gap * 2f) * 0.5f;
        float rosterWidth = bodySize.X - statusWidth - gap * 2f;

        ImGui.SetCursorScreenPos(bodyMin);
        ImGui.BeginChild("##GEffectsStatusPane", new float2(statusWidth, bodySize.Y), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        DrawStatusPane(canvas, instance, crewStatusMessage);
        ImGui.EndChild();

        // Vertical divider between the panes, same hairline style as ConsoleWidgets.Rule.
        float dividerX = bodyMin.X + statusWidth + gap;
        ImGui.GetWindowDrawList().AddLine(
            new float2(dividerX, bodyMin.Y),
            new float2(dividerX, bodyMin.Y + bodySize.Y),
            ConsoleStyle.ApplyAlpha(ConsoleStyle.HairlineU32),
            ConsoleStyle.WindowBorderThicknessPx);

        ImGui.SetCursorScreenPos(new float2(dividerX + gap, bodyMin.Y));
        ImGui.BeginChild("##GEffectsCrewRoster", new float2(rosterWidth, bodySize.Y), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        DrawCrewRoster(crew, instance);
        ImGui.EndChild();
    }

    private void DrawStatusPane(GEffectsCanvas canvas, KSAGEffectsLogicInstance? instance, string crewStatusMessage)
    {
        ConsoleWidgets.RegionHeader("CREW CONTROL");
        ImGui.TextWrapped(Fmt(crewStatusMessage));

        if (instance == null)
        {
            ConsoleWidgets.Rule();
            ImGui.Text("No crew physiological data is available.");
        }
        else
        {
            DrawInstanceSummary(instance);
            if (_expanded)
            {
                DrawInstanceDetails(instance);
            }
        }

        if (_expanded)
        {
            if (ConsoleWidgets.Button("Hide details"))
            {
                _expanded = false;
                canvas.SetSizeUv(_crewPanelOpen ? SizeUvXExpanded : SizeUvXCollapsed, SizeUvY);
            }
        }
        else if (ConsoleWidgets.PrimaryButton("Show details"))
        {
            _expanded = true;
            canvas.SetSizeUv(_crewPanelOpen ? SizeUvXExpanded : SizeUvXCollapsed, SizeUvYDetails);
        }
    }

    private void DrawCrewRoster(IReadOnlyList<KSAGEffectsLogicInstance> crew, KSAGEffectsLogicInstance? primary)
    {
        ConsoleWidgets.RegionHeader("CREW");
        if (crew.Count == 0)
        {
            ImGui.Text("No crew assigned.");
            return;
        }

        foreach (KSAGEffectsLogicInstance member in crew)
        {
            ImGui.PushID(member.KittenId);
            if (ConsoleWidgets.Button(member.KittenId))
                _crewDetailWindows[member.KittenId] = true;
            if (ReferenceEquals(member, primary))
            {
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("main view");
            }
            if (member.IsUnconscious)
            {
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(ConsoleStyle.Danger, "unconscious");
            }
            if (!member.Enabled)
            {
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("fx off");
            }

            ImGui.TextDisabled(Fmt(
                $"consc. {member.ConsciousnessLevel:P0}   perf. {member.PhysModel.PerfusionLevel:P0}   O2 {member.BrainO2:P0}"));
            ImGui.Spacing();
            ImGui.PopID();
        }
    }

    private void DrawCrewDetailWindows(IReadOnlyList<KSAGEffectsLogicInstance> crew)
    {
        foreach (KSAGEffectsLogicInstance member in crew)
        {
            if (!_crewDetailWindows.TryGetValue(member.KittenId, out bool open) || !open)
                continue;

            ImGui.SetNextWindowSizeConstraints(
                new float2(CrewDetailWindowWidth, MinWindowHeight),
                new float2(CrewDetailWindowWidth, float.MaxValue));
            if (ConsoleStyle.BeginWindow(
                "KSAGEffectsCrewDetail_" + member.KittenId,
                member.KittenId,
                "GFX",
                ref open,
                new float2(CrewDetailWindowWidth, CrewDetailWindowHeight),
                ImGuiWindowFlags.None,
                ImGuiCond.FirstUseEver))
            {
                ConsoleStyle.BeginBody();
                ConsoleStyle.PushWidgetStyle();
                DrawInstanceSummary(member);
                DrawInstanceDetails(member);
                ConsoleWidgets.Rule();
                ImGui.PushID(member.KittenId);
                if (member.Enabled && ConsoleWidgets.DangerButton("Disable")) member.Enabled = false;
                else if (!member.Enabled && ConsoleWidgets.PositiveButton("Enable")) member.Enabled = true;
                ImGui.SameLine();
                if (ConsoleWidgets.Button("Reset")) member.Reset();
                ImGui.PopID();
                ConsoleStyle.PopWidgetStyle();
                ConsoleStyle.EndBody();
                ConsoleStyle.BeginFooter();
                ConsoleStyle.FooterStatus("CREW DETAIL", pending: false);
                ConsoleStyle.EndFooter();
                ConsoleStyle.EndWindow();
            }
            _crewDetailWindows[member.KittenId] = open;
        }
    }

    private static void DrawInstanceSummary(KSAGEffectsLogicInstance instance)
    {
        ConsoleWidgets.Rule();
        ConsoleWidgets.Readout("Crew member", instance.KittenId);
        ConsoleWidgets.Readout("Current G load", $"{ResultantLoad(instance):F2} G");
        ConsoleWidgets.Readout("G-effects", instance.Enabled ? "Active" : "Disabled");
        DrawStatusBar("Consciousness", instance.ConsciousnessLevel);
        DrawStatusBar("Perfusion", instance.PhysModel.PerfusionLevel);
        DrawStatusBar("Brain oxygen", instance.BrainO2);
    }

    private static void DrawInstanceDetails(KSAGEffectsLogicInstance instance)
    {
        ConsoleWidgets.Rule();
        ConsoleWidgets.RegionHeader("G-FORCE INPUTS");
        ConsoleWidgets.Readout("Gx", $"{instance.LastGx:F2}");
        ConsoleWidgets.Readout("Gy", $"{instance.LastGy:F2}");
        ConsoleWidgets.Readout("Gz", $"{instance.LastGz:F2}");
        ConsoleWidgets.Rule();
        ConsoleWidgets.RegionHeader("VISUAL EFFECTS");
        ConsoleWidgets.Readout("Tunnel vision", Fmt($"{instance.VisualTunnelVisionLevel:P0}"));
        ConsoleWidgets.Readout("Redout", Fmt($"{instance.VisualRedoutLevel:P0}"));
        ConsoleWidgets.Readout("Grayscale", Fmt($"{instance.VisualGrayscaleLevel:P0}"));
        ConsoleWidgets.Readout("Blur", Fmt($"{instance.VisualBlurLevel:P0}"));
        ConsoleWidgets.Readout("Film grain", Fmt($"{instance.VisualFilmGrainLevel:P0}"));
        ConsoleWidgets.Rule();
        ConsoleWidgets.Readout("Simulation", instance.Enabled ? "Active" : "Paused (effects disabled)");
        ConsoleWidgets.Readout("Consciousness", instance.IsUnconscious ? "Unconscious" : "Conscious");
    }

    // Brutal's ImGui text APIs treat strings as printf format strings, so literal percent signs must be doubled.
    private static string Fmt(string text) => text.Replace("%", "%%");

    private static void DrawStatusBar(string label, double value)
    {
        double normalized = Math.Clamp(value, 0.0, 1.0);
        float4 color = normalized <= 0.25
            ? ConsoleStyle.Danger
            : normalized <= 0.6
                ? ConsoleStyle.Pending
                : ConsoleStyle.Positive;
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, in color);
        ImGui.ProgressBar((float)normalized, overlay: "");
        ImGui.PopStyleColor();

        string percentage = $"{normalized:P0}";
        float2 labelSize = ImGui.CalcTextSize(label);
        float2 percentageSize = ImGui.CalcTextSize(percentage);
        float2 barMin = ImGui.GetItemRectMin();
        float2 barMax = ImGui.GetItemRectMax();
        float textY = barMin.Y + (barMax.Y - barMin.Y - labelSize.Y) * 0.5f;
        const float horizontalPadding = 4.0f;
        const float verticalPadding = 1.0f;
        float2 labelMin = new(barMin.X + horizontalPadding, textY);
        float2 percentageMin = new(barMax.X - percentageSize.X - horizontalPadding, textY);
        float2 labelBackgroundMin = new(labelMin.X - 2.0f, textY - verticalPadding);
        float2 labelBackgroundMax = new(labelMin.X + labelSize.X + 2.0f, textY + labelSize.Y + verticalPadding);
        float2 percentageBackgroundMin = new(percentageMin.X - 2.0f, textY - verticalPadding);
        float2 percentageBackgroundMax = new(percentageMin.X + percentageSize.X + 2.0f, textY + percentageSize.Y + verticalPadding);
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        ImColor8 textBackground = new(0, 0, 0, 160);
        ImColor8 textColor = new(255, 255, 255, 255);
        drawList.AddRectFilled(labelBackgroundMin, labelBackgroundMax, textBackground);
        drawList.AddRectFilled(percentageBackgroundMin, percentageBackgroundMax, textBackground);
        drawList.AddText(labelMin, textColor, label);
        drawList.AddText(percentageMin, textColor, percentage);
    }

    internal static double ResultantLoad(KSAGEffectsLogicInstance instance) =>
        Math.Sqrt(instance.LastGx * instance.LastGx + instance.LastGy * instance.LastGy + instance.LastGz * instance.LastGz);
}

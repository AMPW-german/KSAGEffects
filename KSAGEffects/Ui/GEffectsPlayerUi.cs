using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSAGEffects.Ui;

internal sealed class GEffectsPlayerUi
{
    private static float InterfaceScale => ImGuiHelper.InterfaceScale;

    private static float ExpandedWindowWidth => 780.0f * InterfaceScale;
    private static float WindowHeight => 400.0f * InterfaceScale;
    private static float WindowHeightExpanded => 560.0f * InterfaceScale;
    private static float MinWindowHeight => 240.0f * InterfaceScale;
    private static float CrewDetailWindowWidth => 380.0f * InterfaceScale;
    private static float CrewDetailWindowHeight => 460.0f * InterfaceScale;
    private const float WindowLeftFraction = 0.235f;
    private const float WindowBottomFraction = 0.115f;

    private bool _showWindow = true;
    private bool _expanded;
    private bool _resizeOnNextFrame;
    private bool _crewPanelOpen;
    private readonly Dictionary<string, bool> _crewDetailWindows = new();

    // Matches the status pane width in the expanded layout so toggling the crew panel does not reflow it.
    private static float CollapsedWindowWidth
    {
        get
        {
            float padding = ConsoleStyle.ContentPaddingPx;
            float bodyWidth = ExpandedWindowWidth - padding * 2f;
            return (bodyWidth - padding) * 0.5f + padding * 2f;
        }
    }

    internal void OpenExpanded()
    {
        _showWindow = true;
        _expanded = true;
        _resizeOnNextFrame = true;
    }

    internal void Draw(
        Vehicle? vehicle,
        KSAGEffectsLogicInstance? instance,
        string crewStatusMessage,
        IReadOnlyList<KSAGEffectsLogicInstance> crew)
    {
        float windowWidth = _crewPanelOpen ? ExpandedWindowWidth : CollapsedWindowWidth;
        float windowHeight = _expanded ? WindowHeightExpanded : WindowHeight;
        if (_showWindow)
        {
            ImGui.SetNextWindowPos(
                InitialWindowPosition(new float2(windowWidth, windowHeight)),
                ImGuiCond.Appearing);
            ImGui.SetNextWindowSizeConstraints(
                new float2(windowWidth, MinWindowHeight),
                new float2(windowWidth, float.MaxValue));
        }
        if (_showWindow && ConsoleStyle.BeginWindow(
            "KSAGEffectsPlayer",
            "G EFFECTS",
            "GFX",
            ref _showWindow,
            new float2(windowWidth, windowHeight),
            ImGuiWindowFlags.None,
            _resizeOnNextFrame ? ImGuiCond.Always : ImGuiCond.FirstUseEver))
        {
            _resizeOnNextFrame = false;
            DrawCrewPanelToggle();

            ConsoleStyle.BeginBody();
            ConsoleStyle.PushWidgetStyle();
            if (_crewPanelOpen)
            {
                DrawCrewLayout(instance, crewStatusMessage, crew);
            }
            else
            {
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, ConsoleStyle.ScaledWindowPaddingPx);
                DrawStatusPane(instance, crewStatusMessage);
                ImGui.PopStyleVar();
            }
            ConsoleStyle.PopWidgetStyle();
            ConsoleStyle.EndBody();
            ConsoleStyle.BeginFooter();
            ConsoleStyle.FooterStatus("PHYSIOLOGICAL STATUS", pending: false);
            ConsoleStyle.EndFooter();
            ConsoleStyle.EndWindow();
        }

        DrawCrewDetailWindows(crew);
    }

    // Bottom area of the viewport work area, to the right of the stock left-side HUD column.
    private static float2 InitialWindowPosition(float2 windowSize)
    {
        var viewport = ImGui.GetMainViewport();
        return new float2(
            viewport.WorkPos.X + viewport.WorkSize.X * WindowLeftFraction,
            viewport.WorkPos.Y + viewport.WorkSize.Y - viewport.WorkSize.Y * WindowBottomFraction - windowSize.Y);
    }

    private void DrawCrewPanelToggle()
    {
        float buttonSize = ConsoleStyle.CloseButtonSizePx;
        float2 windowPos = ImGui.GetWindowPos();
        float2 windowSize = ImGui.GetWindowSize();
        float2 buttonPos = new float2(
            windowPos.X + windowSize.X
                - ConsoleStyle.TitlebarHorizontalPaddingPx
                - ConsoleStyle.CloseButtonSizePx
                - ConsoleStyle.TitlebarSignatureGapPx
                - buttonSize,
            windowPos.Y + (ConsoleStyle.TitlebarHeight - buttonSize) * 0.5f);
        float2 cursor = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(buttonPos);
        if (ConsoleWidgets.Button(_crewPanelOpen ? "<" : ">", "GFXCrewPanel", new float2(buttonSize, buttonSize)))
        {
            _crewPanelOpen = !_crewPanelOpen;
            _resizeOnNextFrame = true;
        }
        ImGui.SetCursorScreenPos(cursor);
    }

    private void DrawCrewLayout(
        KSAGEffectsLogicInstance? instance,
        string crewStatusMessage,
        IReadOnlyList<KSAGEffectsLogicInstance> crew)
    {
        float2 avail = ImGui.GetContentRegionAvail();
        float gap = ConsoleStyle.ContentPaddingPx;
        float statusWidth = (avail.X - gap) * 0.5f;
        float rosterWidth = avail.X - statusWidth - gap;
        float2 panePos = ImGui.GetCursorScreenPos();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, ConsoleStyle.ScaledWindowPaddingPx);
        ImGui.BeginChild("##GEffectsStatusPane", new float2(statusWidth, avail.Y), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        ImGui.PopStyleVar();
        DrawStatusPane(instance, crewStatusMessage);
        ImGui.EndChild();

        ImGui.SetCursorScreenPos(new float2(panePos.X + statusWidth + gap, panePos.Y));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, ConsoleStyle.ScaledWindowPaddingPx);
        ImGui.BeginChild("##GEffectsCrewRoster", new float2(rosterWidth, avail.Y), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoBackground);
        ImGui.PopStyleVar();
        DrawCrewRoster(crew, instance);
        ImGui.EndChild();
    }

    private void DrawStatusPane(KSAGEffectsLogicInstance? instance, string crewStatusMessage)
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
                _resizeOnNextFrame = true;
            }
        }
        else if (ConsoleWidgets.PrimaryButton("Show details"))
        {
            _expanded = true;
            _resizeOnNextFrame = true;
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

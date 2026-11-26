using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSAGEffects.Ui;

internal sealed class GEffectsDebugUi
{
    private long _copyConfirmationUntil;
    private string? _copyConfirmation;

    private bool _isOpen;

    internal bool IsOpen
    {
        get => _isOpen;
        set => _isOpen = value;
    }

    internal string? SelectedVehicleId { get; private set; }

    internal string? SelectedKittenId { get; private set; }

    internal void Draw()
    {
        if (!IsOpen) return;

        List<Vehicle> vehicles = [];
        if (Universe.CurrentSystem is { } currentSystem)
            foreach (Vehicle vehicle in currentSystem.All.OfType<Vehicle>())
                vehicles.Add(vehicle);
        vehicles.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        if (SelectedVehicleId == null || vehicles.All(vehicle => vehicle.Id != SelectedVehicleId))
            SelectedVehicleId = vehicles.FirstOrDefault()?.Id;
        Vehicle? selectedVehicle = vehicles.FirstOrDefault(vehicle => vehicle.Id == SelectedVehicleId);
        List<KittenRosterEntryData> crew = selectedVehicle == null
            ? []
            : Universe.KittenRoster.Kittens
                .Where(entry => !entry.Kia && entry.AssignedVehicleId == selectedVehicle.Id)
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ToList();
        if (SelectedKittenId == null || crew.All(entry => entry.Name != SelectedKittenId))
            SelectedKittenId = crew.FirstOrDefault()?.Name;

        if (!ConsoleStyle.BeginWindow(
            "KSAGEffectsDiagnostics",
            "G EFFECTS DIAGNOSTICS",
            "GFX-DIAG",
            ref _isOpen,
            new float2(1180.0f, 700.0f),
            ImGuiWindowFlags.None,
            ImGuiCond.FirstUseEver))
            return;

        ConsoleStyle.BeginBody();
        ConsoleStyle.PushWidgetStyle();
        if (ImGui.BeginTable("GEffectsDiagnosticsLayout", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Saved vessels", ImGuiTableColumnFlags.WidthFixed, 220.0f);
            ImGui.TableSetupColumn("Crew diagnostics", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawVesselSidebar(vehicles, crew);
            ImGui.TableNextColumn();
            DrawCrewDetails(selectedVehicle, crew);
            ImGui.EndTable();
        }
        ConsoleStyle.PopWidgetStyle();
        ConsoleStyle.EndBody();
        ConsoleStyle.BeginFooter();
        string footerStatus = _copyConfirmation != null && Environment.TickCount64 <= _copyConfirmationUntil
            ? _copyConfirmation
            : "SAVE-WIDE CREW DIAGNOSTICS";
        ConsoleStyle.FooterStatus(footerStatus, pending: false);
        ConsoleStyle.EndFooter();
        ConsoleStyle.EndWindow();
    }

    private void DrawVesselSidebar(List<Vehicle> vehicles, List<KittenRosterEntryData> selectedCrew)
    {
        if (vehicles.Count == 0)
        {
            ImGui.Text("No saved vessels in the current system.");
            return;
        }

        foreach (Vehicle vehicle in vehicles)
        {
            int crewCount = vehicle.Id == SelectedVehicleId
                ? selectedCrew.Count
                : Universe.KittenRoster.Kittens.Count(entry => !entry.Kia && entry.AssignedVehicleId == vehicle.Id);
            ImGui.PushID(vehicle.Id);
            if (ConsoleWidgets.Button($"{vehicle.Id} ({crewCount})"))
            {
                SelectedVehicleId = vehicle.Id;
                SelectedKittenId = null;
            }
            ImGui.PopID();
        }
    }

    private void DrawCrewDetails(Vehicle? vehicle, List<KittenRosterEntryData> crew)
    {
        if (vehicle == null)
        {
            ImGui.Text("Select a saved vessel.");
            return;
        }

        ImGui.Text($"Vessel: {vehicle.Id}");
        if (crew.Count == 0)
        {
            ImGui.Text("No assigned crew.");
            return;
        }

        if (ImGui.BeginTable("GEffectsCrewTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Crew");
            ImGui.TableSetupColumn("G axes / total");
            ImGui.TableSetupColumn("Conscious");
            ImGui.TableSetupColumn("Perfusion");
            ImGui.TableSetupColumn("Brain O2");
            ImGui.TableSetupColumn("State");
            ImGui.TableSetupColumn("Enabled");
            ImGui.TableHeadersRow();

            foreach (KittenRosterEntryData entry in crew)
            {
                global::KSAGEffects.KSAGEffects.GEffectsInstances.TryGetValue(entry.Name, out KSAGEffectsLogicInstance? instance);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.PushID(entry.Name);
                if (ConsoleWidgets.Button(entry.Name)) SelectedKittenId = entry.Name;
                ImGui.PopID();
                ImGui.TableNextColumn();
                ImGui.Text(instance == null ? "—" : $"{instance.LastGx:F2}/{instance.LastGy:F2}/{instance.LastGz:F2} ({GEffectsPlayerUi.ResultantLoad(instance):F2})");
                ImGui.TableNextColumn();
                ImGui.Text(instance == null ? "—" : $"{instance.ConsciousnessLevel:P0}");
                ImGui.TableNextColumn();
                ImGui.Text(instance == null ? "—" : $"{instance.PhysModel.PerfusionLevel:P0}");
                ImGui.TableNextColumn();
                ImGui.Text(instance == null ? "—" : $"{instance.BrainO2:P0}");
                ImGui.TableNextColumn();
                ImGui.Text(GetInstanceState(instance));
                ImGui.TableNextColumn();
                ImGui.Text(instance == null ? "—" : (instance.Enabled ? "Yes" : "No"));
            }

            ImGui.EndTable();
        }

        KittenRosterEntryData? selectedEntry = crew.FirstOrDefault(entry => entry.Name == SelectedKittenId);
        if (selectedEntry == null) return;
        ConsoleWidgets.Rule();
        ImGui.Text($"Selected crew member: {selectedEntry.Name}");
        if (!global::KSAGEffects.KSAGEffects.GEffectsInstances.TryGetValue(selectedEntry.Name, out KSAGEffectsLogicInstance? selectedInstance) ||
            selectedInstance == null)
        {
            ImGui.Text("Not initialized; no physiological state is available.");
            return;
        }

        ImGui.Text(GetInstanceState(selectedInstance));
        ImGui.Text($"Last input: Gx {selectedInstance.LastGx:F3}, Gy {selectedInstance.LastGy:F3}, Gz {selectedInstance.LastGz:F3}");
        ImGui.Text($"Consciousness {selectedInstance.ConsciousnessLevel:P1} | perfusion {selectedInstance.PhysModel.PerfusionLevel:P1} | brain O2 {selectedInstance.BrainO2:P1}");
        ImGui.Text($"Tunnel vision {selectedInstance.VisualTunnelVisionLevel:P1} | redout {selectedInstance.VisualRedoutLevel:P1} | LoC {selectedInstance.VisualLoCLevel:P1}");
        ImGui.Text($"Time {selectedInstance.Time:F2}s | stable {selectedInstance.IsStable} | dead {selectedInstance.IsDead}");

        ImGui.PushID(selectedEntry.Name);
        if (selectedInstance.Enabled && ConsoleWidgets.DangerButton("Disable")) selectedInstance.Enabled = false;
        else if (!selectedInstance.Enabled && ConsoleWidgets.PositiveButton("Enable")) selectedInstance.Enabled = true;
        ImGui.SameLine();
        if (ConsoleWidgets.Button("Reset")) selectedInstance.Reset();
        ImGui.SameLine();
        if (ConsoleWidgets.PrimaryButton("Copy diagnostics"))
        {
            ImGui.SetClipboardText(selectedInstance.CreateDiagnosticDump());
            _copyConfirmation = $"Diagnostics copied for {selectedEntry.Name}.";
            _copyConfirmationUntil = Environment.TickCount64 + 2000;
        }
        ImGui.PopID();
    }

    private static string GetInstanceState(KSAGEffectsLogicInstance? instance)
    {
        if (instance == null) return "Not initialized";
        if (!instance.Enabled) return "Effects disabled";
        if (!instance.IsUpdateRecent) return "Stale / not updating";
        return instance.IsUnconscious ? "Unconscious" : "Active";
    }
}

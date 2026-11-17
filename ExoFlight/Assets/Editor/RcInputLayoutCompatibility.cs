using System;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace ExoFlight.EditorTools
{
    /// <summary>
    /// OpenXR 1.16.1 registers all editor layouts, even disabled features. Its DPad
    /// device shadows Input System's case-insensitive Dpad control and breaks Gamepad.
    /// Restore only this known collision while the optional D-pad XR feature is disabled.
    /// Player builds register only enabled profiles and do not need this workaround.
    /// </summary>
    [InitializeOnLoad]
    public static class RcInputLayoutCompatibility
    {
        static bool repairing;
        static RcInputLayoutCompatibility()
        {
            InputSystem.onLayoutChange += LayoutChanged;
            EditorApplication.update += FirstUpdate;
        }
        static void FirstUpdate() { EditorApplication.update -= FirstUpdate; Repair(); }
        static void LayoutChanged(string name, InputControlLayoutChange change)
        {
            if (string.Equals(name, "Dpad", StringComparison.OrdinalIgnoreCase)) Repair();
        }
        public static void Repair()
        {
            if (repairing) return;
            var settings = OpenXRSettings.ActiveBuildTargetInstance;
            var feature = settings != null ? settings.GetFeature<DPadInteraction>() : null;
            if (feature != null && feature.enabled) return;
            var layout = InputSystem.LoadLayout("Dpad");
            if (layout == null || layout.type != typeof(DPadInteraction.DPad)) return;
            repairing = true;
            try { InputSystem.RegisterLayout<DpadControl>("Dpad"); }
            finally { repairing = false; }
        }
    }
}

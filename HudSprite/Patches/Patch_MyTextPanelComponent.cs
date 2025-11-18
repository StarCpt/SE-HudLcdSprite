using HarmonyLib;
using Sandbox.Game.Entities.Blocks;
using VRage.Game.GUI.TextPanel;

namespace HudSprite.Patches;

[HarmonyPatch(typeof(MyTextPanelComponent))]
public class Patch_MyTextPanelComponent
{
    [HarmonyPatch(nameof(MyTextPanelComponent.UpdateAfterSimulation))]
    [HarmonyPrefix]
    public static void UpdateAfterSimulation_Prefix(MyTextPanelComponent __instance, ref bool isWorking, ref bool isInRange)
    {
        isInRange |= isWorking && Plugin.ActiveSurfaces.Contains(__instance) && __instance.ContentType is ContentType.SCRIPT;
    }
}

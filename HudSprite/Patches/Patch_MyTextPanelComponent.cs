using HarmonyLib;
using Sandbox.Game.Components;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Collections;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace HudSprite.Patches;

[HarmonyPatch(typeof(MyRenderComponentScreenAreas))]
public class Patch_MyRenderComponentScreenAreas
{
    private static readonly HashSet<(long, int)> _surfaces = [];
    public static readonly ConcurrentDictionary<(long, int), (MySprite[] Sprites, Vector2I TextureSize, Vector2 AspectRatio, Color BackgroundColor, byte BackgroundAlpha)> SpriteDict = [];

    [HarmonyPatch(nameof(MyRenderComponentScreenAreas.RenderSpritesToTexture))]
    [HarmonyPostfix]
    public static void RenderSpritesToTexture_Postfix(MyRenderComponentScreenAreas __instance, int area, ListReader<MySprite> sprites, Vector2I textureSize, Vector2 aspectRatio, Color backgroundColor, byte backgroundAlpha)
    {
        if (_surfaces.Contains((__instance.Entity.EntityId, area)))
        {
            SpriteDict[(__instance.Entity.EntityId, area)] = (sprites.ToArray(), textureSize, aspectRatio, backgroundColor, backgroundAlpha);
        }
    }

    public static void RegisterSurface(long entityId, int area) => _surfaces.Add((entityId, area));
    public static void UnregisterSurface(long entityId, int area)
    {
        _surfaces.Remove((entityId, area));
        SpriteDict.Remove((entityId, area));
    }
}

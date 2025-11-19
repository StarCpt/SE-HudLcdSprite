using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRage.Plugins;

namespace HudSprite;

public partial class Plugin : IPlugin
{
    const string HUDLCD_TAG = "hudlcd";
    public const string OFFSCREEN_TEX_PREFIX = "HUDSPRITE_";
    static readonly char[] _newline = ['\n'];

    public void Init(object gameInstance)
    {
        new Harmony(GetType().FullName).PatchAll(Assembly.GetExecutingAssembly());
    }

    private int _lastLcdGather = -1;
    private readonly List<MyTextPanelComponent> _lcdsToRemove = [];

    public void Update()
    {
        if (MySession.Static is null || !MySession.Static.Ready)
        {
            ClearLcds();
            _lastLcdGather = -1;
            return;
        }

        if (MySession.Static.ControlledEntity is MyCockpit cockpit)
        {
            if (MySession.Static.GameplayFrameCounter - 100 > _lastLcdGather)
            {
                GatherNewLcds(cockpit.CubeGrid);
                _lastLcdGather = MySession.Static.GameplayFrameCounter;
            }
        }
        else
        {
            ClearLcds();
            _lastLcdGather = -1;
        }

        if (Surfaces.Count > 0)
        {
            // update surfaces
            bool update10 = MySession.Static.GameplayFrameCounter % 10 == 0;
            foreach (var data in Surfaces.Values)
            {
                if (!data.Update(update10))
                {
                    _lcdsToRemove.Add(data.Comp);
                }
            }

            // remove invalid lcds
            for (int i = _lcdsToRemove.Count - 1; i >= 0; i--)
            {
                if (Surfaces.TryRemove(_lcdsToRemove[i], out var data))
                {
                    data.Dispose();
                }
                _lcdsToRemove.RemoveAt(i);
            }
        }
    }

    public static readonly ConcurrentDictionary<MyTextPanelComponent, HudSpriteSurface> Surfaces = [];
    public static readonly HashSet<string> CreatedTextures = [];

    private static void GatherNewLcds(MyCubeGrid grid)
    {
        foreach (var block in grid.GetFatBlocks<MyTerminalBlock>())
        {
            if (block.CustomData == null || block is not Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider surfaceProvider)
            {
                continue;
            }

            string[] lines = block.CustomData.ToLower().Split(_newline, StringSplitOptions.RemoveEmptyEntries);
            int surfaceIndex = 0;
            foreach (var line in lines)
            {
                if (surfaceIndex >= Math.Max(1, surfaceProvider.SurfaceCount))
                    break;

                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(HUDLCD_TAG))
                    continue;

                if (surfaceProvider.GetSurface(surfaceIndex) is not MyTextPanelComponent surface)
                    continue;

                if (Surfaces.ContainsKey(surface))
                    continue;

                var data = new HudSpriteSurface(surfaceProvider, surface, surfaceIndex);
                Surfaces.TryAdd(surface, data);
                data.UpdateSettings();

                surfaceIndex++;
            }
        }
    }

    private static void ClearLcds()
    {
        if (Surfaces.Count > 0)
        {
            Surfaces.Values.ForEach(i => i.Dispose());
            Surfaces.Clear();
        }
    }

    public void Dispose()
    {
        ClearLcds();
    }
}

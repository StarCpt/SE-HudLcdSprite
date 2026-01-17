using HarmonyLib;
using HudSprite.Config;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VRage.FileSystem;
using VRage.Plugins;

namespace HudSprite;

public partial class Plugin : IPlugin
{
    const string HUDLCD_TAG = "hudlcd";
    public const string OFFSCREEN_TEX_PREFIX = "HUDSPRITE_";
    static readonly char[] _newline = ['\n'];

    public static HudSpriteSettings Settings = null!;

    public Plugin()
    {
        Settings = HudSpriteSettings.LoadOrCreate(Path.Combine(MyFileSystem.UserDataPath, "Storage", "HudSpriteSettings.json"));
    }

    public void Init(object gameInstance)
    {
        new Harmony(GetType().FullName).PatchAll(Assembly.GetExecutingAssembly());
    }

    public void OpenConfigDialog() => MyGuiSandbox.AddScreen(new GuiScreenHudSpriteConfig(Settings));

    private int _lastLcdGather = -1;
    private readonly List<MyTextPanelComponent> _lcdsToRemove = [];
    private static readonly HashSet<long> _blocksToNotDraw = [];

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
            if (block is not IMyTextSurfaceProvider surfaceProvider)
            {
                continue;
            }

            if (block is MyTextPanel panel && panel.PublicTitle.ToString().StartsWith(HUDLCD_TAG))
            {
                if (TryAddSurface(surfaceProvider, 0))
                {
                    continue;
                }
            }

            string[] lines = block.CustomData.ToLower().Split(_newline, StringSplitOptions.RemoveEmptyEntries);
            int surfaceIndex = 0;
            foreach (var line in lines)
            {
                if (surfaceIndex >= Math.Max(1, surfaceProvider.SurfaceCount))
                    break;

                bool tagFound = Settings.ScanAllText ? line.Contains(HUDLCD_TAG) : line.StartsWith(HUDLCD_TAG);
                if (string.IsNullOrWhiteSpace(line) || !tagFound)
                    continue;

                if (TryAddSurface(surfaceProvider, surfaceIndex))
                {
                    surfaceIndex++;
                }
            }
        }

        static bool TryAddSurface(IMyTextSurfaceProvider surfaceProvider, int surfaceIndex)
        {
            if (surfaceProvider.GetSurface(surfaceIndex) is not MyTextPanelComponent surface)
                return false;

            if (Surfaces.ContainsKey(surface))
                return false;

            var data = new HudSpriteSurface(surfaceProvider, surface, surfaceIndex);
            data.UpdateSettings();
            Surfaces.TryAdd(surface, data);
            return true;
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

    /// <summary>
    /// Overrides the draw state of a text panel surface.
    /// </summary>
    /// <param name="blockId">Entity id of the block.</param>
    /// <param name="shouldDraw">True by default, set false to disable hud visibility for surfaces owned by this block.</param>
    public static void SetDrawState(long blockId, bool shouldDraw)
    {
        if (!shouldDraw)
        {
            _blocksToNotDraw.Add(blockId);
        }
        else
        {
            _blocksToNotDraw.Remove(blockId);
        }
    }

    public void Dispose()
    {
        ClearLcds();
    }
}

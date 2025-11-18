using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.World;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VRage.Collections;
using VRage.Plugins;
using VRageMath;

namespace HudSprite;

public class LcdConfig
{
    public MyTextPanel Block;
    public Sandbox.ModAPI.Ingame.IMyTextSurface Surface;
    public Vector2D TopLeft;
    public double Scale;
}

public class Plugin : IPlugin
{
    const string HUDLCD_TAG = "hudlcd";
    static readonly char _configSeparator = ':';
    static readonly Vector2D _defaultPos = new Vector2D(-0.98, -0.2);
    static readonly double _defaultScale = 0.8;

    public void Init(object gameInstance)
    {
        new Harmony(GetType().FullName).PatchAll(Assembly.GetExecutingAssembly());
    }

    int _counter = 0;

    public void Update()
    {
        _counter++;

        if (MySession.Static is null || !MySession.Static.Ready)
            return;

        if (MySession.Static?.ControlledEntity is MyCockpit cockpit && !cockpit.Closed)
        {
            bool update10 = _counter % 10 == 0;
            if (update10)
            {
                ClearLCDs();
                UpdateLCDs(cockpit.CubeGrid);
            }

        }
        else
        {
            ClearLCDs();
        }
    }

    public static MyConcurrentList<LcdConfig> Lcds = [];

    private static void UpdateLCDs(MyCubeGrid grid)
    {
        // read custom data
        foreach (var lcd in grid.GetFatBlocks<MyTextPanel>())
        {
            if (lcd.CustomData == null)
            {
                continue;
            }

            string[] lines = lcd.CustomData.ToLower().Split('\n');
            int surfaceIndex = 0;
            foreach (var line in lines)
            {
                if (surfaceIndex >= Math.Max(1, lcd.SurfaceCount))
                    break;

                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(HUDLCD_TAG))
                continue;

                Vector2D pos = _defaultPos;
                double scale = _defaultScale;

                string[] args = line.Trim().Split([_configSeparator], 6);
                for (int i = 1; i < Math.Min(6, args.Length); i++)
                {
                    if (string.IsNullOrWhiteSpace(args[i]))
                        continue;

                    switch (i)
                    {
                        case 1: // x position
                            if (!double.TryParse(args[i], out pos.X))
                                pos.X = _defaultPos.X;
                            break;
                        case 2: // y position
                            if (!double.TryParse(args[i], out pos.Y))
                                pos.Y = _defaultPos.Y;
                            break;
                        case 3:
                            if (!double.TryParse(args[i], out scale))
                                scale = _defaultScale;
                            break;
                        case 4:
                            // text color, not applicable for sprites
                            break;
                        case 5:
                            // text shadow, also not applicable for sprites
                            break;
                    }
                }

                // change [-1,1] top left, [1,-1] bottom right used by hudlcd to [0,0], [1,1] used by rendering
                pos.X = (pos.X + 1) * 0.5;
                pos.Y = (-pos.Y + 1) * 0.5;
                scale *= 0.68;

                Lcds.Add(new LcdConfig
                {
                    Block = lcd,
                    Surface = lcd.SurfaceCount is 0 ? lcd.PanelComponent : lcd.GetSurface(surfaceIndex),
                    TopLeft = pos,
                    Scale = scale,
                });

                surfaceIndex++;
            }
        }
    }

    private static void ClearLCDs()
    {
        Lcds.Clear();
    }

    public void Dispose()
    {
    }
}

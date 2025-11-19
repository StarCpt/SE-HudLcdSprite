using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.GUI.TextPanel;
using VRage.Plugins;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace HudSprite;

public struct SurfaceConfig
{
    public Vector2 TopLeft;
    public float Scale;
}

public class Plugin : IPlugin
{
    const string HUDLCD_TAG = "hudlcd";
    public const string OFFSCREEN_TEX_PREFIX = "HUDSPRITE_";
    static readonly char _configSeparator = ':';
    static readonly Vector2D _defaultPos = new Vector2D(-0.98, -0.2);
    static readonly double _defaultScale = 0.8;

    public void Init(object gameInstance)
    {
        new Harmony(GetType().FullName).PatchAll(Assembly.GetExecutingAssembly());
    }

    public void Update()
    {
        if (MySession.Static is null || !MySession.Static.Ready)
            return;

        if (MySession.Static?.ControlledEntity is MyCockpit cockpit && !cockpit.Closed)
        {
            bool update10 = MySession.Static.GameplayFrameCounter % 10 == 0;
            if (update10)
            {
                UpdateLCDs(cockpit.CubeGrid);
            }
        }
        else
        {
            ClearLCDs();
        }
    }

    public static readonly ConcurrentDictionary<MyTextPanelComponent, HudSpriteData> Surfaces = [];
    public static readonly HashSet<string> CreatedTextures = [];

    private static void UpdateLCDs(MyCubeGrid grid)
    {
        var surfacesToRemove = Surfaces.Keys.ToList();

        // read custom data
        foreach (var block in grid.GetFatBlocks<MyFunctionalBlock>())
        {
            if (block.CustomData == null)
            {
                continue;
            }

            string[] lines = block.CustomData.ToLower().Split('\n');
            int surfaceIndex = 0;
            foreach (var line in lines)
            {
                if (surfaceIndex >= Math.Max(1, block.SurfaceCount))
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

                if ((block as IMyTextSurfaceProvider)?.GetSurface(surfaceIndex) is not MyTextPanelComponent surface)
                {
                    continue;
                }

                if (!Surfaces.TryGetValue(surface, out var data))
                {
                    data = new HudSpriteData(surface);
                    Surfaces.TryAdd(surface, data);
                }
                else
                {
                    surfacesToRemove.RemoveFast(surface);
                }

                data.Update((Vector2)pos, (float)scale);

                surfaceIndex++;
            }
        }

        foreach (var surface in surfacesToRemove)
        {
            if (Surfaces.TryRemove(surface, out var data))
            {
                data.Dispose();
            }
        }
    }

    private static void ClearLCDs()
    {
        Surfaces.Values.ForEach(i => i.Dispose());
        Surfaces.Clear();
    }

    public class HudSpriteData : IDisposable
    {
        public MyTextPanelComponent Comp { get; }
        public Vector2 TopLeft { get; private set; }
        public float Scale { get; private set; }

        private bool _textureCreated = false;

        public HudSpriteData(MyTextPanelComponent comp)
        {
            Comp = comp;
        }

        public void Update(Vector2 topLeft, float scale)
        {
            TopLeft = topLeft;
            Scale = scale;

            if (Comp.m_textureGenerated && Comp.ContentType is ContentType.SCRIPT && Comp.m_block.IsWorking)
            {
                CreateTexture();
            }
            else
            {
                DestroyTexture();
            }
        }

        private void CreateTexture()
        {
            if (!_textureCreated)
            {
                _textureCreated = true;
                string name = OFFSCREEN_TEX_PREFIX + Comp.GetRenderTextureName();
                MyRenderProxy.CreateGeneratedTexture(name, Comp.m_textureSize.X, Comp.m_textureSize.Y, MyGeneratedTextureType.RGBA, 1, null, generateMipmaps: true, immediatelyReady: false);
                Comp.m_lastRenderLayers.Clear();
                CreatedTextures.Add(name);
            }
        }

        private void DestroyTexture()
        {
            if (_textureCreated)
            {
                _textureCreated = false;
                string name = OFFSCREEN_TEX_PREFIX + Comp.GetRenderTextureName();
                MyRenderProxy.DestroyGeneratedTexture(name);
                CreatedTextures.Remove(name);
            }
        }

        public void Dispose()
        {
            DestroyTexture();
        }
    }

    public void Dispose()
    {
    }
}

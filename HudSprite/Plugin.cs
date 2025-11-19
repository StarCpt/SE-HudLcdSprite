using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.Definitions;
using VRage.Game.GUI.TextPanel;
using VRage.Plugins;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Utils;
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

    // default settings
    static readonly Vector2D _defaultPos = new Vector2D(-0.98, -0.2);
    static readonly double _defaultScale = 0.8;
    static readonly bool _defaultTextShadow = false;

    static readonly Dictionary<string, Color> _colorByName =
        typeof(Color).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.CanRead && p.PropertyType == typeof(Color))
            .ToDictionary(p => p.Name.ToLower(), p => (Color)p.GetValue(null));

    public void Init(object gameInstance)
    {
        new Harmony(GetType().FullName).PatchAll(Assembly.GetExecutingAssembly());
    }

    private int _lastLcdGather = -1;

    public void Update()
    {
        if (MySession.Static is null || !MySession.Static.Ready)
        {
            _lastLcdGather = -1;
            return;
        }

        if (MySession.Static?.ControlledEntity is MyCockpit cockpit)
        {
            if (MySession.Static.GameplayFrameCounter - 100 > _lastLcdGather)
            {
                UpdateLCDs(cockpit.CubeGrid);
                _lastLcdGather = MySession.Static.GameplayFrameCounter;
            }
        }
        else
        {
            ClearLCDs();
            _lastLcdGather = -1;
        }

        DrawLCDs();
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

                if ((block as IMyTextSurfaceProvider)?.GetSurface(surfaceIndex) is not MyTextPanelComponent surface)
                    continue;

                Vector2D pos = _defaultPos;
                double scale = surface.FontSize;
                Color textColor = surface.FontColor;
                bool textShadow = _defaultTextShadow;

                string[] args = line.Trim().Split([_configSeparator], 6);
                for (int i = 1; i < Math.Min(6, args.Length); i++)
                {
                    if (string.IsNullOrWhiteSpace(args[i]))
                        continue;

                    switch (i)
                    {
                        case 1: // x position
                            pos.X = TryParseOrDefault(args[i], pos.X);
                            break;
                        case 2: // y position
                            pos.Y = TryParseOrDefault(args[i], pos.Y);
                            break;
                        case 3:
                            scale = TryParseOrDefault(args[i], scale);
                            break;
                        case 4:
                            textColor = _colorByName.GetValueOrDefault(args[i].Trim().ToLower(), surface.FontColor);
                            break;
                        case 5:
                            textShadow = args[i].Trim() is "1";
                            break;
                    }
                }

                // change [-1,1] top left, [1,-1] bottom right used by hudlcd to [0,0], [1,1] used by rendering
                pos.X = (pos.X + 1) * 0.5;
                pos.Y = (-pos.Y + 1) * 0.5;
                scale *= 0.682; // match with original hudlcd

                if (!Surfaces.TryGetValue(surface, out var data))
                {
                    data = new HudSpriteData(surface);
                    Surfaces.TryAdd(surface, data);
                }
                else
                {
                    surfacesToRemove.RemoveFast(surface);
                }

                data.UpdateSettings((Vector2)pos, (float)scale, textColor, textShadow);

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

    private static double TryParseOrDefault(string str, double defaultValue)
    {
        return double.TryParse(str, out var val) ? val : defaultValue;
    }

    private static void ClearLCDs()
    {
        Surfaces.Values.ForEach(i => i.Dispose());
        Surfaces.Clear();
    }

    static readonly MyStringHash _fontMonospace = MyStringHash.GetOrCompute("Monospace");

    private static void DrawLCDs()
    {
        foreach (var item in Surfaces.Values)
        {
            if (item.Comp.ContentType is ContentType.TEXT_AND_IMAGE)
            {
                string[] lines = item.Comp.Text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    // match original hudlcd position/line spacing
                    Vector2 offset;
                    float scale = item.Scale;
                    if (item.Comp.Font.SubtypeId == _fontMonospace)
                    {
                        offset = new Vector2(0, -0.0045f + (0.02825f * i)) * item.Scale;
                        scale *= 1.018f;
                    }
                    else
                    {
                        offset = new Vector2(0.0001f, -0.005f + (0.0229f * i)) * item.Scale;
                    }
                    MyGuiManager.DrawString(item.Comp.Font.SubtypeId, lines[i], item.TopLeft + offset, scale, item.TextColor, useFullClientArea: true);
                }
            }
        }
    }

    public class HudSpriteData : IDisposable
    {
        public MyTextPanelComponent Comp { get; }
        public Vector2 TopLeft { get; private set; }
        public float Scale { get; private set; }
        public Color TextColor { get; private set; }
        public bool TextShadow { get; private set; }

        public string? OffscreenTextureName { get; private set; }

        private bool _textureCreated = false;

        public HudSpriteData(MyTextPanelComponent comp)
        {
            Comp = comp;
        }

        public void UpdateSettings(Vector2 topLeft, float scale, Color textColor, bool textShadow)
        {
            TopLeft = topLeft;
            Scale = scale;
            TextColor = textColor;
            TextShadow = textShadow;

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
                OffscreenTextureName = name;
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
                OffscreenTextureName = null;
            }
        }

        public IUserGeneratedTexture? TryGetRenderTexture()
        {
            if (OffscreenTextureName is not null && MyManagers.FileTextures.TryGetTexture(OffscreenTextureName, out IUserGeneratedTexture texture))
            {
                return texture;
            }
            return null;
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

using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.Components;
using Sandbox.Game.Entities.Blocks;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Collections;
using VRage.Game.Definitions;
using VRage.Game.GUI.TextPanel;
using VRage.Render11.Resources;
using VRage.Render11.Sprites;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace HudSprite.Patches;

[HarmonyPatch]
public class Patches
{
    [HarmonyPatch(typeof(MyTextPanelComponent), nameof(MyTextPanelComponent.UpdateAfterSimulation))]
    [HarmonyPrefix]
    public static void UpdateAfterSimulation_Prefix(MyTextPanelComponent __instance, ref bool isWorking, ref bool isInRange)
    {
        // keeps lcd screens active when the camera moves out of range if it's used by the plugin
        isInRange |= isWorking && __instance.ContentType is ContentType.SCRIPT && Plugin.Surfaces.ContainsKey(__instance);
    }

    [HarmonyPatch(typeof(MyRenderComponentScreenAreas), nameof(MyRenderComponentScreenAreas.RenderSpritesToTexture))]
    [HarmonyPostfix]
    static void RenderSpritesToTexture_Postfix(MyRenderComponentScreenAreas __instance, int area, ListReader<MySprite> sprites, Vector2I textureSize, Vector2 aspectRatio, Color backgroundColor, byte backgroundAlpha)
    {
        string text = Plugin.OFFSCREEN_TEX_PREFIX + __instance.GenerateOffscreenTextureName(__instance.m_entity.EntityId, area);
        if (!Plugin.CreatedTextures.Contains(text))
        {
            return;
        }

        Vector2 vector = MyRenderComponentScreenAreas.CalcAspectFactor(textureSize, aspectRatio);
        Vector2 vector2 = MyRenderComponentScreenAreas.CalcShift(textureSize, vector);
        bool flag = false;
        for (int i = 0; i < sprites.Count; i++)
        {
            MySprite mySprite = sprites[i];
            Vector2 vector3 = mySprite.Size ?? ((Vector2)textureSize);
            Vector2 vector4 = mySprite.Position ?? ((Vector2)(textureSize / 2));
            Color color = mySprite.Color ?? Color.White;
            vector4 += vector2;
            switch (mySprite.Type)
            {
                case SpriteType.TEXTURE:
                    {
                        MyLCDTextureDefinition definition2 = MyDefinitionManager.Static.GetDefinition<MyLCDTextureDefinition>(MyStringHash.GetOrCompute(mySprite.Data));
                        if ((definition2?.SpritePath ?? definition2?.TexturePath) != null)
                        {
                            switch (mySprite.Alignment)
                            {
                                case TextAlignment.LEFT:
                                    vector4 += new Vector2(vector3.X * 0.5f, 0f);
                                    break;
                                case TextAlignment.RIGHT:
                                    vector4 -= new Vector2(vector3.X * 0.5f, 0f);
                                    break;
                            }

                            Vector2 rightVector = new Vector2(1f, 0f);
                            if (Math.Abs(mySprite.RotationOrScale) > 1E-05f)
                            {
                                rightVector = new Vector2((float)Math.Cos(mySprite.RotationOrScale), (float)Math.Sin(mySprite.RotationOrScale));
                            }

                            MyRenderProxy.DrawSpriteAtlas(definition2.SpritePath ?? definition2.TexturePath, vector4, Vector2.Zero, Vector2.One, rightVector, Vector2.One, color, vector3 / 2f, ignoreBounds: false, text);
                        }

                        break;
                    }
                case SpriteType.TEXT:
                    {
                        switch (mySprite.Alignment)
                        {
                            case TextAlignment.RIGHT:
                                vector4 -= new Vector2(vector3.X, 0f);
                                break;
                            case TextAlignment.CENTER:
                                vector4 -= new Vector2(vector3.X * 0.5f, 0f);
                                break;
                        }

                        MyFontDefinition definition = MyDefinitionManager.Static.GetDefinition<MyFontDefinition>(MyStringHash.GetOrCompute(mySprite.FontId));
                        int textureWidthinPx = (int)Math.Round(vector3.X);
                        MyRenderProxy.DrawStringAligned((int)(definition?.Id.SubtypeId ?? MyStringHash.GetOrCompute("Debug")), vector4, color, mySprite.Data ?? string.Empty, mySprite.RotationOrScale, float.PositiveInfinity, ignoreBounds: false, text, textureWidthinPx, (MyRenderTextAlignmentEnum)mySprite.Alignment);
                        break;
                    }
                case SpriteType.CLIP_RECT:
                    if (mySprite.Position.HasValue && mySprite.Size.HasValue)
                    {
                        if (flag)
                        {
                            MyRenderProxy.SpriteScissorPop(text);
                        }
                        else
                        {
                            flag = true;
                        }

                        MyRenderProxy.SpriteScissorPush(new Rectangle((int)vector4.X, (int)vector4.Y, (int)vector3.X, (int)vector3.Y), text);
                    }
                    else if (flag)
                    {
                        MyRenderProxy.SpriteScissorPop(text);
                        flag = false;
                    }

                    break;
            }
        }

        if (flag)
        {
            MyRenderProxy.SpriteScissorPop(text);
        }

        backgroundColor.A = backgroundAlpha;
        uint[] renderObjectIDs = __instance.m_screenAreas[area].RenderObjectIDs;
        int j;
        for (j = 0; j < renderObjectIDs.Length && renderObjectIDs[j] == uint.MaxValue; j++)
        {
        }

        if (j < renderObjectIDs.Length)
        {
            MyRenderProxy.RenderOffscreenTexture(text, vector, backgroundColor);
        }
    }

    [HarmonyPatch(typeof(MySpritesManager), nameof(MySpritesManager.Render))]
    [HarmonyPrefix]
    static void MySpritesManager_Render_Prefix(IRtvBindable rtv, ref IBlendState blendState)
    {
        // makes the sprite renderer write alpha for proper transparency
        if (rtv.Name.StartsWith(Plugin.OFFSCREEN_TEX_PREFIX))
        {
            blendState = MyBlendStateManager.BlendAlphaPremult;
        }
    }
}

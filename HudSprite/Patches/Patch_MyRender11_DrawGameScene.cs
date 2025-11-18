using HarmonyLib;
using Sandbox.Game.Entities.Blocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.GUI.TextPanel;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRageRender;

namespace HudSprite.Patches;

[HarmonyPatch(typeof(MyRender11), nameof(MyRender11.DrawGameScene))]
public class Patch_MyRender11_DrawGameScene
{
    [HarmonyPostfix]
    public static void DrawGameScene_Postfix(ref IRtvBindable renderTarget)
    {
        if (renderTarget is not MyBackbuffer)
        {
            return;
        }

        foreach (var lcd in Plugin.Lcds)
        {
            if (lcd.Block.Closed || !lcd.Block.InScene)
                continue;

            if (lcd.Surface.ContentType is ContentType.TEXT_AND_IMAGE)
            {
                // TODO
            }
            else if (lcd.Surface.ContentType is ContentType.SCRIPT)
            {
                // get srv/rtv
                var comp = (MyTextPanelComponent)lcd.Surface;
                if (!TryGetRenderTexture(comp, out var tex))
                {
                    return;
                }

                var vp = new MyViewport(
                    (float)lcd.TopLeft.X * renderTarget.Size.X,
                    (float)lcd.TopLeft.Y * renderTarget.Size.Y,
                    tex.Size.X * (float)lcd.Scale,
                    tex.Size.Y * (float)lcd.Scale);
                CopyReplaceNoAlpha(renderTarget, tex, vp, true);
            }
        }
    }

    private static void CopyReplaceNoAlpha(IRtvBindable destination, ISrvBindable source, MyViewport viewport, bool shouldStretch = false)
    {
        MyImmediateRC.RC.SetBlendState(MyBlendStateManager.BlendReplaceNoAlphaChannel);

        MyImmediateRC.RC.SetInputLayout(null);
        if (source.Size != destination.Size || shouldStretch)
        {
            if (shouldStretch)
            {
                MyImmediateRC.RC.PixelShader.Set(MyCopyToRT.m_stretchPs);
            }
            else
            {
                MyImmediateRC.RC.PixelShader.Set(MyCopyToRT.m_copyFilterPs);
                MyImmediateRC.RC.PixelShader.SetSampler(2, MySamplerStateManager.Linear);
            }
        }
        else
        {
            MyImmediateRC.RC.PixelShader.Set(MyCopyToRT.m_copyPs);
        }

        MyImmediateRC.RC.SetRtv(destination);
        MyImmediateRC.RC.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        MyImmediateRC.RC.PixelShader.SetSrv(0, source);
        MyScreenPass.DrawFullscreenQuad(MyImmediateRC.RC, viewport);
    }

    private static bool TryGetRenderTexture(MyTextPanelComponent comp, out IUserGeneratedTexture texture)
    {
        string name;
        try
        {
            name = comp.GetRenderTextureName();
        }
        catch (NullReferenceException)
        {
            texture = null;
            return false;
        }

        return MyManagers.FileTextures.TryGetTexture(name, out texture) && texture != null;
    }
}

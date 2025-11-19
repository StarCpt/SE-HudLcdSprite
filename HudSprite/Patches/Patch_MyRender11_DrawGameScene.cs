using HarmonyLib;
using Sandbox.Game.Entities.Blocks;
using System;
using System.Linq;
using VRage.Game.GUI.TextPanel;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Render11.Resources.Internal;
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

        foreach (var (comp, data) in Plugin.Surfaces)
        {
            if (comp.ContentType is ContentType.TEXT_AND_IMAGE)
            {
            }
            else if (comp.ContentType is ContentType.SCRIPT)
            {
                IBlendState blendState;
                IUserGeneratedTexture? sourceTex;

                // camera lcd (remastered) compatibility
                if (data.Comp.Script == "TSS_CameraDisplay_2")
                {
                    blendState = MyBlendStateManager.BlendReplaceNoAlphaChannel;
                    sourceTex = TryGetCameraLcdRtv(data.Comp);
                }
                else
                {
                    blendState = MyBlendStateManager.BlendAlphaPremult;
                    sourceTex = data.TryGetRenderTexture();
                }

                if (sourceTex is null)
                {
                    continue;
                }

                var vp = new MyViewport(
                    data.TopLeft.X * renderTarget.Size.X,
                    data.TopLeft.Y * renderTarget.Size.Y,
                    comp.SurfaceSize.X * data.Scale,
                    comp.SurfaceSize.Y * data.Scale);
                CopyWithBlendState(blendState, renderTarget, sourceTex, vp, true);
            }
        }
    }

    private static IUserGeneratedTexture? TryGetCameraLcdRtv(MyTextPanelComponent comp)
    {
        string? rtvName = null;
        try
        {
            if (comp.m_block != null && comp.Render != null)
            {
                rtvName = comp.GetRenderTextureName();
            }
        }
        catch (NullReferenceException)
        {
            return null;
        }

        if (rtvName is not null && MyManagers.FileTextures.TryGetTexture(rtvName, out IUserGeneratedTexture texture) && texture.IsLoaded)
        {
            return texture;
        }
        return null;
    }

    private static void CopyWithBlendState(IBlendState blend, IRtvBindable destination, ISrvBindable source, MyViewport viewport, bool shouldStretch = false)
    {
        MyImmediateRC.RC.SetBlendState(blend);

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
}

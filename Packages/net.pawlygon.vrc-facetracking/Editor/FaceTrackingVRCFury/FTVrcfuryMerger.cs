using com.vrcfury.api;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Merges the generated assets with a VRCFury Full Controller, the way VRCFury asks other tools to
    /// (https://vrcfury.com/api/). Preferred over Modular Avatar when both are installed.
    /// </summary>
    internal sealed class FTVrcfuryMerger : FTMerger
    {
        public override string Name => "VRCFury";
        public override int Priority => 0;

        public override void Install(GameObject avatarRoot, AnimatorController fx, VRCExpressionParameters parameters, VRCExpressionsMenu menu)
        {
            // On the avatar root, so animation paths stay relative to it
            var controller = FuryComponents.CreateFullController(avatarRoot);
            if (fx != null) controller.AddController(fx, VRCAvatarDescriptor.AnimLayerType.FX);
            if (parameters != null) controller.AddParams(parameters);
            if (menu != null) controller.AddMenu(menu);
            // Forwarded parameters and the guard parameters are shared with native face tracking and other animators
            controller.AddGlobalParam("*");
        }
    }
}

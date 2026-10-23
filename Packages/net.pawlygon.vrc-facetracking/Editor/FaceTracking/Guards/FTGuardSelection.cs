using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    internal enum FTGuardKind
    {
        Gesture,
        Blink,
    }

    /// <summary>
    /// The FX layers a <see cref="PawlygonFTGuards"/> component wants guarded, in every controller that ends up in FX,
    /// with the parameter that pauses each one. Whatever guards them (Bake Guards today) works from this list, so the
    /// detection and the layer choices in the inspector live in one place.
    /// </summary>
    internal sealed class FTGuardSelection
    {
        internal sealed class Layer
        {
            public FTGuardKind Kind;
            // Where the controller comes from: the avatar's FX, or the object of the component that merges it
            public string Source;
            public AnimatorController Controller;
            public int Index;
            public string Name;
            public bool AlreadyGuarded;

            /// <summary>The name without the prefix VRCFury adds when merging, as the layer lists store it.</summary>
            public string CleanName => FXGuardAnalysis.CleanLayerName(Name);

            /// <summary>The parameter that pauses the layer while it's on.</summary>
            public string Parameter => Kind == FTGuardKind.Gesture ? FXGuardWriter.GestureGuardParameter : FXGuardWriter.BlinkGuardParameter;
        }

        public readonly List<Layer> Layers = new List<Layer>();

        /// <summary>Selected layers that don't have a guard yet.</summary>
        public IEnumerable<Layer> Unguarded => Layers.Where(l => !l.AlreadyGuarded);

        /// <summary>The selection in every controller that ends up in FX: the avatar's own and the ones VRCFury / Modular Avatar merge.</summary>
        public static FTGuardSelection Resolve(PawlygonFTGuards component, VRCAvatarDescriptor avatar)
        {
            var faceMeshes = FaceMeshes(avatar);
            return From(component, FTAvatarAssets.GetSourceFXControllers(avatar)
                .Select(s => (s.source, s.controller, FXGuardAnalysis.Analyze(s.controller, faceMeshes))));
        }

        /// <summary>The selection in controllers that were already analyzed.</summary>
        public static FTGuardSelection From(PawlygonFTGuards component,
            IEnumerable<(string source, AnimatorController controller, FXGuardAnalysis analysis)> analyses)
        {
            var selection = new FTGuardSelection();
            foreach (var (source, controller, analysis) in analyses)
            {
                if (FaceTrackingGuardsBuilder.GuardsGestures(component))
                {
                    selection.Layers.AddRange(analysis.GestureLayers.Where(l => ShouldGuard(component, l)).Select(l => new Layer
                    {
                        Kind = FTGuardKind.Gesture, Source = source, Controller = controller, Index = l.Index, Name = l.Name, AlreadyGuarded = l.AlreadyGuarded,
                    }));
                }
                if (component.guardBlinking)
                {
                    selection.Layers.AddRange(analysis.BlinkLayers.Where(l => ShouldGuard(component, l)).Select(l => new Layer
                    {
                        Kind = FTGuardKind.Blink, Source = source, Controller = controller, Index = l.Index, Name = l.Name, AlreadyGuarded = l.AlreadyGuarded,
                    }));
                }
            }
            return selection;
        }

        /// <summary>The meshes whose blendshapes count as the face: the setup component's, or the ones already on the descriptor.</summary>
        public static FXFaceMeshes FaceMeshes(VRCAvatarDescriptor avatar)
        {
            var setup = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault();
            var meshes = setup != null
                ? FaceTrackingSetupPlan.Resolve(setup, avatar).Meshes
                : avatar.faceTrackingMeshes ?? new List<SkinnedMeshRenderer>();
            return new FXFaceMeshes(meshes, avatar.transform);
        }

        /// <summary>
        /// Layers whose gestures animate the face are selected unless excluded, and layers that only check gestures when
        /// included. Already guarded layers count as expression layers.
        /// </summary>
        public static bool ShouldGuard(PawlygonFTGuards component, FXGuardAnalysis.GestureLayer layer)
        {
            var name = FXGuardAnalysis.CleanLayerName(layer.Name);
            if (IsAutomatic(layer)) return !component.excludedGestureLayers.Contains(name);
            return component.includedGestureLayers.Contains(name);
        }

        public static bool IsAutomatic(FXGuardAnalysis.GestureLayer layer) => layer.DrivesExpressions || layer.AlreadyGuarded;

        public static bool ShouldGuard(PawlygonFTGuards component, FXGuardAnalysis.BlinkLayer layer)
        {
            var name = FXGuardAnalysis.CleanLayerName(layer.Name);
            if (component.excludedBlinkLayers.Contains(name)) return false;
            return layer.Confidence == FXBlinkConfidence.High || layer.AlreadyGuarded || component.includedBlinkLayers.Contains(name);
        }
    }
}

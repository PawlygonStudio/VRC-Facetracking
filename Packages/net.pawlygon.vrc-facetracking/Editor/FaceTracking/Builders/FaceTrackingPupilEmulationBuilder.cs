using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Takes the pupil blendshapes over from native face tracking and drives them from an FX layer:
    /// Off → Idle → Eyes Closed (blink) → Blink Reflex → Idle while eye tracking is active, and Tracked, which
    /// plays the real pupil tracking, as soon as the headset reports it. The layer's weight is 0 while eye
    /// tracking is off, so the blendshapes are left as they are.
    /// </summary>
    internal sealed class FaceTrackingPupilEmulationBuilder : FTBuilder<PawlygonFTPupilEmulation>
    {
        const string LayerName = "Pawlygon FT - Pupil Emulation";
        const string LidParameter = FTPupilEmulationPlan.ParameterPrefix + "Lid";
        const string OneParameter = FTPupilEmulationPlan.ParameterPrefix + "One";

        // The more open eyelid, on the 1.x EyeLid scale (0 closed, 0.75 normally open): both eyelids are
        // below a threshold exactly when this is
        const string LidExpression = "max(1 - EyeClosedLeft, 1 - EyeClosedRight) * 0.75";

        // A pupil value above this means the headset tracks pupils
        const float RealTrackingMargin = 0.02f;

        // After Face Tracking has set up the meshes and blendshape sets the pupil keys are taken from
        public override FTPhase Phase => FTPhase.Animator;

        protected override void Build(PawlygonFTPupilEmulation component, FTBuildContext context)
        {
            if (!context.EnableFaceTracking)
            {
                throw new FTBuildException("Face tracking isn't enabled on this avatar. Add a Pawlygon Face Tracking component, " +
                                           "or enable face tracking on the avatar descriptor.");
            }

            var plan = FTPupilEmulationPlan.Resolve(context.Meshes, context.Sets, context.UseAllTrackingKeys);
            if (plan.Shapes.Count == 0)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] No face tracking mesh has EyeDilation or EyeConstrict blendshapes, " +
                                 "so Pupil Emulation has nothing to drive.", component);
                return;
            }

            FTPupilEmulationPlan.UnmapPupilKeys(context.Sets);
            AddForward(context, LidParameter, LidExpression);
            if (plan.RealTracking)
            {
                foreach (var shape in plan.Shapes) AddForward(context, shape.Parameter, shape.Expression);
            }

            var descriptor = context.Descriptor;
            var fx = FTAvatarAssets.CopyFX(FTAvatarAssets.GetFX(descriptor), context.BuildFolder, out var assign);
            new LayerBuilder(fx, component, plan, context).Build();
            FTGuardDriver.AddBlinkDriver(fx);
            FTAvatarAssets.SetFX(descriptor, assign);

            // Set by the owner's driver and synced, like Face Tracking Guards does
            var parameters = FTAvatarAssets.CopyParameters(descriptor, context.BuildFolder);
            FTAvatarAssets.AddParameter(parameters, FXGuardWriter.BlinkGuardParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);
            AssetDatabase.SaveAssets();
        }

        static void AddForward(FTBuildContext context, string parameter, string expression)
        {
            context.Behaviors.RemoveAll(b => b.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator
                                             && b.outputParameterName == parameter);
            context.Behaviors.Add(FaceTrackingParametersBuilder.CreateForward(parameter, expression));
        }

        sealed class LayerBuilder
        {
            readonly AnimatorController fx;
            readonly PawlygonFTPupilEmulation settings;
            readonly FTPupilEmulationPlan plan;
            readonly Dictionary<string, List<string>> pathsByShape;

            public LayerBuilder(AnimatorController fx, PawlygonFTPupilEmulation settings, FTPupilEmulationPlan plan, FTBuildContext context)
            {
                this.fx = fx;
                this.settings = settings;
                this.plan = plan;

                // Native face tracking applies a mapping to every face tracking mesh with that blendshape
                var meshes = context.Meshes.Where(m => m != null && m.sharedMesh != null).Distinct().ToList();
                pathsByShape = plan.Shapes.ToDictionary(s => s.Name, s => meshes
                    .Where(m => m.sharedMesh.GetBlendShapeIndex(s.Name) >= 0)
                    .Select(m => AnimationUtility.CalculateTransformPath(m.transform, context.AvatarRoot.transform))
                    .ToList());
            }

            public void Build()
            {
                FXGuardWriter.OnOffConditions(fx, FXGuardWriter.BlinkGuardParameter, AnimatorControllerParameterType.Float,
                    out var tracking, out var notTracking);
                AddFloat(LidParameter, 0.75f);

                var machine = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
                Save(machine);
                var layerIndex = fx.layers.Length;
                fx.AddLayer(new AnimatorControllerLayer { name = fx.MakeUniqueLayerName(LayerName), defaultWeight = 0f, stateMachine = machine });

                var (reflexDilation, reflexConstrict) = FTPupilCurves.ReflexCurves(settings);
                var idleClip = Clip("Pupils - Idle", true, FTPupilCurves.IdleCurve(settings), Constant(0f, FTPupilCurves.IdleLength));
                var reflexClip = Clip("Pupils - Blink Reflex", false, reflexDilation, reflexConstrict);

                var off = State(machine, "Off", new Vector3(300f, 0f), Save(new AnimationClip { name = "Pupils - Off" }));
                var idle = State(machine, "Idle", new Vector3(300f, 120f), idleClip);
                var closed = State(machine, "Eyes Closed", new Vector3(560f, 120f), idleClip);
                var reflex = State(machine, "Blink Reflex", new Vector3(430f, 240f), reflexClip);
                machine.defaultState = off;

                LayerControl(off, layerIndex, 0f);
                LayerControl(idle, layerIndex, 1f);

                // Any State → Off, without re-entering Off every frame
                var toOff = machine.AddAnyStateTransition(off);
                Configure(toOff, 0f);
                toOff.canTransitionToSelf = false;
                toOff.conditions = new[] { notTracking };

                Transition(off, idle, 0f, tracking);
                foreach (var from in new[] { idle, reflex })
                {
                    Transition(from, closed, 0.05f, new AnimatorCondition { mode = AnimatorConditionMode.Less, parameter = LidParameter, threshold = settings.lidClosedBelow });
                }
                Transition(closed, reflex, 0f, new AnimatorCondition { mode = AnimatorConditionMode.Greater, parameter = LidParameter, threshold = settings.lidOpenAbove });

                var settle = reflex.AddTransition(idle);
                Configure(settle, 0.25f);
                settle.hasExitTime = true;
                settle.exitTime = 1f;

                if (!plan.RealTracking) return;

                // Stays in Tracked until eye tracking stops, even when the pupils come back to rest
                var tracked = State(machine, "Tracked", new Vector3(560f, 0f), TrackedMotion());
                LayerControl(tracked, layerIndex, 1f);
                foreach (var shape in plan.Shapes)
                {
                    AddFloat(shape.Parameter, 0f);
                    foreach (var from in new[] { idle, closed, reflex })
                    {
                        Transition(from, tracked, 0.25f, new AnimatorCondition { mode = AnimatorConditionMode.Greater, parameter = shape.Parameter, threshold = RealTrackingMargin });
                    }
                }
            }

            /// <summary>Each pupil blendshape follows its own tracking value, played together in a Direct tree.</summary>
            Motion TrackedMotion()
            {
                AddFloat(OneParameter, 1f);
                var root = Tree("Pupils - Tracked", BlendTreeType.Direct, null);
                root.children = plan.Shapes.Select(shape =>
                {
                    var tree = Tree($"Pupils - Tracked {shape.Name}", BlendTreeType.Simple1D, shape.Parameter);
                    tree.AddChild(ShapeClip($"Pupils - {shape.Name} 0", shape.Name, 0f), 0f);
                    tree.AddChild(ShapeClip($"Pupils - {shape.Name} 100", shape.Name, 100f), 1f);
                    return new ChildMotion { motion = tree, directBlendParameter = OneParameter, timeScale = 1f };
                }).ToArray();
                return root;
            }

            AnimationClip Clip(string name, bool loop, AnimationCurve dilation, AnimationCurve constrict)
            {
                var clip = new AnimationClip { name = name };
                foreach (var shape in plan.Shapes) SetCurve(clip, shape.Name, shape.IsDilation ? dilation : constrict);
                var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
                clipSettings.loopTime = loop;
                AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
                return Save(clip);
            }

            AnimationClip ShapeClip(string name, string shape, float value)
            {
                var clip = new AnimationClip { name = name };
                SetCurve(clip, shape, Constant(value, 1f / 60f));
                return Save(clip);
            }

            void SetCurve(AnimationClip clip, string shape, AnimationCurve curve)
            {
                foreach (var path in pathsByShape[shape])
                {
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape), curve);
                }
            }

            static AnimationCurve Constant(float value, float length) => AnimationCurve.Constant(0f, length, value);

            BlendTree Tree(string name, BlendTreeType type, string parameter)
            {
                var tree = new BlendTree { name = name, blendType = type, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
                if (parameter != null) tree.blendParameter = parameter;
                return Save(tree);
            }

            static AnimatorState State(AnimatorStateMachine machine, string name, Vector3 position, Motion motion)
            {
                var state = machine.AddState(name, position);
                state.motion = motion;
                // The layer only animates the pupil blendshapes, and lets go of them when eye tracking stops
                state.writeDefaultValues = true;
                return state;
            }

            static void LayerControl(AnimatorState state, int layerIndex, float weight)
            {
                var control = state.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
                control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
                control.layer = layerIndex;
                control.goalWeight = weight;
                control.blendDuration = 0.25f;
            }

            static void Transition(AnimatorState from, AnimatorState to, float duration, params AnimatorCondition[] conditions)
            {
                var transition = from.AddTransition(to);
                Configure(transition, duration);
                transition.conditions = conditions;
            }

            static void Configure(AnimatorStateTransition transition, float duration)
            {
                transition.hasExitTime = false;
                transition.hasFixedDuration = true;
                transition.duration = duration;
                transition.exitTime = 0f;
                transition.offset = 0f;
            }

            void AddFloat(string name, float defaultValue)
            {
                if (fx.parameters.Any(p => p.name == name)) return;
                fx.AddParameter(new AnimatorControllerParameter { name = name, type = AnimatorControllerParameterType.Float, defaultFloat = defaultValue });
            }

            T Save<T>(T asset) where T : Object
            {
                if (AssetDatabase.Contains(fx)) AssetDatabase.AddObjectToAsset(asset, fx);
                return asset;
            }
        }
    }
}

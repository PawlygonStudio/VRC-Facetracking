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
    /// Off → Idle → Eyes Closed (blink) → Blink Reflex → Idle while eye tracking is active. Each of those plays the
    /// emulated movement with the headset's pupil tracking added on top, and Face Tracking Toggles can put a menu
    /// toggle on either. The layer's weight is 0 while eye tracking is off, so the blendshapes are left as they are.
    /// </summary>
    internal sealed class FaceTrackingPupilEmulationBuilder : FTBuilder<PawlygonFTPupilEmulation>
    {
        const string LayerName = "Pawlygon FT - Pupil Emulation";
        const string LidParameter = FTPupilEmulationPlan.ParameterPrefix + "Lid";
        const string OneParameter = FTPupilEmulationPlan.ParameterPrefix + "One";
        const string WideParameter = FTPupilEmulationPlan.ParameterPrefix + "Wide";
        const string FrownParameter = FTPupilEmulationPlan.ParameterPrefix + "Frown";

        // Mood reactions: how wide both eyes are, and how much both mouth corners frown. Kept apart so both can
        // apply at once.
        const string WideExpression = "(EyeWideLeft + EyeWideRight) / 2";
        const string FrownExpression = "min(MouthFrownLeft, MouthFrownRight)";

        // The more open eyelid, on the 1.x EyeLid scale (0 closed, 0.75 normally open): both eyelids are
        // below a threshold exactly when this is
        const string LidExpression = "max(1 - EyeClosedLeft, 1 - EyeClosedRight) * 0.75";

        // After Face Tracking has set up the meshes and blendshape sets the pupil keys are taken from
        public override FTPhase Phase => FTPhase.Animator;

        /// <summary>
        /// Builds the layer before VRCFury and Modular Avatar run, so they merge it, and blendshape optimizers see
        /// that it animates the pupil blendshapes.
        /// </summary>
        protected override void BuildDescriptor(PawlygonFTPupilEmulation component, FTBuildContext context)
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

            var toggles = FTAvatarUtils.GetBuildComponents<PawlygonFTToggles>(context.AvatarRoot).FirstOrDefault();
            plan.ResolveToggles(toggles);

            FTPupilEmulationPlan.UnmapPupilKeys(context.Sets);
            AddForward(context, LidParameter, LidExpression);
            if (plan.RealTracking)
            {
                foreach (var channel in plan.Channels) AddForward(context, channel.Parameter, channel.Expression);
            }
            if (UsesMood(component, plan))
            {
                AddForward(context, WideParameter, WideExpression);
                AddForward(context, FrownParameter, FrownExpression);
            }

            var fx = context.Generated.FX;
            new LayerBuilder(fx, component, plan, context).Build();
            FTGuardDriver.AddBlinkDriver(fx);

            // Set by the owner's driver and synced, like Face Tracking Guards does
            FTAvatarAssets.AddParameter(context.Generated.Parameters, FXGuardWriter.BlinkGuardParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);

            if (plan.TrackingToggle) AddToggle(toggles, context, "Pupil Tracking", FTPupilEmulationPlan.TrackingParameter, "Eye_Dilatation_Tracking_Icon.png");
            if (plan.EmulationToggle) AddToggle(toggles, context, "Pupil Emulation", FTPupilEmulationPlan.EmulationParameter, "Eye_Tracking_Icon.png");

            WarnAboutFakePupils(component, context);
        }

        /// <summary>
        /// A menu toggle that starts on. Everyone's layer reads it, so it's synced, and the choice is saved.
        /// </summary>
        static void AddToggle(PawlygonFTToggles toggles, FTBuildContext context, string name, string parameter, string icon)
        {
            FTAvatarAssets.AddParameter(context.Generated.Parameters, parameter, VRCExpressionParameters.ValueType.Bool, 1, true, true);

            var menu = FaceTrackingTogglesBuilder.ControlsMenu(toggles, context, parameter);
            if (menu == null) return;
            menu.controls.Add(FTAvatarAssets.CreateControl(name, VRCExpressionsMenu.Control.ControlType.Toggle, parameter, FaceTrackingTogglesBuilder.Icon(icon)));
            EditorUtility.SetDirty(menu);
        }

        /// <summary>
        /// Fake pupil layers avatars bring from 1.x (Face Tracking Extras' or hand-made) animate the same blendshapes, so
        /// both would play at once. Builds don't change the avatar's animators, so they're only reported: Remove Fake
        /// Pupils in the inspector takes them out of their controllers.
        /// </summary>
        static void WarnAboutFakePupils(PawlygonFTPupilEmulation component, FTBuildContext context)
        {
            foreach (var (source, controller, _) in FTAvatarAssets.GetSourceFXControllers(context.Descriptor))
            {
                var names = FTFakePupilLayers.Find(controller).Select(i => $"'{FXGuardAnalysis.CleanLayerName(controller.layers[i].name)}'").ToList();
                if (names.Count == 0) continue;

                Debug.LogWarning($"[{FTErrorBoundary.Title}] The fake pupil {(names.Count == 1 ? "layer" : "layers")} {string.Join(", ", names)} in {source} " +
                                 $"{(names.Count == 1 ? "animates" : "animate")} the same blendshapes as Pupil Emulation, so both will play at once. " +
                                 "Use Remove Fake Pupils in the Pupil Emulation inspector.", component);
            }
        }

        /// <summary>Mood reactions read keys that need All Tracking Keys, like real pupil tracking.</summary>
        static bool UsesMood(PawlygonFTPupilEmulation component, FTPupilEmulationPlan plan) => component.moodReactions && plan.RealTracking;

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
                Motion idleClip = Clip("Pupils - Idle", true, FTPupilCurves.IdleCurve(settings), Constant(0f, FTPupilCurves.IdleLength));
                if (UsesMood(settings, plan)) idleClip = WithMood(idleClip);
                var reflexClip = Clip("Pupils - Blink Reflex", false, reflexDilation, reflexConstrict);

                var idleMotion = Combined("Idle", idleClip, FTPupilCurves.IdleLength);
                var reflexMotion = Combined("Blink Reflex", reflexClip, FTPupilCurves.ReflexDuration(settings));

                var off = State(machine, "Off", new Vector3(300f, 0f), Save(new AnimationClip { name = "Pupils - Off" }));
                var idle = State(machine, "Idle", new Vector3(300f, 120f), idleMotion);
                var closed = State(machine, "Eyes Closed", new Vector3(560f, 120f), idleMotion);
                var reflex = State(machine, "Blink Reflex", new Vector3(430f, 240f), reflexMotion);
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
            }

            /// <summary>
            /// The emulated motion with the headset's pupil tracking added on top, in a Direct tree. Each part plays at
            /// its menu toggle's value, or always when it has no toggle. A headset that doesn't track pupils reports 0,
            /// which adds nothing. Every clip in the tree is as long as the emulated one, so the tree keeps its length
            /// whatever the weights are.
            /// </summary>
            Motion Combined(string name, Motion emulated, float length)
            {
                if (!plan.RealTracking && !plan.EmulationToggle) return emulated;

                AddFloat(OneParameter, 1f);
                var children = new List<ChildMotion> { Child(emulated, Weight(plan.EmulationToggle, FTPupilEmulationPlan.EmulationParameter)) };
                if (plan.RealTracking)
                {
                    children.Add(Child(TrackedMotion(name, length), Weight(plan.TrackingToggle, FTPupilEmulationPlan.TrackingParameter)));
                }

                var root = Tree($"Pupils - {name} Combined", BlendTreeType.Direct, null);
                root.children = children.ToArray();
                return root;
            }

            /// <summary>The menu toggle's parameter, read as a float (0 or 1), or the constant 1 without a toggle.</summary>
            string Weight(bool hasToggle, string parameter)
            {
                if (!hasToggle) return OneParameter;
                AddFloat(parameter, 1f);
                return parameter;
            }

            static ChildMotion Child(Motion motion, string weightParameter)
            {
                return new ChildMotion { motion = motion, directBlendParameter = weightParameter, timeScale = 1f };
            }

            /// <summary>
            /// The idle drift with the mood reactions added on top in a Direct tree: wide eyes add constriction, a
            /// frown on both sides adds dilation.
            /// </summary>
            Motion WithMood(Motion idle)
            {
                AddFloat(OneParameter, 1f);
                AddFloat(WideParameter, 0f);
                AddFloat(FrownParameter, 0f);

                var children = new List<ChildMotion> { Child(idle, OneParameter) };
                if (plan.ConstrictShapes.Any() && settings.wideEyesConstrict > 0f)
                {
                    children.Add(MoodChild("Wide Eyes", WideParameter, plan.ConstrictShapes, 0f, settings.wideEyesConstrict));
                }
                if (plan.DilationShapes.Any() && settings.frownDilation > 0f)
                {
                    children.Add(MoodChild("Frown", FrownParameter, plan.DilationShapes, settings.frownDilation, 0f));
                }

                var root = Tree("Pupils - Idle With Mood", BlendTreeType.Direct, null);
                root.children = children.ToArray();
                return root;
            }

            ChildMotion MoodChild(string name, string parameter, IEnumerable<FTPupilEmulationPlan.Shape> shapes, float dilation, float constriction)
            {
                var list = shapes.ToList();
                var tree = Tree($"Pupils - Mood {name}", BlendTreeType.Simple1D, parameter);
                tree.AddChild(PupilClip($"Pupils - Mood {name} 0", list, 0f, 0f, FTPupilCurves.IdleLength), 0f);
                tree.AddChild(PupilClip($"Pupils - Mood {name}", list, dilation, constriction, FTPupilCurves.IdleLength), 1f);
                return Child(tree, OneParameter);
            }

            /// <summary>
            /// The given shapes held at one value for <paramref name="length"/> seconds: <paramref name="dilation"/> for
            /// dilation shapes, <paramref name="constriction"/> for the others.
            /// </summary>
            AnimationClip PupilClip(string name, IEnumerable<FTPupilEmulationPlan.Shape> shapes, float dilation, float constriction, float length)
            {
                var clip = new AnimationClip { name = name };
                foreach (var shape in shapes) SetCurve(clip, shape.Name, Constant(shape.IsDilation ? dilation : constriction, length));
                return Save(clip);
            }

            /// <summary>
            /// Each eye's pupil shapes follow its signed tracking value, played together in a Direct tree: -1 fully
            /// constricted, 0 at rest, 1 fully dilated.
            /// </summary>
            Motion TrackedMotion(string name, float length)
            {
                var root = Tree($"Pupils - {name} Tracked", BlendTreeType.Direct, null);
                root.children = plan.Channels.Select(channel =>
                {
                    AddFloat(channel.Parameter, 0f);
                    var tree = Tree($"Pupils - {name} Tracked {channel.Side}", BlendTreeType.Simple1D, channel.Parameter);
                    if (channel.ConstrictShapes.Any()) tree.AddChild(PupilClip($"Pupils - {name} {channel.Side} Constricted", channel.Shapes, 0f, 100f, length), -1f);
                    tree.AddChild(PupilClip($"Pupils - {name} {channel.Side} 0", channel.Shapes, 0f, 0f, length), 0f);
                    if (channel.DilationShapes.Any()) tree.AddChild(PupilClip($"Pupils - {name} {channel.Side} Dilated", channel.Shapes, 100f, 0f, length), 1f);
                    return Child(tree, OneParameter);
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

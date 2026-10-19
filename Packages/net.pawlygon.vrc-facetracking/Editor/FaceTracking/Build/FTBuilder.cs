using System;
using System.Collections.Generic;
using UnityEditor;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// The build hook a builder runs in. Each pass commits its own <see cref="FTBuildContext"/> to the descriptor.
    /// </summary>
    internal enum FTPass
    {
        // Before NDMF / Modular Avatar and VRCFury: the descriptor's face tracking setup, and the components'
        // own FX layers, parameters and menu controls, which those tools then merge
        Descriptor,
        // After VRCFury and Modular Avatar merged everything: changes to layers that only exist in the merged
        // FX controller, and anything that reads it
        Animator,
    }

    /// <summary>
    /// Order in which component builders run within a pass. Components in the same phase run in hierarchy order.
    /// </summary>
    internal enum FTPhase
    {
        // Base face tracking configuration (meshes, base set, visemes, eyes)
        Setup = 100,
        // Parameter behaviors (forward to animator, blendshape fixes)
        Behaviors = 300,
        // Animator changes (guards, driver layers, menus)
        Animator = 400,
    }

    /// <summary>
    /// Applies one component type to the build context, in one or both passes. Builders are discovered automatically.
    /// </summary>
    internal abstract class FTBuilder
    {
        public abstract Type ComponentType { get; }
        public abstract FTPhase Phase { get; }
        public abstract void Build(FTPass pass, PawlygonFTComponent component, FTBuildContext context);
    }

    internal abstract class FTBuilder<T> : FTBuilder where T : PawlygonFTComponent
    {
        public sealed override Type ComponentType => typeof(T);

        public sealed override void Build(FTPass pass, PawlygonFTComponent component, FTBuildContext context)
        {
            if (pass == FTPass.Descriptor) BuildDescriptor((T)component, context);
            else BuildAnimator((T)component, context);
        }

        /// <summary>
        /// Before NDMF and VRCFury. Descriptor face tracking data, and new layers, parameters and menu controls
        /// through <see cref="FTBuildContext.Generated"/>, never edits to the avatar's own controllers.
        /// </summary>
        protected virtual void BuildDescriptor(T component, FTBuildContext context) { }

        /// <summary>After NDMF and VRCFury, with their controllers merged into the playable layers.</summary>
        protected virtual void BuildAnimator(T component, FTBuildContext context) { }
    }

    internal static class FTBuilderRegistry
    {
        static Dictionary<Type, FTBuilder> builders;

        public static FTBuilder Get(Type componentType)
        {
            if (builders == null) Load();
            return builders.TryGetValue(componentType, out var builder) ? builder : null;
        }

        static void Load()
        {
            builders = new Dictionary<Type, FTBuilder>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<FTBuilder>())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition) continue;

                var builder = (FTBuilder)Activator.CreateInstance(type, true);
                if (builders.TryGetValue(builder.ComponentType, out var existing))
                {
                    throw new InvalidOperationException(
                        $"{builder.ComponentType.Name} has two builders: {existing.GetType().Name} and {type.Name}");
                }
                builders[builder.ComponentType] = builder;
            }
        }
    }
}

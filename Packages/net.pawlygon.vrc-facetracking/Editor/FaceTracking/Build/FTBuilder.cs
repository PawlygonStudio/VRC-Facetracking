using System;
using System.Collections.Generic;
using UnityEditor;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Order in which component builders run. Components in the same phase run in hierarchy order.
    /// </summary>
    internal enum FTPhase
    {
        // Base face tracking configuration (meshes, base set, visemes, eyes)
        Setup = 100,
        // Changes to blendshape sets (splits, weights)
        Sets = 200,
        // Parameter behaviors (forward to animator, blendshape limits)
        Behaviors = 300,
        // Animator changes (toggles, menus, set weight states)
        Animator = 400,
    }

    /// <summary>
    /// Applies one component type to the build context. Builders are discovered automatically.
    /// </summary>
    internal abstract class FTBuilder
    {
        public abstract Type ComponentType { get; }
        public abstract FTPhase Phase { get; }
        public abstract void Build(PawlygonFTComponent component, FTBuildContext context);
    }

    internal abstract class FTBuilder<T> : FTBuilder where T : PawlygonFTComponent
    {
        public sealed override Type ComponentType => typeof(T);

        public sealed override void Build(PawlygonFTComponent component, FTBuildContext context)
        {
            Build((T)component, context);
        }

        protected abstract void Build(T component, FTBuildContext context);
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

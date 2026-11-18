using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    /// <summary>An avatar that only exists in memory: a descriptor, meshes with the given blendshapes, and components.</summary>
    internal sealed class TestAvatar : IDisposable
    {
        public readonly GameObject Root;
        public readonly VRCAvatarDescriptor Descriptor;

        readonly List<Object> created = new List<Object>();

        public TestAvatar(string name = "Test Avatar")
        {
            Root = new GameObject(name);
            Descriptor = Root.AddComponent<VRCAvatarDescriptor>();
        }

        /// <summary>Every tracking key's own name, as an avatar with full Unified Expressions blendshapes has them.</summary>
        public static string[] AllKeyNames => Enumerable.Range(0, FTKeys.KeyCount).Select(FTKeys.KeyName).ToArray();

        public SkinnedMeshRenderer AddMesh(string name, params string[] blendshapes)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(Root.transform, false);

            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            var deltas = new Vector3[3];
            foreach (var blendshape in blendshapes) mesh.AddBlendShapeFrame(blendshape, 100f, deltas, null, null);
            created.Add(mesh);

            var renderer = holder.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            return renderer;
        }

        /// <summary>Adds a component on its own object inside the avatar, like the prefab does.</summary>
        public T Add<T>() where T : Component
        {
            var holder = new GameObject(typeof(T).Name);
            holder.transform.SetParent(Root.transform, false);
            return holder.AddComponent<T>();
        }

        /// <summary>A controller that isn't saved anywhere, set as the avatar's FX.</summary>
        public AnimatorController AddFX()
        {
            var controller = NewController("FX");
            Descriptor.customizeAnimationLayers = true;
            Descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = false,
                    animatorController = controller,
                },
            };
            return controller;
        }

        public AnimatorController NewController(string name)
        {
            var controller = new AnimatorController { name = name };
            created.Add(controller);
            return controller;
        }

        public void Dispose()
        {
            if (Root != null) Object.DestroyImmediate(Root);
            foreach (var item in created.Where(o => o != null)) Object.DestroyImmediate(item);
        }
    }
}

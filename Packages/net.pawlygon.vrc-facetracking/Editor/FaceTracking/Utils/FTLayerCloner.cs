using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Gives an FX layer its own copy of its state machine before it's edited. A copied controller file can still
    /// point at state machines, states or transitions stored in other files (VRCFury and NDMF don't always clone a
    /// controller fully either), and editing those would change the other controller. Motions are left shared,
    /// since they're never edited.
    /// </summary>
    internal static class FTLayerCloner
    {
        public static void CloneStateMachine(AnimatorController controller, int layerIndex)
        {
            var layers = controller.layers;
            var original = layers[layerIndex].stateMachine;
            if (original == null) return;

            var clones = new Dictionary<Object, Object>();
            Collect(original, clones);
            foreach (var source in new List<Object>(clones.Keys))
            {
                var clone = Object.Instantiate(source);
                clone.name = source.name;
                clone.hideFlags = source.hideFlags;
                clones[source] = clone;
            }

            var saveInController = AssetDatabase.Contains(controller);
            foreach (var clone in clones.Values)
            {
                Remap(clone, clones);
                if (saveInController) AssetDatabase.AddObjectToAsset(clone, controller);
            }

            layers[layerIndex].stateMachine = (AnimatorStateMachine)clones[original];
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
        }

        /// <summary>Whether every object in the state machine's graph is stored in the asset at <paramref name="path"/>.</summary>
        public static bool IsStoredIn(AnimatorStateMachine machine, string path)
        {
            if (machine == null) return true;
            var graph = new Dictionary<Object, Object>();
            Collect(machine, graph);
            foreach (var node in graph.Keys)
            {
                if (AssetDatabase.GetAssetPath(node) != path) return false;
            }
            return true;
        }

        /// <summary>The state machine graph: sub-state machines, states, transitions and behaviours.</summary>
        static void Collect(Object node, Dictionary<Object, Object> found)
        {
            if (node == null || found.ContainsKey(node) || !IsGraphObject(node)) return;
            found.Add(node, null);

            using (var serialized = new SerializedObject(node))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference) Collect(property.objectReferenceValue, found);
                }
            }
        }

        static void Remap(Object clone, Dictionary<Object, Object> clones)
        {
            using (var serialized = new SerializedObject(clone))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var target = property.objectReferenceValue;
                    if (target != null && clones.TryGetValue(target, out var replacement)) property.objectReferenceValue = replacement;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static bool IsGraphObject(Object node)
        {
            return node is AnimatorStateMachine || node is AnimatorState || node is AnimatorTransitionBase || node is StateMachineBehaviour;
        }
    }
}

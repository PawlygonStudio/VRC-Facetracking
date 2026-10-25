using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Meshes VRCFury Full Controllers configure for face tracking. VRCFury lets only one place configure a mesh (the
    /// avatar descriptor or one Full Controller) and stops the build otherwise, so the descriptor setup leaves them out.
    /// Read from the components' serialized data, so VRCFury doesn't need to be installed.
    /// </summary>
    internal static class FTFullControllerMeshes
    {
        /// <summary>Each claimed mesh, with the path of the object holding the Full Controller.</summary>
        public static Dictionary<SkinnedMeshRenderer, string> Find(GameObject avatarRoot)
        {
            var claimed = new Dictionary<SkinnedMeshRenderer, string>();
            var animator = avatarRoot.GetComponent<Animator>();
            foreach (var behaviour in avatarRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().FullName != "VF.Model.VRCFury") continue;
                if (FTAvatarUtils.IsEditorOnly(behaviour.transform, avatarRoot.transform)) continue;

                using (var serialized = new SerializedObject(behaviour))
                {
                    var content = serialized.FindProperty("content");
                    if (content == null || !content.managedReferenceFullTypename.EndsWith("FullController")) continue;
                    var meshes = content.FindPropertyRelative("faceTrackingMeshes");
                    if (meshes == null || !meshes.isArray) continue;

                    var holder = FTAvatarUtils.GetPath(behaviour.transform, avatarRoot.transform);
                    for (var i = 0; i < meshes.arraySize; i++)
                    {
                        var mesh = Resolve(meshes.GetArrayElementAtIndex(i), avatarRoot.transform, animator);
                        if (mesh != null && !claimed.ContainsKey(mesh)) claimed.Add(mesh, holder);
                    }
                }
            }
            return claimed;
        }

        /// <summary>
        /// Resolves a VRCFury object reference like VRCFury does: a humanoid bone, an object, or the avatar root, then
        /// an optional path below it. Returns null when it doesn't point at a mesh on the avatar.
        /// </summary>
        static SkinnedMeshRenderer Resolve(SerializedProperty reference, Transform avatarRoot, Animator animator)
        {
            Transform target;
            if (reference.FindPropertyRelative("useBone")?.boolValue == true)
            {
                var bone = reference.FindPropertyRelative("bone");
                target = animator != null && animator.isHuman && bone != null ? animator.GetBoneTransform((HumanBodyBones)bone.intValue) : null;
            }
            else if (reference.FindPropertyRelative("useObj")?.boolValue == true)
            {
                target = (reference.FindPropertyRelative("obj")?.objectReferenceValue as GameObject)?.transform;
            }
            else
            {
                target = avatarRoot;
            }

            var offset = reference.FindPropertyRelative("offset")?.stringValue;
            if (target != null && !string.IsNullOrWhiteSpace(offset)) target = target.Find(offset.Trim());
            if (target == null || !target.IsChildOf(avatarRoot)) return null;
            return target.GetComponent<SkinnedMeshRenderer>();
        }
    }
}

using System;
using UnityEngine;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking
{
    /// <summary>
    /// Base class for every Pawlygon face tracking component. Components only hold configuration:
    /// they are applied to a copy of the avatar at build time and are never uploaded.
    /// </summary>
    public abstract class PawlygonFTComponent : MonoBehaviour, IEditorOnly, ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector] private int version = -1;

        public int Version => version;
        public abstract int LatestVersion { get; }
        public bool IsTooNew => version > LatestVersion;

        /// <summary>
        /// Brings data saved by an older version of the component up to date.
        /// Called explicitly by the inspector and the build, never from OnValidate.
        /// </summary>
        public void Upgrade()
        {
            if (version < 0 || version >= LatestVersion) return;
            OnUpgrade(version);
            version = LatestVersion;
        }

        protected virtual void OnUpgrade(int fromVersion) { }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            // -1 means the component was just created, so its data is already in the latest format
            if (version < 0) version = LatestVersion;
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() { }
    }

    /// <summary>
    /// Marks a component type that may only appear once per avatar.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class FTSingleInstanceAttribute : Attribute { }
}

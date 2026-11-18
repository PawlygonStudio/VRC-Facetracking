using System.Runtime.CompilerServices;

// Optional integrations, compiled only when their tool is installed
[assembly: InternalsVisibleTo("Pawlygon.FaceTracking.Editor.VRCFury")]
[assembly: InternalsVisibleTo("Pawlygon.FaceTracking.Editor.ModularAvatar")]

// The tests package in the repository, which isn't part of the released package
[assembly: InternalsVisibleTo("Pawlygon.FaceTracking.Editor.Tests")]

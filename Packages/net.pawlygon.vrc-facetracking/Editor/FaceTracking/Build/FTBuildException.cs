using System;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>A build failure with a message meant for the user.</summary>
    internal class FTBuildException : Exception
    {
        public FTBuildException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>The user chose to stop the build. Not reported as an error.</summary>
    internal sealed class FTBuildCancelledException : Exception
    {
        public FTBuildCancelledException(string message) : base(message) { }
    }
}

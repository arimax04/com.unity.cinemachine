#if CINEMACHINE_UNITY_SPLINES
using UnityEngine;
using System;
using Cinemachine.Utility;
using UnityEngine.Splines;

namespace Cinemachine
{
    /// <summary>
    /// A Cinemachine Virtual Camera Body component that constrains camera motion
    /// to a Spline.  The camera can move along the spline.
    ///
    /// This behaviour can operate in two modes: manual positioning, and Auto-Dolly positioning.
    /// In Manual mode, the camera's position is specified by animating the Spline Position field.
    /// In Auto-Dolly mode, the Spline Position field is animated automatically every frame by finding
    /// the position on the spline that's closest to the virtual camera's Follow target.
    /// </summary>
    [DocumentationSorting(DocumentationSortingAttribute.Level.UserRef)]
    [AddComponentMenu("")] // Don't display in add component menu
    [SaveDuringPlay]
    public class CinemachineSplineDolly : CinemachineComponentBase
    {
        /// <summary>The Spline container to which the camera will be constrained.  This must be non-null.</summary>
        [Tooltip("The Spline container to which the camera will be constrained.  This must be non-null.")]
        public SplineContainer m_Spline;

        /// <summary>The position along the spline at which the camera will be placed. This can be animated directly,
        /// or set automatically by the Auto-Dolly feature to get as close as possible to the Follow target.
        /// The value is interpreted according to the Position Units setting.</summary>
        [Tooltip("The position along the spline at which the camera will be placed.  "
           + "This can be animated directly, or set automatically by the Auto-Dolly feature to "
            + "get as close as possible to the Follow target.  The value is interpreted "
            + "according to the Position Units setting.")]
        public float m_SplinePosition;

        /// <summary>How to interpret the Spline Position:
        /// - Distance: Values range from 0 (start of Spline) to Length of the Spline (end of Spline).
        /// - Normalized: Values range from 0 (start of Spline) to 1 (end of Spline).
        /// - Knot: Values are defined by knot indices and a fractional value representing the normalized
        /// interpolation between the specific knot index and the next knot."</summary>
        [Tooltip("How to interpret the Spline Position:\n"+
            "- Distance: Values range from 0 (start of Spline) to Length of the Spline (end of Spline).\n"+
            "- Normalized: Values range from 0 (start of Spline) to 1 (end of Spline).\n"+
            "- Knot: Values are defined by knot indices and a fractional value representing the normalized " +
            "interpolation between the specific knot index and the next knot.\n")]
        public PathIndexUnit m_PositionUnits = PathIndexUnit.Normalized;

        /// <summary>Where to put the camera realtive to the spline postion.  X is perpendicular 
        /// to the spline, Y is up, and Z is parallel to the spline.</summary>
        [Tooltip("Where to put the camera relative to the spline position.  X is perpendicular "
            + "to the spline, Y is up, and Z is parallel to the spline.")]
        public Vector3 m_SplineOffset = Vector3.zero;

        /// <summary>How aggressively the camera tries to maintain the offset along the x, y, or z directions in spline local space.
        /// Meaning:
        /// - x represents the axis that is perpendicular to the spline.
        /// - y represents the axis that is defined by the spline-local up direction.
        /// - z represents the axis that is parallel to the spline.
        /// Smaller numbers are more responsive. Larger numbers give a more heavy, slowly responding camera.
        /// Using different settings per axis can yield a wide range of camera behaviors.</summary>
        [Tooltip("How aggressively the camera tries to maintain the offset along the x, y, or z directions in spline local space. \n" +
            "- x represents the axis that is perpendicular to the spline. \n" +
            "- y represents the axis that is defined by the spline-local up direction. \n" +
            "- z represents the axis that is parallel to the spline. \n" +
            "Smaller numbers are more responsive, larger numbers give a more heavy, slowly responding camera. " +
            "Using different settings per axis can yield a wide range of camera behaviors.")]
        public Vector3 m_Damping = Vector3.zero;

        /// <summary>Different ways to set the camera's up vector</summary>
        [DocumentationSorting(DocumentationSortingAttribute.Level.UserRef)]
        public enum CameraUpMode
        {
            /// <summary>Leave the camera's up vector alone.  It will be set according to the Brain's WorldUp.</summary>
            Default,
            /// <summary>Take the up vector from the spline's up vector at the current point</summary>
            Spline,
            /// <summary>Take the up vector from the spline's up vector at the current point, but with the roll zeroed out</summary>
            SplineNoRoll,
            /// <summary>Take the up vector from the Follow target's up vector</summary>
            FollowTarget,
            /// <summary>Take the up vector from the Follow target's up vector, but with the roll zeroed out</summary>
            FollowTargetNoRoll,
        };

        /// <summary>How to set the virtual camera's Up vector.  This will affect the screen composition.</summary>
        [Tooltip("How to set the virtual camera's Up vector.  This will affect the screen composition, because "
            + "the camera Aim behaviours will always try to respect the Up direction.")]
        public CameraUpMode m_CameraUp = CameraUpMode.Default;

        /// <summary>
        /// How aggressively the camera tries to track the target rotation (x: Pitch, y: Yaw, z: Roll).
        /// Smaller numbers are more responsive. Larger numbers give a more heavy, slowly responding camera.</summary>
        /// </summary>
        public Vector3 m_RotationDamping = Vector3.zero;

        /// <summary>Controls how automatic dollying occurs</summary>
        [DocumentationSorting(DocumentationSortingAttribute.Level.UserRef)]
        [Serializable]
        public struct AutoDolly
        {
            /// <summary>If checked, will enable automatic dolly, which chooses a spline position
            /// that is as close as possible to the Follow target.</summary>
            [Tooltip("If checked, will enable automatic dolly, which chooses a spline position that is as "
                + "close as possible to the Follow target.  Note: this can have significant performance impact")]
            public bool m_Enabled;

            /// <summary>Offset, in current position units, from the closest point on the spline to the follow target.</summary>
            [Tooltip("Offset, in current position units, from the closest point on the spline to the follow target")]
            public float m_PositionOffset;

            /// <summary>
            /// Affects how many segments to split a spline into when calculating the nearest point.
            /// Higher values mean smaller and more segments, which increases accuracy at the cost of processing time.
            /// In most cases, the default resolution is appropriate. Use with <seealso cref="m_SearchIteration"/> to fine-tune point accuracy.
            /// For more information, see SplineUtility.GetNearestPoint.
            /// </summary>
            [Tooltip("Affects how many segments to split a spline into when calculating the nearest point." +
                "Higher values mean smaller and more segments, which increases accuracy at the cost of processing time. " +
                "In most cases, the default value (4) is appropriate. Use with SearchIteration to fine-tune point accuracy.")]
            public int m_SearchResolution;

            /// <summary>
            /// The nearest point is calculated by finding the nearest point on the entire length
            /// of the spline using <seealso cref="m_SearchResolution"/> to divide into equally spaced line segments. Successive
            /// iterations will then subdivide further the nearest segment, producing more accurate results. In most cases,
            /// the default value is sufficient.
            /// For more information, see SplineUtility.GetNearestPoint.
            /// </summary>
            [Tooltip("The nearest point is calculated by finding the nearest point on the entire length of the spline " +
                "using SearchResolution to divide into equally spaced line segments. Successive iterations will then " +
                "subdivide further the nearest segment, producing more accurate results. In most cases, the default value (2) is sufficient.")]
            public int m_SearchIteration;

            /// <summary>Constructor with specific field values</summary>
            /// <param name="enabled">Whether to enable automatic dolly</param>
            /// <param name="positionOffset">Offset, in current position units, from the closest point on the spline to the follow target</param>
            /// <param name="searchResolution">We search between waypoints by dividing the segment into this many straight pieces</param>
            /// <param name="searchIteration">Straight pieces defined by searchResolution are further subdivided.</param>
            public AutoDolly(bool enabled, float positionOffset, int searchResolution, int searchIteration)
            {
                m_Enabled = enabled;
                m_PositionOffset = positionOffset;
                m_SearchResolution = searchResolution;
                m_SearchIteration = searchIteration;
            }
        };

        /// <summary>Controls how automatic dollying occurs</summary>
        [Tooltip("Controls how automatic dollying occurs.  A Follow target is necessary to use this feature.")]
        public AutoDolly m_AutoDolly = new AutoDolly(false, 0, 4, 2);

        /// <summary>True if component is enabled and has a spline</summary>
        public override bool IsValid { get { return enabled && m_Spline != null; } }

        /// <summary>Get the Cinemachine Pipeline stage that this component implements.
        /// Always returns the Body stage</summary>
        public override CinemachineCore.Stage Stage { get { return CinemachineCore.Stage.Body; } }

        /// <summary>
        /// Report maximum damping time needed for this component.
        /// </summary>
        /// <returns>Highest damping setting in this component</returns>
        public override float GetMaxDampTime() 
        { 
            var d2 = AngularDamping;
            var a = Mathf.Max(m_Damping.x, Mathf.Max(m_Damping.y, m_Damping.z)); 
            var b = Mathf.Max(d2.x, Mathf.Max(d2.y, d2.z)); 
            return Mathf.Max(a, b); 
        }
        
        /// <summary>
        /// Subscribe to onSplineChanged if you'd like to react to changes to the Spline attached to this vcam.
        /// This action is invoked by the Spline's changed event when a spline property is modified.
        /// </summary>
        public event Action onSplineChanged;

        bool m_Registered = false;
        SplineContainer m_SplineCache;
        void OnValidate()
        {
            if (m_SplineCache != null)
            {
                m_SplineCache.Spline.changed -= onSplineChanged;
                m_SplineCache = m_Spline;
                m_Registered = false;
            }
            if (!m_Registered && m_Spline != null && m_Spline.Spline != null)
            {
                m_Registered = true;
                m_SplineCache = m_Spline;
                m_Spline.Spline.changed += onSplineChanged;
            }

            m_Damping.x = Mathf.Clamp(m_Damping.x, 0, 20);
            m_Damping.y = Mathf.Clamp(m_Damping.y, 0, 20);
            m_Damping.z = Mathf.Clamp(m_Damping.z, 0, 20);
            m_RotationDamping.x = Mathf.Clamp(m_RotationDamping.x, 0, 20);
            m_RotationDamping.y = Mathf.Clamp(m_RotationDamping.y, 0, 20);
            m_RotationDamping.z = Mathf.Clamp(m_RotationDamping.z, 0, 20);
        }

        void UpdateSplineRollOverrideCache()
        {
            m_RollOverrideExtension = null;
            // if vcam has an override, use that
            if (transform.parent.TryGetComponent(out m_RollOverrideExtension))
            {
#if UNITY_EDITOR
                m_RollOverrideExtension.splineContainer = m_Spline; // this is needed by the RollHandle to work in the scene view
#endif
            }
            // else if the spline has an override, use that
            else if (m_Spline.TryGetComponent(out m_RollOverrideExtension)) {}
        }

        CinemachineSplineRollOverrideExtension m_RollOverrideExtension; // don't use this directly
        CinemachineSplineRollOverrideExtension RollOverrideExtension
        {
            get
            {
                if(m_RollOverrideExtension == null)
                    UpdateSplineRollOverrideCache();

                return m_RollOverrideExtension;
            }
        }
        /// <summary>Positions the virtual camera according to the transposer rules.</summary>
        /// <param name="curState">The current camera state</param>
        /// <param name="deltaTime">Used for damping.  If less that 0, no damping is done.</param>
        public override void MutateCameraState(ref CameraState curState, float deltaTime)
        {
            if (!IsValid)
            {
                m_SplinePosition = 0;
                return;
            }

            var spline = m_Spline.Spline;
            if (spline == null || spline.Count == 0)
            {
                return;
            }
            if (spline.Count == 1)
            {
                curState.RawPosition = m_PreviousCameraPosition = spline[0].Position;
                curState.RawOrientation = m_PreviousOrientation = spline[0].Rotation;
                return;
            }
            
            // splines work with normalized position by default, so we convert m_SplinePosition to normalized at the start
            m_SplinePosition = 
                SplineUtility.ConvertIndexUnit(spline, m_SplinePosition, m_PositionUnits, PathIndexUnit.Normalized);
            
            // Init previous frame state info
            if (deltaTime < 0 || !VirtualCamera.PreviousStateIsValid)
            {
                m_PreviousNormalizedSplinePosition = m_SplinePosition;
                m_PreviousCameraPosition = curState.RawPosition;
                m_PreviousOrientation = curState.RawOrientation;
            }

            // Get the new ideal spline base position
            if (m_AutoDolly.m_Enabled && FollowTarget != null)
            {
                // convert follow target into spline local space, because SplineUtility works in spline local space
                SplineUtility.GetNearestPoint(spline, 
                    m_Spline.transform.InverseTransformPoint(FollowTargetPosition), out _, out m_SplinePosition, 
                    m_AutoDolly.m_SearchResolution, m_AutoDolly.m_SearchIteration);
                // Apply the spline position offset
                m_SplinePosition += m_AutoDolly.m_PositionOffset;
            }
            float newSplinePosition = m_SplinePosition;

            if (deltaTime >= 0 && VirtualCamera.PreviousStateIsValid)
            {
                float maxUnit = 1; // we are always using normalized unit [0-1]
                {
                    float prev = m_PreviousNormalizedSplinePosition;
                    float next = newSplinePosition;
                    if (spline.Closed && Mathf.Abs(next - prev) > maxUnit / 2)
                    {
                        if (next > prev)
                            prev += maxUnit;
                        else
                            prev -= maxUnit;
                    }
                    m_PreviousNormalizedSplinePosition = prev;
                    newSplinePosition = next;
                }

                // Apply damping in the spline direction
                float offset = m_PreviousNormalizedSplinePosition - newSplinePosition;
                offset = Damper.Damp(offset, m_Damping.z, deltaTime);
                newSplinePosition = m_PreviousNormalizedSplinePosition - offset;
            }
            m_PreviousNormalizedSplinePosition = newSplinePosition;
            m_Spline.Evaluate(newSplinePosition, 
                out var localPosition, out var localTangent, out var localUp);
            Vector3 newCameraPos = localPosition;
            var newSplineOrientation = 
                Vector3.SqrMagnitude(localTangent) == 0 || Vector3.SqrMagnitude(localUp) == 0 ? 
                    Quaternion.identity : Quaternion.LookRotation(localTangent, localUp);
            
            if (RollOverrideExtension != null && RollOverrideExtension.enabled)
            {
                float roll = RollOverrideExtension.RollOverride.Evaluate(spline, newSplinePosition, 
                    PathIndexUnit.Normalized, new UnityEngine.Splines.Interpolators.LerpFloat());
                var rollRotation = Quaternion.AngleAxis(-roll, localTangent);
                newSplineOrientation = Quaternion.LookRotation(localTangent, rollRotation * localUp);
            }

            // Apply the offset to get the new camera position
            var offsetX = newSplineOrientation * Vector3.right;
            var offsetY = newSplineOrientation * Vector3.up;
            var offsetZ = newSplineOrientation * Vector3.forward;
            newCameraPos += m_SplineOffset.x * offsetX;
            newCameraPos += m_SplineOffset.y * offsetY;
            newCameraPos += m_SplineOffset.z * offsetZ;

            // Apply damping to the remaining directions
            if (deltaTime >= 0 && VirtualCamera.PreviousStateIsValid)
            {
                Vector3 currentCameraPos = m_PreviousCameraPosition;
                Vector3 delta = (currentCameraPos - newCameraPos);
                Vector3 delta1 = Vector3.Dot(delta, offsetY) * offsetY;
                Vector3 delta0 = delta - delta1;
                delta0 = Damper.Damp(delta0, m_Damping.x, deltaTime);
                delta1 = Damper.Damp(delta1, m_Damping.y, deltaTime);
                newCameraPos = currentCameraPos - (delta0 + delta1);
            }
            curState.RawPosition = m_PreviousCameraPosition = newCameraPos;

            // Set the orientation and up
            Quaternion newOrientation = GetCameraOrientationAtSplinePoint(newSplineOrientation, curState.ReferenceUp);
            if (deltaTime >= 0 && VirtualCamera.PreviousStateIsValid)
            {
                Vector3 relative = (Quaternion.Inverse(m_PreviousOrientation)
                    * newOrientation).eulerAngles;
                for (int i = 0; i < 3; ++i)
                    if (relative[i] > 180)
                        relative[i] -= 360;
                relative = Damper.Damp(relative, AngularDamping, deltaTime);
                newOrientation = m_PreviousOrientation * Quaternion.Euler(relative);
            }
            m_PreviousOrientation = newOrientation;

            curState.RawOrientation = newOrientation;
            if (m_CameraUp != CameraUpMode.Default)
                curState.ReferenceUp = curState.RawOrientation * Vector3.up;
            
            // convert unit back to user's preference
            m_SplinePosition = 
                SplineUtility.ConvertIndexUnit(spline, m_SplinePosition, PathIndexUnit.Normalized, m_PositionUnits);
        }

        Quaternion GetCameraOrientationAtSplinePoint(Quaternion splineOrientation, Vector3 up)
        {
            switch (m_CameraUp)
            {
                default:
                case CameraUpMode.Default: break;
                case CameraUpMode.Spline: return splineOrientation;
                case CameraUpMode.SplineNoRoll:
                    return Quaternion.LookRotation(splineOrientation * Vector3.forward, up);
                case CameraUpMode.FollowTarget:
                    if (FollowTarget != null)
                        return FollowTargetRotation;
                    break;
                case CameraUpMode.FollowTargetNoRoll:
                    if (FollowTarget != null)
                        return Quaternion.LookRotation(FollowTargetRotation * Vector3.forward, up);
                    break;
            }
            return Quaternion.LookRotation(VirtualCamera.transform.rotation * Vector3.forward, up);
        }

        Vector3 AngularDamping
        {
            get
            {
                switch (m_CameraUp)
                {
                    case CameraUpMode.SplineNoRoll:
                    case CameraUpMode.FollowTargetNoRoll:
                        return new Vector3(m_RotationDamping.x, m_RotationDamping.y, 0);
                    case CameraUpMode.Default:
                        return Vector3.zero;
                    default:
                        return new Vector3(m_RotationDamping.x, m_RotationDamping.y, m_RotationDamping.z);
                }
            }
        }

        float m_PreviousNormalizedSplinePosition = 0;
        Quaternion m_PreviousOrientation = Quaternion.identity;
        Vector3 m_PreviousCameraPosition = Vector3.zero;
    }
}
#endif
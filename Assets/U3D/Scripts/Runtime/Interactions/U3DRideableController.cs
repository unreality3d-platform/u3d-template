using UnityEngine;
using Fusion;

namespace U3D
{
    public enum RideableMovementMode
    {
        Waypoints,
        Rotation,
        Static
    }

    public enum RideableLoopMode
    {
        Loop,
        PingPong
    }

    public class U3DRideableController : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] private RideableMovementMode movementMode = RideableMovementMode.Waypoints;

        [SerializeField] private Transform[] waypoints;
        [SerializeField] private RideableLoopMode loopMode = RideableLoopMode.Loop;
        [SerializeField] private float speed = 3f;
        [SerializeField] private float pauseAtWaypoint = 0f;

        [SerializeField] private Vector3 rotationAxis = Vector3.up;
        [SerializeField] private float rotationSpeed = 45f;

        // World-space waypoint positions cached at Spawned — waypoints live outside
        // the platform hierarchy so their positions never drift during movement.
        // Cached on every client so authority transfers don't lose target positions.
        private Vector3[] _waypointPositions;

        // Indexing state is networked so authority transfers (or late joiners) see
        // the correct waypoint progression. NetworkTransform on this object handles
        // position/rotation replication; we only need to network the bookkeeping.
        [Networked] private int NetworkCurrentWaypointIndex { get; set; }
        [Networked] private float NetworkPauseTimer { get; set; }
        [Networked] private NetworkBool NetworkPingPongForward { get; set; }

        public override void Spawned()
        {
            CacheWaypointPositions();

            if (Object.HasStateAuthority)
            {
                NetworkPingPongForward = true;
                NetworkCurrentWaypointIndex = 0;
                NetworkPauseTimer = 0f;
            }
        }

        private void CacheWaypointPositions()
        {
            if (movementMode != RideableMovementMode.Waypoints) return;

            if (waypoints == null || waypoints.Length == 0)
            {
                Debug.LogWarning($"U3DRideableController on '{name}': Movement Mode is Waypoints but no waypoints are assigned.");
                _waypointPositions = new Vector3[0];
                return;
            }

            _waypointPositions = new Vector3[waypoints.Length];
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] != null)
                    _waypointPositions[i] = waypoints[i].position;
                else
                    _waypointPositions[i] = transform.position;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            switch (movementMode)
            {
                case RideableMovementMode.Waypoints:
                    TickWaypoints();
                    break;
                case RideableMovementMode.Rotation:
                    TickRotation();
                    break;
                case RideableMovementMode.Static:
                    break;
            }
        }

        private void TickWaypoints()
        {
            if (_waypointPositions == null || _waypointPositions.Length == 0) return;

            if (NetworkPauseTimer > 0f)
            {
                NetworkPauseTimer -= Runner.DeltaTime;
                return;
            }

            Vector3 target = _waypointPositions[NetworkCurrentWaypointIndex];
            Vector3 toTarget = target - transform.position;
            float distanceThisFrame = speed * Runner.DeltaTime;

            if (toTarget.magnitude <= distanceThisFrame)
            {
                transform.position = target;
                NetworkPauseTimer = pauseAtWaypoint;
                AdvanceWaypoint();
            }
            else
            {
                transform.position += toTarget.normalized * distanceThisFrame;
            }
        }

        private void AdvanceWaypoint()
        {
            if (_waypointPositions.Length <= 1) return;

            if (loopMode == RideableLoopMode.Loop)
            {
                NetworkCurrentWaypointIndex = (NetworkCurrentWaypointIndex + 1) % _waypointPositions.Length;
            }
            else // PingPong
            {
                if (NetworkPingPongForward)
                {
                    int next = NetworkCurrentWaypointIndex + 1;
                    if (next >= _waypointPositions.Length)
                    {
                        NetworkCurrentWaypointIndex = _waypointPositions.Length - 2;
                        NetworkPingPongForward = false;
                    }
                    else
                    {
                        NetworkCurrentWaypointIndex = next;
                    }
                }
                else
                {
                    int next = NetworkCurrentWaypointIndex - 1;
                    if (next < 0)
                    {
                        NetworkCurrentWaypointIndex = 1;
                        NetworkPingPongForward = true;
                    }
                    else
                    {
                        NetworkCurrentWaypointIndex = next;
                    }
                }
            }
        }

        private void TickRotation()
        {
            float angle = rotationSpeed * Runner.DeltaTime;
            transform.Rotate(rotationAxis.normalized, angle, Space.Self);
        }
    }
}
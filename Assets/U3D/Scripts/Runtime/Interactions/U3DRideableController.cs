using Fusion;
using UnityEngine;

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

    [RequireComponent(typeof(NetworkObject))]
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

        [Networked] private Vector3 NetworkedPosition { get; set; }
        [Networked] private Quaternion NetworkedRotation { get; set; }

        private Vector3 _previousPosition;
        private Quaternion _previousRotation;

        // World-space waypoint positions cached at spawn — waypoints are children so
        // their transform.position would drift with the parent during movement.
        private Vector3[] _waypointPositions;

        private int _currentWaypointIndex;
        private float _pauseTimer;
        private bool _pingPongForward = true;

        public override void Spawned()
        {
            NetworkedPosition = transform.position;
            NetworkedRotation = transform.rotation;
            _previousPosition = transform.position;
            _previousRotation = transform.rotation;

            CacheWaypointPositions();
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
            _previousPosition = transform.position;
            _previousRotation = transform.rotation;

            if (HasStateAuthority)
            {
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

                NetworkedPosition = transform.position;
                NetworkedRotation = transform.rotation;
            }
            else
            {
                transform.position = NetworkedPosition;
                transform.rotation = NetworkedRotation;
            }
        }

        private void TickWaypoints()
        {
            if (_waypointPositions == null || _waypointPositions.Length == 0) return;

            if (_pauseTimer > 0f)
            {
                _pauseTimer -= Runner.DeltaTime;
                return;
            }

            Vector3 target = _waypointPositions[_currentWaypointIndex];
            Vector3 toTarget = target - transform.position;
            float distanceThisFrame = speed * Runner.DeltaTime;

            if (toTarget.magnitude <= distanceThisFrame)
            {
                transform.position = target;
                _pauseTimer = pauseAtWaypoint;
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
                _currentWaypointIndex = (_currentWaypointIndex + 1) % _waypointPositions.Length;
            }
            else // PingPong
            {
                if (_pingPongForward)
                {
                    _currentWaypointIndex++;
                    if (_currentWaypointIndex >= _waypointPositions.Length)
                    {
                        _currentWaypointIndex = _waypointPositions.Length - 2;
                        _pingPongForward = false;
                    }
                }
                else
                {
                    _currentWaypointIndex--;
                    if (_currentWaypointIndex < 0)
                    {
                        _currentWaypointIndex = 1;
                        _pingPongForward = true;
                    }
                }
            }
        }

        private void TickRotation()
        {
            float angle = rotationSpeed * Runner.DeltaTime;
            transform.Rotate(rotationAxis.normalized, angle, Space.Self);
        }

        public void GetTickDelta(out Vector3 posDelta, out float yawDelta)
        {
            posDelta = transform.position - _previousPosition;

            float prevYaw = _previousRotation.eulerAngles.y;
            float currYaw = transform.rotation.eulerAngles.y;
            yawDelta = Mathf.DeltaAngle(prevYaw, currYaw);
        }

        private void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<U3DPlayerController>();
            if (player == null) return;
            player.MountRideable(this);
        }

        private void OnTriggerExit(Collider other)
        {
            var player = other.GetComponentInParent<U3DPlayerController>();
            if (player == null) return;
            player.DismountRideable(this);
        }
    }
}
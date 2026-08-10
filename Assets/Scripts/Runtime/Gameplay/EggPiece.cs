using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Pooled match-3 piece carrying archetype, elemental, physics, and board state.</summary>
    public sealed class EggPiece : MonoBehaviour, IPoolable
    {
        private Vector3 gravityVector = Vector3.back;
        private WeatherState currentWeather = WeatherState.Clear;
        private float baselineLinearDamping;

        /// <summary>Gets the combined elemental tags used for culinary reactions.</summary>
        public ElementalTag Tags { get; private set; }
        /// <summary>Gets the stable matching archetype name.</summary>
        public string Archetype { get; private set; }
        /// <summary>Gets or sets the piece's logical board coordinate without allocation.</summary>
        public Vector2Int BoardPosition { get; set; }
        /// <summary>Gets the cached Rigidbody, which may be null for a purely kinematic prefab.</summary>
        public Rigidbody Body { get; private set; }

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            if (Body != null)
            {
                baselineLinearDamping = Body.linearDamping;
            }
        }

        private void OnEnable()
        {
            WorldStateBus.OnGravityVectorChanged += HandleGravityChanged;
            WorldStateBus.OnWeatherChanged += HandleWeatherChanged;
        }

        private void OnDisable()
        {
            WorldStateBus.OnGravityVectorChanged -= HandleGravityChanged;
            WorldStateBus.OnWeatherChanged -= HandleWeatherChanged;
        }

        private void FixedUpdate()
        {
            if (Body != null && !Body.isKinematic)
            {
                float weatherGravityScale = currentWeather == WeatherState.Snow ? 0.85f : 1f;
                Body.AddForce(gravityVector * weatherGravityScale, ForceMode.Acceleration);
            }
        }

        /// <summary>Resets pool-generic transient transform state before activation.</summary>
        public void OnRentFromPool()
        {
            transform.localScale = Vector3.one;
            HandleWeatherChanged(WorldStateBus.Snapshot.Weather);
        }

        /// <summary>Initializes the piece after it has been rented from its pool.</summary>
        public void OnSpawnFromPool(string archetype, ElementalTag tags, Vector2Int boardPos)
        {
            Archetype = archetype;
            Tags = tags;
            BoardPosition = boardPos;
            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.isKinematic = true;
            }
        }

        /// <summary>Clears gameplay state without destroying the GameObject.</summary>
        public void OnReturnToPool()
        {
            Tags = ElementalTag.None;
            Archetype = null;
            BoardPosition = new Vector2Int(-1, -1);
            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.isKinematic = true;
            }
        }

        private void HandleGravityChanged(Vector3 value)
        {
            gravityVector = value;
        }

        private void HandleWeatherChanged(WeatherState value)
        {
            currentWeather = value;
            if (Body != null)
            {
                Body.linearDamping = value == WeatherState.Rain
                    ? baselineLinearDamping * 0.75f
                    : baselineLinearDamping;
            }
        }
    }
}

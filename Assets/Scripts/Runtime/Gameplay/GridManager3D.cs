using System;
using System.Collections.Generic;
using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Describes the geometric shape of a resolved match attack.</summary>
    public enum MatchShape
    {
        /// <summary>A straight horizontal or vertical match.</summary>
        Line,
        /// <summary>An intersecting match with one perpendicular arm.</summary>
        LShape,
        /// <summary>An intersecting match with two perpendicular arms.</summary>
        TShape
    }

    /// <summary>Applies a board effect without adding effect-specific branches to the grid core.</summary>
    public interface IBoardEffect
    {
        /// <summary>Applies the effect to a grid using caller-owned context.</summary>
        void Apply(GridManager3D grid, MatchContext context);
    }

    /// <summary>Owns the fixed 7x8 board, input, swaps, matches, scoring, cascades, and pooled refill.</summary>
    public sealed class GridManager3D : MonoBehaviour
    {
        [SerializeField] private int columns = 7;
        [SerializeField] private int rows = 8;
        [SerializeReference] private IObjectPool<EggPiece> piecePool;
        [SerializeField] private MonoBehaviour piecePoolProvider;
        [SerializeField] private Transform gridAnchor;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private LayerMask pieceLayerMask = ~0;
        [SerializeField, Min(0.1f)] private float cellSpacing = 1f;
        [SerializeField, Min(0f)] private float minimumDragPixels = 18f;
        [SerializeField, Min(0.02f)] private float scramblePhysicsSeconds = 0.18f;
        [SerializeField] private CulinaryReactionSystem culinaryReactionSystem;
        [SerializeField] private MeshFractureEngine meshFractureEngine;
        [SerializeField] private BossBattleManager bossBattleManager;

        private EggPiece[,] board;
        private readonly List<EggPiece> matchScratchBuffer = new List<EggPiece>(64);
        private bool[,] matchMarks;
        private int[,] lockTurns;
        private float[,] hazardExpiry;
        private bool[] columnShields;
        private readonly RaycastHit[] raycastHits = new RaycastHit[8];
        private System.Random random;
        private Vector2Int pressCell;
        private Vector2 pressScreenPosition;
        private bool hasPressCell;
        private bool initialized;
        private bool resolving;
        private Vector3 currentGravity = Vector3.back;
        private WeatherState currentWeather = WeatherState.Clear;
        private int cascadeIndex;
        private int scoreMultiplierMatches;
        private float scoreMultiplier = 1f;
        private float scoreMultiplierExpiry;
        private bool hasDeferredAreaClear;
        private Vector2Int deferredAreaCenter;
        private int deferredAreaRadius;
        private float scrambleSettleAt = -1f;

        /// <summary>Gets the score accumulated from resolved pieces in this board session.</summary>
        public int Score { get; private set; }
        /// <summary>Raised after a group resolves; invoked synchronously and does not allocate itself.</summary>
        public event Action<int, MatchShape, int> OnMatchResolved;

        private void Awake()
        {
            if (gridAnchor == null)
            {
                GameObject anchorObject = GameObject.Find("GridAnchor");
                if (anchorObject != null)
                {
                    gridAnchor = anchorObject.transform;
                }
            }

            if (gridAnchor == null)
            {
                Debug.LogError("[GridManager3D] Missing scene object named 'GridAnchor'. SceneAutoBuilder must create it before runtime initialization.", this);
            }

            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }

            if (piecePool == null && piecePoolProvider != null)
            {
                piecePool = piecePoolProvider as IObjectPool<EggPiece>;
            }
        }

        private void OnEnable()
        {
            WorldStateBus.OnGravityVectorChanged += HandleGravityChanged;
            WorldStateBus.OnWeatherChanged += HandleWeatherChanged;
            WorldStateBus.OnTemperatureThresholdCrossed += HandleTemperatureThresholdCrossed;
        }

        private void OnDisable()
        {
            WorldStateBus.OnGravityVectorChanged -= HandleGravityChanged;
            WorldStateBus.OnWeatherChanged -= HandleWeatherChanged;
            WorldStateBus.OnTemperatureThresholdCrossed -= HandleTemperatureThresholdCrossed;
        }

        private void Update()
        {
            if (scrambleSettleAt >= 0f)
            {
                if (Time.unscaledTime >= scrambleSettleAt)
                {
                    CompleteScramblePhysics();
                }
                return;
            }
            if (!initialized || resolving)
            {
                return;
            }

            HandleRaycastInput();
            ExpireHazards();
        }

        /// <summary>Allocates the board buffers once, pre-warms the pool, and creates a match-free board.</summary>
        public void Initialize(int seed)
        {
            if (initialized)
            {
                Debug.LogWarning("[GridManager3D] Initialize was ignored because board buffers are intentionally allocated only once.", this);
                return;
            }

            if (gridAnchor == null || piecePool == null)
            {
                Debug.LogError("[GridManager3D] Initialization requires GridAnchor and an IObjectPool<EggPiece> provider.", this);
                return;
            }

            columns = Mathf.Max(3, columns);
            rows = Mathf.Max(3, rows);
            board = new EggPiece[columns, rows];
            matchMarks = new bool[columns, rows];
            lockTurns = new int[columns, rows];
            hazardExpiry = new float[columns, rows];
            columnShields = new bool[columns];
            random = new System.Random(seed);
            currentWeather = WorldStateBus.Snapshot.Weather;
            piecePool.Prewarm(columns * rows);
            Score = 0;
            initialized = true;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    SpawnPieceAt(x, y, true);
                }
            }
        }

        private void HandleRaycastInput()
        {
            bool pressed = false;
            bool released = false;
            Vector2 screenPosition = default;

            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                screenPosition = touch.position;
                pressed = touch.phase == TouchPhase.Began;
                released = touch.phase == TouchPhase.Ended;
            }
            else
            {
                pressed = Input.GetMouseButtonDown(0);
                released = Input.GetMouseButtonUp(0);
                screenPosition = Input.mousePosition;
            }

            if (!pressed && !released)
            {
                return;
            }

            Vector2Int cell;
            if (!TryRaycastCell(screenPosition, out cell))
            {
                if (released)
                {
                    hasPressCell = false;
                }
                return;
            }

            if (pressed)
            {
                pressCell = cell;
                pressScreenPosition = screenPosition;
                hasPressCell = true;
            }
            else if (released && hasPressCell)
            {
                if (cell == pressCell)
                {
                    Vector2 drag = screenPosition - pressScreenPosition;
                    if (drag.sqrMagnitude < minimumDragPixels * minimumDragPixels)
                    {
                        hasPressCell = false;
                        return;
                    }

                    if (Mathf.Abs(drag.x) > Mathf.Abs(drag.y))
                    {
                        cell = pressCell + (drag.x >= 0f ? Vector2Int.right : Vector2Int.left);
                    }
                    else
                    {
                        cell = pressCell + (drag.y >= 0f ? Vector2Int.up : Vector2Int.down);
                    }
                }

                TrySwap(pressCell, cell);
                hasPressCell = false;
            }
        }

        private bool TryRaycastCell(Vector2 screenPosition, out Vector2Int cell)
        {
            cell = default;
            if (inputCamera == null)
            {
                return false;
            }

            Ray ray = inputCamera.ScreenPointToRay(screenPosition);
            int count = Physics.RaycastNonAlloc(ray, raycastHits, 100f, pieceLayerMask, QueryTriggerInteraction.Ignore);
            float nearestDistance = float.MaxValue;
            EggPiece nearestPiece = null;
            for (int i = 0; i < count; i++)
            {
                if (raycastHits[i].distance >= nearestDistance)
                {
                    continue;
                }

                EggPiece candidate = FindPieceForCollider(raycastHits[i].collider);
                if (candidate != null)
                {
                    nearestDistance = raycastHits[i].distance;
                    nearestPiece = candidate;
                }
            }

            if (nearestPiece == null)
            {
                return false;
            }

            cell = nearestPiece.BoardPosition;
            return IsInside(cell.x, cell.y);
        }

        private EggPiece FindPieceForCollider(Collider hitCollider)
        {
            if (hitCollider == null)
            {
                return null;
            }
            Rigidbody attachedBody = hitCollider.attachedRigidbody;
            Transform hitTransform = hitCollider.transform;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    EggPiece piece = board[x, y];
                    if (piece == null)
                    {
                        continue;
                    }
                    if ((attachedBody != null && attachedBody == piece.Body) || hitTransform == piece.transform ||
                        hitTransform.IsChildOf(piece.transform))
                    {
                        return piece;
                    }
                }
            }
            return null;
        }

        private bool TrySwap(Vector2Int a, Vector2Int b)
        {
            if (!IsInside(a.x, a.y) || !IsInside(b.x, b.y) ||
                Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) != 1 ||
                lockTurns[a.x, a.y] > 0 || lockTurns[b.x, b.y] > 0)
            {
                return false;
            }

            SwapCells(a.x, a.y, b.x, b.y);
            if (!DetectMatches())
            {
                SwapCells(a.x, a.y, b.x, b.y);
                return false;
            }

            resolving = true;
            cascadeIndex = 0;
            do
            {
                ResolveMatches();
                ResolveDeferredAreaClear();
                ApplyGravityFall();
                cascadeIndex++;
            }
            while (cascadeIndex < 64 && DetectMatches());
            DecrementLocks();
            resolving = false;
            return true;
        }

        private bool DetectMatches()
        {
            matchScratchBuffer.Clear();
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    matchMarks[x, y] = false;
                }
            }

            for (int y = 0; y < rows; y++)
            {
                int runStart = 0;
                while (runStart < columns)
                {
                    EggPiece first = board[runStart, y];
                    int runEnd = runStart + 1;
                    while (first != null && runEnd < columns && SameArchetype(first, board[runEnd, y]))
                    {
                        runEnd++;
                    }
                    if (first != null && runEnd - runStart >= 3)
                    {
                        for (int x = runStart; x < runEnd; x++) matchMarks[x, y] = true;
                    }
                    runStart = runEnd;
                }
            }

            for (int x = 0; x < columns; x++)
            {
                int runStart = 0;
                while (runStart < rows)
                {
                    EggPiece first = board[x, runStart];
                    int runEnd = runStart + 1;
                    while (first != null && runEnd < rows && SameArchetype(first, board[x, runEnd]))
                    {
                        runEnd++;
                    }
                    if (first != null && runEnd - runStart >= 3)
                    {
                        for (int y = runStart; y < runEnd; y++) matchMarks[x, y] = true;
                    }
                    runStart = runEnd;
                }
            }

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    if (matchMarks[x, y] && board[x, y] != null)
                    {
                        matchScratchBuffer.Add(board[x, y]);
                    }
                }
            }
            return matchScratchBuffer.Count > 0;
        }

        private void ResolveMatches()
        {
            if (matchScratchBuffer.Count == 0)
            {
                return;
            }

            Vector3 origin = Vector3.zero;
            MatchShape shape = DetermineMatchShape();
            for (int i = 0; i < matchScratchBuffer.Count; i++)
            {
                origin += matchScratchBuffer[i].transform.position;
            }
            origin /= matchScratchBuffer.Count;

            culinaryReactionSystem?.EvaluateMatch(matchScratchBuffer, origin);
            bossBattleManager?.ReceiveMatchAttack(matchScratchBuffer.Count, shape, cascadeIndex);
            OnMatchResolved?.Invoke(matchScratchBuffer.Count, shape, cascadeIndex);

            int cascadeMultiplier = Mathf.Min(4, cascadeIndex + 1);
            float reactionMultiplier = scoreMultiplierMatches > 0 && Time.unscaledTime <= scoreMultiplierExpiry
                ? scoreMultiplier : 1f;
            Score += Mathf.RoundToInt(matchScratchBuffer.Count * 100 * cascadeMultiplier * reactionMultiplier);
            if (reactionMultiplier > 1f)
            {
                scoreMultiplierMatches--;
                if (scoreMultiplierMatches <= 0) scoreMultiplier = 1f;
            }
            for (int i = 0; i < matchScratchBuffer.Count; i++)
            {
                EggPiece piece = matchScratchBuffer[i];
                Vector2Int position = piece.BoardPosition;
                if (meshFractureEngine != null)
                {
                    meshFractureEngine.TriggerFracture(piece.Archetype, piece.transform.position,
                        Mathf.Clamp01(matchScratchBuffer.Count / 5f));
                }
                board[position.x, position.y] = null;
                piecePool.Return(piece);
            }
            matchScratchBuffer.Clear();
        }

        private void ApplyGravityFall()
        {
            bool cookedPiece;
            int safety = 0;
            do
            {
                float absX = Mathf.Abs(currentGravity.x);
                float absZ = Mathf.Abs(currentGravity.z);
                if (absX > absZ)
                {
                    SettleHorizontal(currentGravity.x < 0f);
                }
                else
                {
                    SettleVertical(currentGravity.z <= 0f);
                }
                cookedPiece = ResolveHazardOccupants();
                safety++;
            }
            while (cookedPiece && safety < columns * rows);
        }

        private bool ResolveHazardOccupants()
        {
            bool removedAny = false;
            float now = Time.unscaledTime;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    EggPiece piece = board[x, y];
                    if (piece == null || hazardExpiry[x, y] <= now)
                    {
                        continue;
                    }

                    hazardExpiry[x, y] = 0f;
                    board[x, y] = null;
                    if (meshFractureEngine != null)
                    {
                        meshFractureEngine.TriggerFracture(piece.Archetype, piece.transform.position, 0.5f);
                    }
                    piecePool.Return(piece);
                    Score += 250;
                    removedAny = true;
                }
            }
            return removedAny;
        }

        /// <summary>Randomizes the full board and applies optional physical impulses without allocating.</summary>
        public void ScrambleBoard(Vector3 explosionOrigin, float radius)
        {
            if (!initialized) return;
            int total = columns * rows;
            for (int i = total - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                SwapCells(i % columns, i / columns, j % columns, j / columns);
            }
            ResolveAnyPrematches();
            ApplyImpulseToAll(explosionOrigin, radius);
        }

        /// <summary>Randomly shuffles the requested percentage of occupied board cells.</summary>
        public void ApplyWhiskScramble(float percentage)
        {
            if (!initialized) return;
            int swaps = Mathf.Clamp(Mathf.RoundToInt(columns * rows * percentage * 0.5f), 1, columns * rows / 2);
            for (int i = 0; i < swaps; i++)
            {
                int a = random.Next(columns * rows);
                int b = random.Next(columns * rows);
                SwapCells(a % columns, a / columns, b % columns, b / columns);
            }
            ResolveAnyPrematches();
        }

        /// <summary>Locks random tiles for a fixed number of successful player turns.</summary>
        public void ApplyHotOilLock(int tileCount, int turnDuration)
        {
            if (!initialized) return;
            int remaining = Mathf.Clamp(tileCount, 0, columns * rows);
            int attempts = columns * rows * 4;
            while (remaining > 0 && attempts-- > 0)
            {
                int index = random.Next(columns * rows);
                int x = index % columns;
                int y = index / columns;
                if (columnShields[x])
                {
                    columnShields[x] = false;
                    continue;
                }
                if (lockTurns[x, y] == 0)
                {
                    lockTurns[x, y] = Mathf.Max(1, turnDuration);
                    remaining--;
                }
            }
        }

        /// <summary>Marks random tiles as hazards until an unscaled absolute expiry time.</summary>
        public void ApplyHazardTiles(int tileCount, float durationSeconds)
        {
            if (!initialized) return;
            int remaining = Mathf.Clamp(tileCount, 0, columns * rows);
            int attempts = columns * rows * 4;
            float expiry = Time.unscaledTime + Mathf.Max(0f, durationSeconds);
            while (remaining > 0 && attempts-- > 0)
            {
                int index = random.Next(columns * rows);
                int x = index % columns;
                int y = index / columns;
                if (columnShields[x])
                {
                    columnShields[x] = false;
                    continue;
                }
                if (hazardExpiry[x, y] <= Time.unscaledTime)
                {
                    hazardExpiry[x, y] = expiry;
                    remaining--;
                }
            }
        }

        /// <summary>Clears a square radius around a cell and resolves the resulting cascade.</summary>
        public void ClearArea(Vector2Int center, int radius)
        {
            if (!initialized) return;
            if (resolving)
            {
                hasDeferredAreaClear = true;
                deferredAreaCenter = center;
                deferredAreaRadius = Mathf.Max(deferredAreaRadius, Mathf.Max(0, radius));
                return;
            }

            resolving = true;
            matchScratchBuffer.Clear();
            for (int y = Mathf.Max(0, center.y - radius); y <= Mathf.Min(rows - 1, center.y + radius); y++)
            {
                for (int x = Mathf.Max(0, center.x - radius); x <= Mathf.Min(columns - 1, center.x + radius); x++)
                {
                    if (board[x, y] != null) matchScratchBuffer.Add(board[x, y]);
                }
            }
            ResolveMatches();
            ResolveDeferredAreaClear();
            ApplyGravityFall();
            ResolveAnyPrematches();
            resolving = false;
        }

        /// <summary>Protects a column from the next lock or hazard effect.</summary>
        public void AddColumnShield(int column)
        {
            if (!initialized || column < 0 || column >= columns) return;
            columnShields[column] = true;
        }

        /// <summary>Grants a temporary multiplier to a fixed number of subsequent match scores.</summary>
        public void GrantScoreMultiplier(int matchCount, float multiplier, float durationSeconds)
        {
            scoreMultiplierMatches = Mathf.Max(scoreMultiplierMatches, matchCount);
            scoreMultiplier = Mathf.Max(scoreMultiplier, multiplier);
            scoreMultiplierExpiry = Time.unscaledTime + Mathf.Max(0f, durationSeconds);
        }

        /// <summary>Locks the first available low-row tile for a fixed number of turns.</summary>
        public void LockLowestValueTile(int turnDuration)
        {
            if (!initialized) return;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    if (lockTurns[x, y] == 0)
                    {
                        lockTurns[x, y] = Mathf.Max(1, turnDuration);
                        return;
                    }
                }
            }
        }

        /// <summary>Runs an allocation-free adjacent match check without consuming a player move.</summary>
        public void RequestFreeCascade()
        {
            if (!initialized || resolving) return;
            ResolveAnyPrematches();
        }

        /// <summary>Returns the nearest valid board coordinate for a world-space point.</summary>
        public Vector2Int WorldToBoard(Vector3 worldPosition)
        {
            Vector3 local = gridAnchor != null ? gridAnchor.InverseTransformPoint(worldPosition) : worldPosition;
            int x = Mathf.Clamp(Mathf.RoundToInt(local.x / cellSpacing + (columns - 1) * 0.5f), 0, columns - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(local.z / cellSpacing + (rows - 1) * 0.5f), 0, rows - 1);
            return new Vector2Int(x, y);
        }

        private void SettleVertical(bool towardLowRows)
        {
            for (int x = 0; x < columns; x++)
            {
                int target = towardLowRows ? 0 : rows - 1;
                int step = towardLowRows ? 1 : -1;
                int end = towardLowRows ? rows : -1;
                for (int y = target; y != end; y += step)
                {
                    EggPiece piece = board[x, y];
                    if (piece == null) continue;
                    if (y != target)
                    {
                        board[x, target] = piece;
                        board[x, y] = null;
                        PlacePiece(piece, x, target);
                    }
                    target += step;
                }
                while (target != end)
                {
                    SpawnPieceAt(x, target, false);
                    target += step;
                }
            }
        }

        private void SettleHorizontal(bool towardLowColumns)
        {
            for (int y = 0; y < rows; y++)
            {
                int target = towardLowColumns ? 0 : columns - 1;
                int step = towardLowColumns ? 1 : -1;
                int end = towardLowColumns ? columns : -1;
                for (int x = target; x != end; x += step)
                {
                    EggPiece piece = board[x, y];
                    if (piece == null) continue;
                    if (x != target)
                    {
                        board[target, y] = piece;
                        board[x, y] = null;
                        PlacePiece(piece, target, y);
                    }
                    target += step;
                }
                while (target != end)
                {
                    SpawnPieceAt(target, y, false);
                    target += step;
                }
            }
        }

        private void SpawnPieceAt(int x, int y, bool avoidImmediateMatch)
        {
            EggPiece piece = piecePool.Get();
            if (piece == null)
            {
                Debug.LogError("[GridManager3D] EggPiece pool exhausted. Increase its pre-warmed capacity before gameplay.", this);
                return;
            }

            int kind = random.Next(3);
            if (avoidImmediateMatch)
            {
                for (int attempt = 0; attempt < 6 && WouldCreateInitialMatch(x, y, kind); attempt++)
                {
                    kind = (kind + 1) % 3;
                }
            }

            string archetype;
            ElementalTag tags;
            if (kind == 0) { archetype = "Hardboiled"; tags = ElementalTag.HardBoiled; }
            else if (kind == 1) { archetype = "Fried"; tags = ElementalTag.Fried; }
            else { archetype = "Scrambled"; tags = ElementalTag.Beaten; }
            if (currentWeather == WeatherState.Snow && random.NextDouble() < 0.1d)
            {
                tags |= ElementalTag.Frozen;
            }
            board[x, y] = piece;
            piece.transform.SetParent(gridAnchor, false);
            piece.OnSpawnFromPool(archetype, tags, new Vector2Int(x, y));
            PlacePiece(piece, x, y);
        }

        private bool WouldCreateInitialMatch(int x, int y, int kind)
        {
            string value = kind == 0 ? "Hardboiled" : kind == 1 ? "Fried" : "Scrambled";
            bool horizontal = x >= 2 && board[x - 1, y] != null && board[x - 2, y] != null &&
                board[x - 1, y].Archetype == value && board[x - 2, y].Archetype == value;
            bool vertical = y >= 2 && board[x, y - 1] != null && board[x, y - 2] != null &&
                board[x, y - 1].Archetype == value && board[x, y - 2].Archetype == value;
            return horizontal || vertical;
        }

        private void PlacePiece(EggPiece piece, int x, int y)
        {
            piece.BoardPosition = new Vector2Int(x, y);
            piece.transform.localPosition = new Vector3(
                (x - (columns - 1) * 0.5f) * cellSpacing,
                0f,
                (y - (rows - 1) * 0.5f) * cellSpacing);
        }

        private void SwapCells(int ax, int ay, int bx, int by)
        {
            EggPiece temp = board[ax, ay];
            board[ax, ay] = board[bx, by];
            board[bx, by] = temp;
            if (board[ax, ay] != null) PlacePiece(board[ax, ay], ax, ay);
            if (board[bx, by] != null) PlacePiece(board[bx, by], bx, by);
        }

        private void ResolveAnyPrematches()
        {
            bool wasResolving = resolving;
            resolving = true;
            cascadeIndex = 0;
            while (cascadeIndex < 64 && DetectMatches())
            {
                ResolveMatches();
                ResolveDeferredAreaClear();
                ApplyGravityFall();
                cascadeIndex++;
            }
            resolving = wasResolving;
        }

        private void ResolveDeferredAreaClear()
        {
            if (!hasDeferredAreaClear)
            {
                return;
            }

            hasDeferredAreaClear = false;
            matchScratchBuffer.Clear();
            int radius = deferredAreaRadius;
            deferredAreaRadius = 0;
            for (int y = Mathf.Max(0, deferredAreaCenter.y - radius);
                y <= Mathf.Min(rows - 1, deferredAreaCenter.y + radius); y++)
            {
                for (int x = Mathf.Max(0, deferredAreaCenter.x - radius);
                    x <= Mathf.Min(columns - 1, deferredAreaCenter.x + radius); x++)
                {
                    EggPiece piece = board[x, y];
                    if (piece != null)
                    {
                        matchScratchBuffer.Add(piece);
                    }
                }
            }

            for (int i = 0; i < matchScratchBuffer.Count; i++)
            {
                EggPiece piece = matchScratchBuffer[i];
                Vector2Int position = piece.BoardPosition;
                if (meshFractureEngine != null)
                {
                    meshFractureEngine.TriggerFracture(piece.Archetype, piece.transform.position, 0.65f);
                }
                board[position.x, position.y] = null;
                piecePool.Return(piece);
                Score += 100;
            }
            matchScratchBuffer.Clear();
        }

        private MatchShape DetermineMatchShape()
        {
            MatchShape detected = MatchShape.Line;
            for (int i = 0; i < matchScratchBuffer.Count; i++)
            {
                Vector2Int p = matchScratchBuffer[i].BoardPosition;
                int horizontalNeighbors = (IsMarked(p.x - 1, p.y) ? 1 : 0) +
                    (IsMarked(p.x + 1, p.y) ? 1 : 0);
                int verticalNeighbors = (IsMarked(p.x, p.y - 1) ? 1 : 0) +
                    (IsMarked(p.x, p.y + 1) ? 1 : 0);
                if (horizontalNeighbors > 0 && verticalNeighbors > 0)
                {
                    if (horizontalNeighbors == 2 || verticalNeighbors == 2)
                    {
                        return MatchShape.TShape;
                    }
                    detected = MatchShape.LShape;
                }
            }
            return detected;
        }

        private void ApplyImpulseToAll(Vector3 origin, float radius)
        {
            resolving = true;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    Rigidbody body = board[x, y] != null ? board[x, y].Body : null;
                    if (body != null)
                    {
                        body.isKinematic = false;
                        body.AddExplosionForce(4f, origin, Mathf.Max(0.1f, radius), 0.2f, ForceMode.Impulse);
                    }
                }
            }
            scrambleSettleAt = Time.unscaledTime + scramblePhysicsSeconds;
        }

        private void CompleteScramblePhysics()
        {
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    EggPiece piece = board[x, y];
                    if (piece == null)
                    {
                        continue;
                    }
                    Rigidbody body = piece.Body;
                    if (body != null)
                    {
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                        body.isKinematic = true;
                    }
                    PlacePiece(piece, x, y);
                }
            }
            scrambleSettleAt = -1f;
            resolving = false;
        }

        private void DecrementLocks()
        {
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                    if (lockTurns[x, y] > 0) lockTurns[x, y]--;
        }

        private void ExpireHazards()
        {
            float now = Time.unscaledTime;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                    if (hazardExpiry[x, y] > 0f && hazardExpiry[x, y] <= now) hazardExpiry[x, y] = 0f;
        }

        private void HandleGravityChanged(Vector3 gravity) { currentGravity = gravity; }
        private void HandleWeatherChanged(WeatherState weather) { currentWeather = weather; }
        private void HandleTemperatureThresholdCrossed(float celsius, ThresholdDirection direction)
        {
            if (direction == ThresholdDirection.Rising && celsius > 30f)
            {
                ApplyHazardTiles(1, 10f);
            }
        }
        private bool IsInside(int x, int y) { return x >= 0 && x < columns && y >= 0 && y < rows; }
        private bool IsMarked(int x, int y) { return IsInside(x, y) && matchMarks[x, y]; }
        private static bool SameArchetype(EggPiece a, EggPiece b) { return b != null && a.Archetype == b.Archetype; }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Bit flags describing culinary properties carried by a board piece.</summary>
    [Flags]
    public enum ElementalTag
    {
        /// <summary>No culinary property.</summary>
        None = 0,
        /// <summary>A heat-producing spicy property.</summary>
        Spicy = 1,
        /// <summary>A cold frozen property.</summary>
        Frozen = 2,
        /// <summary>A hard-boiled preparation property.</summary>
        HardBoiled = 4,
        /// <summary>A beaten or scrambled preparation property.</summary>
        Beaten = 8,
        /// <summary>A fried preparation property.</summary>
        Fried = 16,
        /// <summary>A pickled preparation property.</summary>
        Pickled = 32
    }

    /// <summary>Identifies the board effect executed by a culinary recipe.</summary>
    public enum ReactionType
    {
        /// <summary>Clears a three-by-three region.</summary>
        ThermalShock,
        /// <summary>Protects one board column from the next hazard.</summary>
        MeringueShield,
        /// <summary>Doubles the next two match scores.</summary>
        UmamiOverload,
        /// <summary>Requests a free cascade check.</summary>
        ScovilleCascade,
        /// <summary>Locks a low board tile for one turn.</summary>
        FlashFreeze
    }

    /// <summary>Immutable value passed to reaction and board-effect executors without allocation.</summary>
    public readonly struct MatchContext
    {
        /// <summary>Creates a match context value.</summary>
        public MatchContext(Vector3 originWorldPosition, Vector2Int boardCenter, int matchSize,
            ElementalTag aggregatedTags)
        {
            OriginWorldPosition = originWorldPosition;
            BoardCenter = boardCenter;
            MatchSize = matchSize;
            AggregatedTags = aggregatedTags;
        }

        /// <summary>Gets the world-space center of the resolved pieces.</summary>
        public readonly Vector3 OriginWorldPosition;
        /// <summary>Gets the nearest logical board coordinate to the match center.</summary>
        public readonly Vector2Int BoardCenter;
        /// <summary>Gets the number of pieces in the resolved group.</summary>
        public readonly int MatchSize;
        /// <summary>Gets the bitwise union of the resolved pieces' elemental tags.</summary>
        public readonly ElementalTag AggregatedTags;
    }

    /// <summary>Data-driven culinary combination and the board effect it selects.</summary>
    [CreateAssetMenu(menuName = "CMF3D/Reaction Recipe", fileName = "ReactionRecipe")]
    public sealed class ReactionRecipeSO : ScriptableObject
    {
        [SerializeField] private string recipeId;
        [SerializeField] private ElementalTag componentA;
        [SerializeField] private ElementalTag componentB;
        [SerializeField] private ReactionType reactionType;
        [SerializeField] private int priority;
        [SerializeField, Min(1)] private int minimumComponentCount = 1;

        /// <summary>Gets the stable save-data identifier for this recipe.</summary>
        public string RecipeId => recipeId;
        /// <summary>Gets the first required elemental component.</summary>
        public ElementalTag ComponentA => componentA;
        /// <summary>Gets the second required elemental component.</summary>
        public ElementalTag ComponentB => componentB;
        /// <summary>Gets the effect dispatched by the reaction executor.</summary>
        public ReactionType ReactionType => reactionType;
        /// <summary>Gets the descending resolution priority.</summary>
        public int Priority => priority;
        /// <summary>Gets the required occurrence count when both components are the same tag.</summary>
        public int MinimumComponentCount => minimumComponentCount;
    }

    /// <summary>Persistent first-discovery state for one culinary recipe.</summary>
    [Serializable]
    public sealed class RecipeBookEntry
    {
        /// <summary>Stable recipe identifier.</summary>
        public string RecipeId;
        /// <summary>First recipe component.</summary>
        public ElementalTag ComponentA;
        /// <summary>Second recipe component.</summary>
        public ElementalTag ComponentB;
        /// <summary>Whether the player has discovered the recipe.</summary>
        public bool Discovered;
        /// <summary>UTC time of first discovery; persistence adapters must preserve this value.</summary>
        public DateTime FirstDiscoveredUtc;
        /// <summary>Localization key for the recipe's flavor text.</summary>
        public string FlavorTextKey;
    }

    /// <summary>Fixed-capacity recipe discovery data prepared before match evaluation begins.</summary>
    [Serializable]
    public sealed class RecipeBookSaveData
    {
        [SerializeField] private RecipeBookEntry[] entries = new RecipeBookEntry[0];

        /// <summary>Gets the authored recipe slots without creating a copy.</summary>
        public RecipeBookEntry[] Entries => entries;

        internal void Prepare(ReactionRecipeSO[] recipes)
        {
            int required = recipes == null ? 0 : recipes.Length;
            if (entries == null || entries.Length < required)
            {
                RecipeBookEntry[] resized = new RecipeBookEntry[required];
                if (entries != null)
                {
                    Array.Copy(entries, resized, entries.Length);
                }
                entries = resized;
            }

            for (int recipeIndex = 0; recipeIndex < required; recipeIndex++)
            {
                ReactionRecipeSO recipe = recipes[recipeIndex];
                if (recipe == null || string.IsNullOrEmpty(recipe.RecipeId))
                {
                    continue;
                }

                int existing = Find(recipe.RecipeId);
                if (existing >= 0)
                {
                    continue;
                }

                for (int slot = 0; slot < entries.Length; slot++)
                {
                    if (entries[slot] != null)
                    {
                        continue;
                    }

                    entries[slot] = new RecipeBookEntry
                    {
                        RecipeId = recipe.RecipeId,
                        ComponentA = recipe.ComponentA,
                        ComponentB = recipe.ComponentB
                    };
                    break;
                }
            }
        }

        internal bool Record(string recipeId)
        {
            int index = Find(recipeId);
            if (index < 0 || entries[index].Discovered)
            {
                return false;
            }

            entries[index].Discovered = true;
            entries[index].FirstDiscoveredUtc = DateTime.UtcNow;
            return true;
        }

        private int Find(string recipeId)
        {
            if (entries == null)
            {
                return -1;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                RecipeBookEntry entry = entries[i];
                if (entry != null && string.Equals(entry.RecipeId, recipeId, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>Resolves priority-ordered culinary recipes for completed board matches.</summary>
    public sealed class CulinaryReactionSystem : MonoBehaviour
    {
        [SerializeField] private ReactionRecipeSO[] recipeDatabase;
        [SerializeField] private RecipeBookSaveData recipeBook = new RecipeBookSaveData();
        [SerializeField] private GridManager3D gridManager;

        /// <summary>Raised synchronously after a recipe effect and first-discovery update.</summary>
        public event Action<ReactionRecipeSO, Vector3> OnReactionTriggered;

        private void Awake()
        {
            if (gridManager == null)
            {
                gridManager = GetComponent<GridManager3D>();
            }

            SortRecipesByPriority();
            if (recipeBook == null)
            {
                recipeBook = new RecipeBookSaveData();
            }
            recipeBook.Prepare(recipeDatabase);
        }

        /// <summary>Aggregates tags without allocation and executes the highest-priority matching recipe.</summary>
        public void EvaluateMatch(IReadOnlyList<EggPiece> matchedPieces, Vector3 originWorldPos)
        {
            if (matchedPieces == null || matchedPieces.Count == 0 || recipeDatabase == null)
            {
                return;
            }

            ElementalTag aggregate = ElementalTag.None;
            for (int i = 0; i < matchedPieces.Count; i++)
            {
                EggPiece piece = matchedPieces[i];
                if (piece != null)
                {
                    aggregate |= piece.Tags;
                }
            }

            for (int i = 0; i < recipeDatabase.Length; i++)
            {
                ReactionRecipeSO recipe = recipeDatabase[i];
                if (recipe == null || !HasTag(aggregate, recipe.ComponentA) || !HasTag(aggregate, recipe.ComponentB))
                {
                    continue;
                }

                if (recipe.ComponentA == recipe.ComponentB &&
                    CountPiecesWithTag(matchedPieces, recipe.ComponentA) < Mathf.Max(1, recipe.MinimumComponentCount))
                {
                    continue;
                }

                Vector2Int center = gridManager != null ? gridManager.WorldToBoard(originWorldPos) : default;
                MatchContext context = new MatchContext(originWorldPos, center, matchedPieces.Count, aggregate);
                ReactionExecutor.Execute(recipe, context, gridManager);
                RecordDiscovery(recipe.RecipeId);
                OnReactionTriggered?.Invoke(recipe, originWorldPos);
                return;
            }
        }

        private void RecordDiscovery(string recipeId)
        {
            if (recipeBook != null && !string.IsNullOrEmpty(recipeId))
            {
                recipeBook.Record(recipeId);
            }
        }

        private void SortRecipesByPriority()
        {
            if (recipeDatabase == null)
            {
                return;
            }

            for (int i = 1; i < recipeDatabase.Length; i++)
            {
                ReactionRecipeSO current = recipeDatabase[i];
                int currentPriority = current == null ? int.MinValue : current.Priority;
                int j = i - 1;
                while (j >= 0)
                {
                    ReactionRecipeSO previous = recipeDatabase[j];
                    int previousPriority = previous == null ? int.MinValue : previous.Priority;
                    if (previousPriority >= currentPriority)
                    {
                        break;
                    }
                    recipeDatabase[j + 1] = previous;
                    j--;
                }
                recipeDatabase[j + 1] = current;
            }
        }

        private static int CountPiecesWithTag(IReadOnlyList<EggPiece> pieces, ElementalTag tag)
        {
            int count = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                EggPiece piece = pieces[i];
                if (piece != null && HasTag(piece.Tags, tag))
                {
                    count++;
                }
            }
            return count;
        }

        private static bool HasTag(ElementalTag value, ElementalTag tag)
        {
            return tag != ElementalTag.None && (value & tag) == tag;
        }
    }

    /// <summary>Stateless dispatcher for data-authored culinary board effects.</summary>
    public static class ReactionExecutor
    {
        /// <summary>Executes one recipe against the supplied board without retaining mutable state.</summary>
        public static void Execute(ReactionRecipeSO recipe, MatchContext context, GridManager3D grid)
        {
            if (recipe == null || grid == null)
            {
                return;
            }

            switch (recipe.ReactionType)
            {
                case ReactionType.ThermalShock:
                    grid.ClearArea(context.BoardCenter, 1);
                    break;
                case ReactionType.MeringueShield:
                    grid.AddColumnShield(context.BoardCenter.x);
                    break;
                case ReactionType.UmamiOverload:
                    grid.GrantScoreMultiplier(2, 2f, 6f);
                    break;
                case ReactionType.ScovilleCascade:
                    grid.RequestFreeCascade();
                    break;
                case ReactionType.FlashFreeze:
                    grid.LockLowestValueTile(1);
                    break;
            }
        }
    }
}

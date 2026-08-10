using System;
using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>One tagged localized dialogue value.</summary>
    [Serializable]
    public sealed class DialogueLine
    {
        [SerializeField] private string tag;
        [SerializeField, TextArea] private string text;

        /// <summary>Gets the lookup tag used by runtime systems.</summary>
        public string Tag => tag;
        /// <summary>Gets the localized or fallback display text.</summary>
        public string Text => text;
    }

    /// <summary>Allocation-free tagged dialogue lookup over an authored fixed array.</summary>
    [CreateAssetMenu(menuName = "CMF3D/Dialogue Database", fileName = "DialogueDatabase")]
    public sealed class DialogueDatabase : ScriptableObject
    {
        [SerializeField] private DialogueLine[] lines;
        private int nextIndex;

        /// <summary>Finds the next line with the requested tag without allocating a collection.</summary>
        public bool TryGetLine(string tag, out string line)
        {
            line = null;
            if (lines == null || lines.Length == 0 || string.IsNullOrEmpty(tag))
            {
                return false;
            }

            for (int offset = 0; offset < lines.Length; offset++)
            {
                int index = (nextIndex + offset) % lines.Length;
                DialogueLine candidate = lines[index];
                if (candidate != null && string.Equals(candidate.Tag, tag, StringComparison.Ordinal))
                {
                    line = candidate.Text;
                    nextIndex = (index + 1) % lines.Length;
                    return !string.IsNullOrEmpty(line);
                }
            }
            return false;
        }
    }

    /// <summary>Contract implemented by the hint or booster system used for paired stress relief.</summary>
    public interface IPlayerAssistSystem
    {
        /// <summary>Offers a non-punitive breather hint and returns whether the offer was accepted for display.</summary>
        bool TryOfferBreatherHint();
    }
}

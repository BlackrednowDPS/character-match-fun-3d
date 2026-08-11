using System;
using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Identifies the fixed character socket used by a cosmetic.</summary>
    public enum CosmeticSocketType
    {
        /// <summary>Uses the Socket_Eyewear transform.</summary>
        Eyewear,
        /// <summary>Uses the Socket_Head transform.</summary>
        Head
    }

    /// <summary>Authored cosmetic identifier, socket, and prefab reference.</summary>
    [CreateAssetMenu(menuName = "CMF3D/Cosmetic Definition", fileName = "CosmeticDefinition")]
    public sealed class CosmeticDefinitionSO : ScriptableObject
    {
        [SerializeField] private string cosmeticId;
        [SerializeField] private CosmeticSocketType socketType;
        [SerializeField] private GameObject prefab;

        /// <summary>Gets the stable inventory identifier.</summary>
        public string CosmeticId => cosmeticId;
        /// <summary>Gets the character socket category.</summary>
        public CosmeticSocketType SocketType => socketType;
        /// <summary>Gets the prefab instantiated once during assembler pre-warming.</summary>
        public GameObject Prefab => prefab;
    }

    /// <summary>Prewarms authored cosmetics and reparents them to cached FBX socket transforms without runtime destruction.</summary>
    public sealed class CosmeticSocketAssembler : MonoBehaviour
    {
        [SerializeField] private Transform characterRoot;
        [SerializeField] private CosmeticDefinitionSO[] availableCosmetics;

        private Transform eyewearSocket;
        private Transform headSocket;
        private GameObject[] instances;
        private int equippedEyewearIndex = -1;
        private int equippedHeadIndex = -1;

        private void Awake()
        {
            if (characterRoot == null)
            {
                characterRoot = transform;
            }
            CacheSockets();
            PrewarmCosmetics();
        }

        /// <summary>Equips a prewarmed cosmetic by stable identifier and returns false when configuration is missing.</summary>
        public bool TryEquip(string cosmeticId)
        {
            if (instances == null || availableCosmetics == null || string.IsNullOrEmpty(cosmeticId))
            {
                return false;
            }
            for (int i = 0; i < availableCosmetics.Length; i++)
            {
                CosmeticDefinitionSO definition = availableCosmetics[i];
                if (definition != null && string.Equals(definition.CosmeticId, cosmeticId,
                    StringComparison.Ordinal))
                {
                    return EquipIndex(i, definition);
                }
            }
            return false;
        }

        /// <summary>Returns the equipped cosmetic for one socket to its inactive prewarmed state.</summary>
        public void Unequip(CosmeticSocketType socketType)
        {
            if (instances == null)
            {
                return;
            }
            int index = socketType == CosmeticSocketType.Eyewear ? equippedEyewearIndex : equippedHeadIndex;
            if (index >= 0 && index < instances.Length && instances[index] != null)
            {
                instances[index].SetActive(false);
                instances[index].transform.SetParent(transform, false);
            }
            if (socketType == CosmeticSocketType.Eyewear)
            {
                equippedEyewearIndex = -1;
            }
            else
            {
                equippedHeadIndex = -1;
            }
        }

        private void CacheSockets()
        {
            Transform[] transforms = characterRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate.name == "Socket_Eyewear")
                {
                    eyewearSocket = candidate;
                }
                else if (candidate.name == "Socket_Head")
                {
                    headSocket = candidate;
                }
            }
            if (eyewearSocket == null || headSocket == null)
            {
                Debug.LogError("[CosmeticSocketAssembler] Character requires child transforms named 'Socket_Eyewear' and 'Socket_Head'.", this);
            }
        }

        private void PrewarmCosmetics()
        {
            int count = availableCosmetics == null ? 0 : availableCosmetics.Length;
            instances = new GameObject[count];
            for (int i = 0; i < count; i++)
            {
                CosmeticDefinitionSO definition = availableCosmetics[i];
                if (definition == null || definition.Prefab == null)
                {
                    continue;
                }
                GameObject instance = Instantiate(definition.Prefab, transform);
                instance.name = definition.Prefab.name;
                instance.SetActive(false);
                instances[i] = instance;
            }
        }

        private bool EquipIndex(int index, CosmeticDefinitionSO definition)
        {
            Transform socket = definition.SocketType == CosmeticSocketType.Eyewear ? eyewearSocket : headSocket;
            GameObject instance = instances[index];
            if (socket == null || instance == null)
            {
                return false;
            }

            Unequip(definition.SocketType);
            Transform instanceTransform = instance.transform;
            instanceTransform.SetParent(socket, false);
            instanceTransform.localPosition = Vector3.zero;
            instanceTransform.localRotation = Quaternion.identity;
            instanceTransform.localScale = Vector3.one;
            instance.SetActive(true);
            if (definition.SocketType == CosmeticSocketType.Eyewear)
            {
                equippedEyewearIndex = index;
            }
            else
            {
                equippedHeadIndex = index;
            }
            return true;
        }
    }
}

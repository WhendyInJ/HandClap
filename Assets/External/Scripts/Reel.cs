using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[DefaultExecutionOrder(-50)]
public class Reel : MonoBehaviour
{
    const string GeneratedRootName = "_GeneratedReelItems";
    const string LogicalItemPrefix = "_ReelItem_";
    const string RuntimeClonePrefix = "_RuntimeBundle_";

    public const float DefaultSlotSpacing = 200f;
    public const float DefaultTopSlotY = 200f;

    public enum SlotCategory
    {
        Element,
        Body,
        Hand
    }

    [Serializable]
    public class SlotChoice
    {
        public SlotCategory category;
        public Element elementType;
        public BodyType bodyType;
        public HandSize handType;
    }

    [Serializable]
    public class ReelItemDefinition
    {
        public Sprite sprite;
        public SlotChoice slot = new SlotChoice();
    }

    [Serializable]
    class RuntimeCell
    {
        public RectTransform rect;
        public int logicalIndex;
        public int bundleOffset;
    }

    [Header("References")]
    public RectTransform content;

    [Header("Items")]
    [SerializeField] List<ReelItemDefinition> itemDefinitions = new List<ReelItemDefinition>();
    [SerializeField] Vector2 itemSize = new Vector2(200f, 200f);
    [SerializeField] bool preserveAspect = true;

    [Header("Layout")]
    [SerializeField] float topSlotY = DefaultTopSlotY;
    [SerializeField] float slotSpacing = DefaultSlotSpacing;

    [Header("Spin")]
    [SerializeField] float spinSpeedPxPerSec = 2600f;
    [SerializeField] float stopMinExtraScroll = 600f;
    [SerializeField, Min(1f)] float stopSpeedMultiplier = 1.3f;
    [SerializeField] float stopDeceleration = 3200f;
    [SerializeField] float finalApproachSpeed = 300f;

    public int itemCount;

    readonly List<RectTransform> logicalItems = new List<RectTransform>();
    readonly List<RuntimeCell> runtimeCells = new List<RuntimeCell>();
    readonly List<int> matchingIndices = new List<int>();

    Coroutine stopRoutine;
    RectTransform generatedRoot;
    bool runtimeStripBuilt;
    bool spinning;

#if UNITY_EDITOR
    bool editorRebuildQueued;
#endif

    float CycleHeight => itemCount * slotSpacing;
    float TotalStripHeight => CycleHeight * 3f;
    float WrapMinY => SlotYForIndex(itemCount - 1) - CycleHeight - slotSpacing;

    static readonly HideFlags GeneratedHideFlags =
        HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;

#if UNITY_EDITOR
    bool CanModifyReelHierarchyInEditor()
    {
        if (Application.isPlaying)
            return true;

        if (!PrefabUtility.IsPartOfPrefabAsset(gameObject))
            return true;

        return PrefabStageUtility.GetPrefabStage(gameObject) != null;
    }
#endif

    void Awake()
    {
        if (Application.isPlaying)
            BuildRuntimeStrip();
        else
            RebuildEditorPreview();
    }

    void OnEnable()
    {
        if (Application.isPlaying)
            BuildRuntimeStrip();
        else
            RebuildEditorPreview();
    }

    void OnDisable()
    {
        spinning = false;
        runtimeStripBuilt = false;
        runtimeCells.Clear();

#if UNITY_EDITOR
        EditorApplication.delayCall -= RunQueuedEditorRebuild;
        editorRebuildQueued = false;
#endif
    }

    void OnValidate()
    {
        slotSpacing = Mathf.Max(1f, slotSpacing);
        itemSize.x = Mathf.Max(1f, itemSize.x);
        itemSize.y = Mathf.Max(1f, itemSize.y);
        finalApproachSpeed = Mathf.Max(1f, finalApproachSpeed);
        stopSpeedMultiplier = Mathf.Max(1f, stopSpeedMultiplier);
        SyncItemCount();

#if UNITY_EDITOR
        if (!Application.isPlaying && CanModifyReelHierarchyInEditor())
            QueueEditorRebuildAfterValidate();
#endif
    }

#if UNITY_EDITOR
    void QueueEditorRebuildAfterValidate()
    {
        if (editorRebuildQueued)
            return;

        editorRebuildQueued = true;
        EditorApplication.delayCall += RunQueuedEditorRebuild;
    }

    void RunQueuedEditorRebuild()
    {
        EditorApplication.delayCall -= RunQueuedEditorRebuild;
        editorRebuildQueued = false;

        if (this == null || gameObject == null)
            return;

        if (Application.isPlaying)
            return;

        if (!CanModifyReelHierarchyInEditor())
            return;

        RebuildEditorPreview();
    }
#endif

    [ContextMenu("Layout Items Now")]
    public void LayoutItemsNow()
    {
        if (Application.isPlaying)
            BuildRuntimeStrip();
        else
            RebuildEditorPreview();
    }

    void Update()
    {
        if (!spinning || runtimeCells.Count == 0)
            return;

        MoveStrip(spinSpeedPxPerSec * Time.deltaTime);
    }

    public void StartSpin()
    {
        EnsureRuntimeStrip();

        if (itemCount == 0)
            return;

        if (stopRoutine != null)
        {
            StopCoroutine(stopRoutine);
            stopRoutine = null;
        }

        spinning = true;
    }

    public void Stop(int index)
    {
        EnsureRuntimeStrip();

        if (stopRoutine != null)
            StopCoroutine(stopRoutine);

        stopRoutine = StartCoroutine(StopRoutine(index));
    }

    public IEnumerator CoStop(int index)
    {
        EnsureRuntimeStrip();

        if (stopRoutine != null)
            StopCoroutine(stopRoutine);

        stopRoutine = StartCoroutine(StopRoutine(index));
        yield return stopRoutine;
    }

    public int GetRandomIndexForSlotType(Element slotType)
    {
        return GetRandomIndexForSlotValue((int)slotType);
    }

    public int GetRandomIndexForSlotType(BodyType slotType)
    {
        return GetRandomIndexForSlotValue((int)slotType);
    }

    public int GetRandomIndexForSlotType(HandSize slotType)
    {
        return GetRandomIndexForSlotValue((int)slotType);
    }

    public int GetSlotTypeValueAt(int index)
    {
        SyncItemCount();

        if (itemCount == 0)
            return 0;

        return GetSlotTypeValue(itemDefinitions[NormalizeIndex(index)].slot);
    }

    int GetRandomIndexForSlotValue(int slotTypeValue)
    {
        SyncItemCount();

        if (itemCount == 0)
            return 0;

        matchingIndices.Clear();

        for (int i = 0; i < itemDefinitions.Count; i++)
        {
            if (GetSlotTypeValue(itemDefinitions[i].slot) == slotTypeValue)
                matchingIndices.Add(i);
        }

        if (matchingIndices.Count == 0)
            return UnityEngine.Random.Range(0, itemCount);

        return matchingIndices[UnityEngine.Random.Range(0, matchingIndices.Count)];
    }

    IEnumerator StopRoutine(int index)
    {
        EnsureRuntimeStrip();

        if (itemCount == 0 || runtimeCells.Count == 0)
        {
            stopRoutine = null;
            yield break;
        }

        spinning = false;

        int normalizedIndex = NormalizeIndex(index);
        float remainingDistance = ComputeStopDistance(normalizedIndex);
        float speed = spinSpeedPxPerSec * stopSpeedMultiplier;
        float minApproachSpeed = finalApproachSpeed * stopSpeedMultiplier;
        float decelerationPerSecond = stopDeceleration * stopSpeedMultiplier;

        while (remainingDistance > 0.01f)
        {
            speed = Mathf.Max(minApproachSpeed, speed - decelerationPerSecond * Time.deltaTime);

            float moveDistance = Mathf.Min(remainingDistance, speed * Time.deltaTime);
            MoveStrip(moveDistance);
            remainingDistance -= moveDistance;

            yield return null;
        }

        SnapToStoppedIndex(normalizedIndex);

        stopRoutine = null;
    }

    void EnsureRuntimeStrip()
    {
        if (runtimeStripBuilt && runtimeCells.Count > 0)
            return;

        BuildRuntimeStrip();
    }

    void BuildRuntimeStrip()
    {
        SyncItemCount();
        EnsureGeneratedRoot();
        RebuildLogicalItems();

        runtimeCells.Clear();
        runtimeStripBuilt = false;

        if (content == null || generatedRoot == null || logicalItems.Count == 0)
            return;

        for (int i = 0; i < logicalItems.Count; i++)
            runtimeCells.Add(new RuntimeCell { rect = logicalItems[i], logicalIndex = i, bundleOffset = 0 });

        CreateBundleCopies(1);
        CreateBundleCopies(-1);

        LayoutRuntimeBundles();
        runtimeStripBuilt = true;
    }

    void RebuildEditorPreview()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && !CanModifyReelHierarchyInEditor())
            return;
#endif
        SyncItemCount();
        EnsureGeneratedRoot();
        RebuildLogicalItems();
        runtimeCells.Clear();
        runtimeStripBuilt = false;
    }

    void RebuildLogicalItems()
    {
        logicalItems.Clear();

        if (content == null || generatedRoot == null)
            return;

        ClearGeneratedChildren();

        for (int i = 0; i < itemDefinitions.Count; i++)
        {
            RectTransform rect = CreateLogicalItem(i, itemDefinitions[i]);
            logicalItems.Add(rect);
        }

        LayoutLogicalItems();
    }

    RectTransform CreateLogicalItem(int index, ReelItemDefinition definition)
    {
        GameObject go = new GameObject($"{LogicalItemPrefix}{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.hideFlags = GeneratedHideFlags;
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(generatedRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = itemSize;

        Image image = go.GetComponent<Image>();
        image.sprite = definition.sprite;
        image.preserveAspect = preserveAspect;
        image.raycastTarget = false;
        image.color = Color.white;

        return rect;
    }

    void ClearGeneratedChildren()
    {
        if (generatedRoot == null)
            return;

        for (int i = generatedRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = generatedRoot.GetChild(i).gameObject;
            if (child == null)
                continue;

            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }
    }

    void EnsureGeneratedRoot()
    {
        generatedRoot = null;

        if (content == null)
            return;

        Transform found = content.Find(GeneratedRootName);
        if (found != null)
        {
            generatedRoot = found as RectTransform;
            return;
        }

        GameObject root = new GameObject(GeneratedRootName, typeof(RectTransform));
        root.hideFlags = GeneratedHideFlags;
        generatedRoot = (RectTransform)root.transform;
        generatedRoot.SetParent(content, false);
        generatedRoot.anchorMin = generatedRoot.anchorMax = generatedRoot.pivot = new Vector2(0.5f, 0.5f);
        generatedRoot.anchoredPosition = Vector2.zero;
        generatedRoot.sizeDelta = Vector2.zero;
    }

    void CreateBundleCopies(int bundleOffset)
    {
        for (int i = 0; i < logicalItems.Count; i++)
        {
            RectTransform source = logicalItems[i];
            GameObject clone = Instantiate(source.gameObject, generatedRoot);
            clone.name = RuntimeClonePrefix + (bundleOffset > 0 ? "Above_" : "Below_") + i;
            clone.hideFlags = GeneratedHideFlags;

            RectTransform rect = (RectTransform)clone.transform;
            runtimeCells.Add(new RuntimeCell { rect = rect, logicalIndex = i, bundleOffset = bundleOffset });
        }
    }

    void LayoutLogicalItems()
    {
        for (int i = 0; i < logicalItems.Count; i++)
        {
            RectTransform rect = logicalItems[i];
            Vector2 anchoredPosition = rect.anchoredPosition;
            anchoredPosition.y = SlotYForIndex(i);
            rect.anchoredPosition = anchoredPosition;
        }
    }

    void LayoutRuntimeBundles()
    {
        float cycleHeight = CycleHeight;

        for (int i = 0; i < runtimeCells.Count; i++)
        {
            RuntimeCell cell = runtimeCells[i];
            Vector2 anchoredPosition = cell.rect.anchoredPosition;
            anchoredPosition.y = cell.bundleOffset * cycleHeight + SlotYForIndex(cell.logicalIndex);
            cell.rect.anchoredPosition = anchoredPosition;
        }
    }

    float SlotYForIndex(int index)
    {
        return topSlotY - index * slotSpacing;
    }

    void MoveStrip(float distance)
    {
        for (int i = 0; i < runtimeCells.Count; i++)
        {
            RectTransform rect = runtimeCells[i].rect;
            rect.anchoredPosition -= new Vector2(0f, distance);
        }

        WrapRuntimeCells();
    }

    void WrapRuntimeCells()
    {
        if (runtimeCells.Count == 0 || itemCount == 0)
            return;

        float wrapDistance = TotalStripHeight;
        float wrapMinY = WrapMinY;

        for (int i = 0; i < runtimeCells.Count; i++)
        {
            RectTransform rect = runtimeCells[i].rect;
            Vector2 anchoredPosition = rect.anchoredPosition;

            while (anchoredPosition.y < wrapMinY)
                anchoredPosition.y += wrapDistance;

            rect.anchoredPosition = anchoredPosition;
        }
    }

    float ComputeStopDistance(int logicalIndex)
    {
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < runtimeCells.Count; i++)
        {
            RuntimeCell cell = runtimeCells[i];
            if (cell.logicalIndex != logicalIndex)
                continue;

            float candidate = cell.rect.anchoredPosition.y;
            while (candidate <= 0f)
                candidate += TotalStripHeight;

            while (candidate < stopMinExtraScroll)
                candidate += TotalStripHeight;

            if (candidate < bestDistance)
                bestDistance = candidate;
        }

        return float.IsPositiveInfinity(bestDistance) ? 0f : bestDistance;
    }

    void SnapToStoppedIndex(int logicalIndex)
    {
        float cycleHeight = CycleHeight;

        for (int i = 0; i < runtimeCells.Count; i++)
        {
            RuntimeCell cell = runtimeCells[i];
            Vector2 anchoredPosition = cell.rect.anchoredPosition;
            anchoredPosition.y = cell.bundleOffset * cycleHeight - GetSignedOffsetFromCenter(logicalIndex, cell.logicalIndex) * slotSpacing;
            cell.rect.anchoredPosition = anchoredPosition;
        }
    }

    int GetSignedOffsetFromCenter(int centerIndex, int itemIndex)
    {
        int offset = itemIndex - centerIndex;
        int half = itemCount / 2;

        while (offset > half)
            offset -= itemCount;

        while (offset < -half)
            offset += itemCount;

        if (itemCount % 2 == 0 && offset == half)
            offset -= itemCount;

        return offset;
    }

    int NormalizeIndex(int index)
    {
        if (itemCount == 0)
            return 0;

        int normalized = index % itemCount;

        if (normalized < 0)
            normalized += itemCount;

        return normalized;
    }

    void SyncItemCount()
    {
        itemCount = itemDefinitions.Count;
    }

    int GetSlotTypeValue(SlotChoice slotChoice)
    {
        switch (slotChoice.category)
        {
            case SlotCategory.Body:
                return (int)slotChoice.bodyType;
            case SlotCategory.Hand:
                return (int)slotChoice.handType;
            default:
                return (int)slotChoice.elementType;
        }
    }

}

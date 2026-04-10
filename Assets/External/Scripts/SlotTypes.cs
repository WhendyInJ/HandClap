using System;
using UnityEngine;

/// <summary>
/// 릴 축 enum. <see cref="SlotReelLayout.VariantCount"/>와 개수가 맞아야 함 (늘리면 const도 같이 올림).
/// </summary>
public enum Element
{
    Earth,
    Water,
    Electric
}

public enum BodyType
{
    Heavy,
    Normal,
    Light
}

public enum HandSize
{
    Big,
    Medium,
    Small
}

[Serializable]
public struct PlayerBuild
{
    public Element Element;
    public BodyType BodyType;
    public HandSize HandSize;
}

/// <summary>
/// 한 릴: 위→아래로 (0,1,…,N-1 서로 다른 심볼) × N번 반복 = N²칸.
/// 결과 종류 k는 슬롯 [k×N .. k×N+N-1] 중 하나에 멈춤.
/// </summary>
public static class SlotReelLayout
{
    /// <summary>Earth/Water/Electric 등 한 축의 값 개수. 바꾸면 enum·인스펙터 스프라이트 개수도 맞출 것.</summary>
    public const int VariantCount = 3;

    public const int ItemsPerReel = VariantCount * VariantCount;

    public static int RandomSlotIndex(int variantIndex)
    {
        return variantIndex * VariantCount + UnityEngine.Random.Range(0, VariantCount);
    }

    public static int RandomSlotFor(Element e) => RandomSlotIndex((int)e);
    public static int RandomSlotFor(BodyType b) => RandomSlotIndex((int)b);
    public static int RandomSlotFor(HandSize h) => RandomSlotIndex((int)h);

    public static Element ElementAtSlot(int slot) => (Element)(slot / VariantCount);
    public static BodyType BodyAtSlot(int slot) => (BodyType)(slot / VariantCount);
    public static HandSize HandAtSlot(int slot) => (HandSize)(slot / VariantCount);
}

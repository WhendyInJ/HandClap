using UnityEngine;
using UnityEngine.Serialization;

public enum SpriteFillColorMode
{
    Attack,
    FakeAttack,
}

[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFillController : MonoBehaviour
{
    static readonly int FillId = Shader.PropertyToID("_Fill");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [Range(0,1)] public float fill = 1f;
    [FormerlySerializedAs("color")] [SerializeField] private Color attackColor = Color.white;
    [SerializeField] private Color fakeAttackColor = new Color(1f, 0.35f, 0.1f, 1f);
    [SerializeField] private SpriteFillColorMode colorMode = SpriteFillColorMode.Attack;

    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock propertyBlock;

    void Awake()
    {
        CacheComponents();
        Apply();
    }

    void OnValidate()
    {
        fill = Mathf.Clamp01(fill);
        CacheComponents();
        Apply();
    }

    void Update()
    {
        Apply();
    }

    public void SetFill(float value)
    {
        fill = Mathf.Clamp01(value);
        Apply();
    }

    public void SetColor(Color value)
    {
        attackColor = value;
        colorMode = SpriteFillColorMode.Attack;
        Apply();
    }

    public void SetColorMode(SpriteFillColorMode mode)
    {
        colorMode = mode;
        Apply();
    }

    void CacheComponents()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
    }

    void Apply()
    {
        if (spriteRenderer == null)
            return;

        spriteRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(FillId, fill);
        propertyBlock.SetColor(ColorId, GetCurrentColor());
        spriteRenderer.SetPropertyBlock(propertyBlock);
    }

    Color GetCurrentColor()
    {
        return colorMode == SpriteFillColorMode.FakeAttack
            ? fakeAttackColor
            : attackColor;
    }
}

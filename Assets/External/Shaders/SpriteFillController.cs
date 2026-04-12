using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFillController : MonoBehaviour
{
    static readonly int FillId = Shader.PropertyToID("_Fill");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [Range(0,1)] public float fill = 1f;
    public Color color = Color.white;

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
        color = value;
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
        propertyBlock.SetColor(ColorId, color);
        spriteRenderer.SetPropertyBlock(propertyBlock);
    }
}

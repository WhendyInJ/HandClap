using UnityEngine;

public class SpriteFillController : MonoBehaviour
{
    public Material mat;
    [Range(0,1)] public float fill = 1f;

    void Update()
    {
        mat.SetFloat("_Fill", fill);
    }
}
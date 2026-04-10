using UnityEngine;

/// <summary>
/// 실제 플레이어 이벤트가 아직 연결되지 않은 상태에서
/// 균형 미니게임을 빠르게 테스트하기 위한 디버그 입력 스크립트.
///
/// 기본 동작:
/// - B 키: 미니게임 시작
/// - H 키: 피격 1회 시뮬레이션
///
/// 나중에 실제 플레이어 휘청 이벤트가 연결되면
/// 이 스크립트는 제거하거나 비활성화하면 된다.
/// </summary>
public class HandClap_BalanceMinigameDebugInput : MonoBehaviour
{
    [Header("컨트롤러 참조")]

    /// <summary>
    /// 테스트할 균형 미니게임 컨트롤러 참조.
    /// </summary>
    [SerializeField] private HandClap_BalanceMinigameController controller = null;

    [Header("디버그 키 설정")]

    /// <summary>
    /// 미니게임 시작용 키.
    /// </summary>
    [SerializeField] private KeyCode startKey = KeyCode.B;

    /// <summary>
    /// 피격 시뮬레이션용 키.
    /// 안전 구간을 강제로 줄인다.
    /// </summary>
    [SerializeField] private KeyCode hitKey = KeyCode.H;

    [Header("피격 테스트 수치")]

    /// <summary>
    /// 디버그 피격 1회당 줄일 안전 구간 비율.
    /// </summary>
    [SerializeField, Range(0.01f, 1f)] private float debugHitShrinkAmount01 = 0.08f;

    /// <summary>
    /// 매 프레임 디버그 키 입력을 감시한다.
    /// </summary>
    private void Update()
    {
        if (controller == null)
        {
            return;
        }

        if (Input.GetKeyDown(startKey))
        {
            controller.StartBalanceMinigame();
        }

        if (Input.GetKeyDown(hitKey))
        {
            controller.ApplyHit(debugHitShrinkAmount01);
        }
    }
}
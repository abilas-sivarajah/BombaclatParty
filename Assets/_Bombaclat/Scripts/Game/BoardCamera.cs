using Unity.Cinemachine;
using UnityEngine;

namespace Bombaclat
{
    /// <summary>
    /// Picks the active Cinemachine camera: a slow orbit over the island in the menu/lobby,
    /// and a follow camera on whoever's turn it is during the game.
    /// </summary>
    public class BoardCamera : MonoBehaviour
    {
        [SerializeField] Transform m_OrbitPivot;
        [SerializeField] CinemachineCamera m_Overview;
        [SerializeField] CinemachineCamera m_Follow;
        [SerializeField] float m_OrbitSpeed = 4f;

        void LateUpdate()
        {
            var gm = BoardGameManager.Instance;
            Transform focus = null;
            if (gm != null && gm.IsSpawned && gm.Phase.Value == GamePhase.Playing &&
                BoardView.Instance != null && BoardView.Instance.TryGetPawn(gm.CurrentPlayerId, out var pawn))
            {
                focus = pawn.transform;
            }

            bool following = focus != null;
            if (following) m_Follow.Follow = focus;
            m_Follow.Priority = following ? 20 : 0;
            m_Overview.Priority = 10;

            // Only orbit while nobody is watching the overview closely (menu, lobby, results).
            if (!following) m_OrbitPivot.Rotate(Vector3.up, m_OrbitSpeed * Time.deltaTime, Space.World);
        }
    }
}

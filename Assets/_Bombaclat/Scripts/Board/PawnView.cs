using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Bombaclat
{
    /// <summary>
    /// Client-side visual for one player on the board. Purely cosmetic: the server owns the
    /// real position (PlayerData.Tile); this just hops, squashes and bounces to match it.
    /// </summary>
    public class PawnView : MonoBehaviour
    {
        public ulong ClientId { get; private set; }
        public bool IsMoving => m_MoveRoutine != null;
        public int StepsLeft { get; private set; }
        public Vector3 HeadPosition => transform.position + Vector3.up * 1.55f;

        /// <summary>Time.time until which the die is spinning; afterwards it shows <see cref="DieValue"/>.</summary>
        public float DieRollingUntil { get; private set; } = -1f;
        public int DieValue { get; private set; }
        public bool DieVisible { get; private set; }

        Transform m_Visual;
        Transform m_Arrow;
        Transform m_Die;
        Vector3 m_Rest;
        bool m_HasRest;
        bool m_IsTurn;
        Coroutine m_MoveRoutine;
        Coroutine m_TeleportRoutine;
        float m_Seed;
        Vector3 m_Squash = Vector3.one;

        public void Init(BoardView board, ulong clientId, Color color)
        {
            ClientId = clientId;
            m_Seed = Random.value * 10f;
            name = $"Pawn {clientId}";

            m_Visual = new GameObject("Visual").transform;
            m_Visual.SetParent(transform, false);

            var dark = color * 0.7f; dark.a = 1f;
            board.Part("Body", m_Visual, board.Meshes.PawnBody, color, Vector3.zero);
            board.Part("Head", m_Visual, board.Meshes.PawnHead, color, new Vector3(0f, 0.98f, 0f));
            board.Part("Belt", m_Visual, board.Meshes.PawnBelt, dark, new Vector3(0f, 0.30f, 0f));
            foreach (float side in new[] { -1f, 1f })
            {
                var eye = board.Part("Eye", m_Visual, board.Meshes.Eye, Color.white, new Vector3(0.12f * side, 1.04f, 0.24f));
                board.Part("Pupil", eye, board.Meshes.Pupil, new Color(0.08f, 0.08f, 0.12f), new Vector3(0.01f * side, 0.01f, 0.06f));
            }

            m_Arrow = board.Part("TurnArrow", transform, board.Meshes.Arrow, Palette.Gold, new Vector3(0f, 2.0f, 0f));
            m_Arrow.localRotation = Quaternion.Euler(180f, 0f, 0f);
            m_Arrow.gameObject.SetActive(false);

            m_Die = board.Part("Die", transform, board.Meshes.Die, Color.white, new Vector3(0f, 2.3f, 0f));
            m_Die.gameObject.SetActive(false);
        }

        public void SetTurn(bool isTurn)
        {
            m_IsTurn = isTurn;
            if (m_Arrow) m_Arrow.gameObject.SetActive(isTurn && !DieVisible);
        }

        /// <summary>Where the pawn should stand when idle. Far jumps (swaps, resets) teleport with a poof.</summary>
        public void SetRest(Vector3 position)
        {
            if (!m_HasRest)
            {
                m_HasRest = true;
                m_Rest = position;
                transform.position = position;
                return;
            }
            if ((position - m_Rest).sqrMagnitude < 0.0001f) return;
            bool far = (position - transform.position).sqrMagnitude > 9f;
            m_Rest = position;
            if (far && !IsMoving)
            {
                if (m_TeleportRoutine != null) StopCoroutine(m_TeleportRoutine);
                m_TeleportRoutine = StartCoroutine(TeleportRoutine(position));
            }
        }

        public void RollDie(int value, float duration)
        {
            DieValue = value;
            DieRollingUntil = Time.time + duration;
            DieVisible = true;
            m_Die.gameObject.SetActive(true);
            m_Arrow.gameObject.SetActive(false);
            m_Squash = new Vector3(1.2f, 0.75f, 1.2f); // little "jump up and hit it" anticipation
        }

        public void PlayMove(List<Vector3> path, float hopTime)
        {
            if (m_MoveRoutine != null) StopCoroutine(m_MoveRoutine);
            if (m_TeleportRoutine != null) { StopCoroutine(m_TeleportRoutine); m_TeleportRoutine = null; }
            m_MoveRoutine = StartCoroutine(MoveRoutine(path, hopTime));
        }

        IEnumerator MoveRoutine(List<Vector3> path, float hopTime)
        {
            m_Die.gameObject.SetActive(false);
            StepsLeft = path.Count;
            foreach (var target in path)
            {
                var from = transform.position;
                var flat = target - from; flat.y = 0f;
                var look = flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(flat) : transform.rotation;
                float t = 0f;
                while (t < 1f)
                {
                    t = Mathf.Min(1f, t + Time.deltaTime / hopTime);
                    transform.position = Vector3.Lerp(from, target, t) + Vector3.up * (4f * t * (1f - t) * 0.75f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-18f * Time.deltaTime));
                    float stretch = Mathf.Sin(t * Mathf.PI) * 0.18f;
                    m_Squash = new Vector3(1f - stretch * 0.5f, 1f + stretch, 1f - stretch * 0.5f);
                    yield return null;
                }
                m_Squash = new Vector3(1.18f, 0.78f, 1.18f);
                StepsLeft--;
            }
            m_Rest = transform.position;
            DieVisible = false;
            m_MoveRoutine = null;
            SetTurn(m_IsTurn);
        }

        IEnumerator TeleportRoutine(Vector3 target)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f)
            {
                m_Visual.localScale = Vector3.one * (1f - t);
                yield return null;
            }
            transform.position = target;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
            {
                float overshoot = Mathf.Sin(t * Mathf.PI) * 0.3f;
                m_Visual.localScale = Vector3.one * (t + overshoot);
                yield return null;
            }
            m_Visual.localScale = Vector3.one;
            m_TeleportRoutine = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!IsMoving && m_TeleportRoutine == null)
            {
                transform.position = Vector3.Lerp(transform.position, m_Rest, 1f - Mathf.Exp(-10f * dt));
                var cam = Camera.main;
                if (cam)
                {
                    var toCam = -cam.transform.forward; toCam.y = 0f;
                    if (toCam.sqrMagnitude > 0.001f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toCam), 1f - Mathf.Exp(-4f * dt));
                }
            }

            // Squash & stretch relaxes back to normal; idle pawns breathe a little.
            m_Squash = Vector3.Lerp(m_Squash, Vector3.one, 1f - Mathf.Exp(-12f * dt));
            float breathe = IsMoving ? 0f : Mathf.Sin(Time.time * 2.4f + m_Seed) * 0.025f;
            if (m_TeleportRoutine == null)
                m_Visual.localScale = new Vector3(m_Squash.x - breathe, m_Squash.y + breathe, m_Squash.z - breathe);

            if (m_Arrow.gameObject.activeSelf)
            {
                m_Arrow.localPosition = new Vector3(0f, 2.0f + Mathf.Sin(Time.time * 4f) * 0.12f, 0f);
                m_Arrow.Rotate(Vector3.up, 120f * dt, Space.World);
            }

            if (m_Die.gameObject.activeSelf)
            {
                bool rolling = Time.time < DieRollingUntil;
                if (rolling) m_Die.Rotate(new Vector3(420f, 610f, 300f) * dt, Space.Self);
                else m_Die.rotation = Quaternion.Slerp(m_Die.rotation, Quaternion.Euler(0f, 45f, 0f), 1f - Mathf.Exp(-12f * dt));
                m_Die.localPosition = new Vector3(0f, 2.3f + (rolling ? Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.25f : 0f), 0f);
            }
        }
    }
}

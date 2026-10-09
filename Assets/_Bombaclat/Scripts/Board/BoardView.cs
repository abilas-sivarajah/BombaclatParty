using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bombaclat
{
    /// <summary>
    /// Builds the island, tiles and decoration procedurally, and keeps the pawns in sync with
    /// the networked game state. Runs identically on every client.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        public static BoardView Instance { get; private set; }

        [SerializeField] Material m_BaseMaterial;

        public MeshLibrary Meshes { get; private set; }

        readonly Dictionary<Color, Material> m_Materials = new();
        readonly Dictionary<ulong, PawnView> m_Pawns = new();
        readonly List<ulong> m_Stale = new();
        readonly Dictionary<int, int> m_TileCounts = new();
        readonly Dictionary<int, int> m_TileSlots = new();
        readonly List<Transform> m_Spinners = new();
        Transform m_Crown;
        Transform m_CrownRing;
        Transform m_Spark;
        int m_CrownTile = -1;

        static readonly Color Grass = new(0.45f, 0.76f, 0.34f);
        static readonly Color GrassDark = new(0.33f, 0.60f, 0.28f);
        static readonly Color Sand = new(0.93f, 0.84f, 0.62f);
        static readonly Color Water = new(0.22f, 0.62f, 0.86f);
        static readonly Color Bark = new(0.48f, 0.32f, 0.20f);
        static readonly Color Stone = new(0.62f, 0.62f, 0.66f);

        public sealed class MeshLibrary
        {
            public Mesh TileTop, TileBase, StartTop, SteppingStone, PawnBody, PawnHead, PawnBelt, Eye, Pupil, Arrow, Die, ChaosGem,
                CrownBand, CrownSpike, Jewel, CrownRing;
            public Mesh[] Rocks, Bushes, Cones, Blobs, Trunks;

            public MeshLibrary()
            {
                TileTop = LowPolyMesh.Prism(6, BoardLayout.TileRadius, BoardLayout.TileRadius * 0.92f, BoardLayout.TileHeight);
                StartTop = LowPolyMesh.Prism(8, BoardLayout.TileRadius * 1.25f, BoardLayout.TileRadius * 1.15f, BoardLayout.TileHeight);
                TileBase = LowPolyMesh.Prism(6, BoardLayout.TileRadius * 1.12f, BoardLayout.TileRadius * 1.08f, 0.1f);
                SteppingStone = LowPolyMesh.Prism(5, 0.2f, 0.16f, 0.07f, 0.15f, 9);
                PawnBody = LowPolyMesh.Prism(8, 0.36f, 0.24f, 0.72f);
                PawnBelt = LowPolyMesh.Prism(8, 0.335f, 0.33f, 0.08f);
                PawnHead = LowPolyMesh.IcoSphere(0.32f, 1);
                Eye = LowPolyMesh.IcoSphere(0.085f, 1);
                Pupil = LowPolyMesh.IcoSphere(0.045f, 0);
                Arrow = LowPolyMesh.Prism(4, 0.28f, 0f, 0.45f);
                Die = Centered(LowPolyMesh.Prism(4, 0.42f, 0.42f, 0.6f));
                ChaosGem = LowPolyMesh.Gem(0.3f, 0.65f, 4);
                CrownBand = LowPolyMesh.Prism(10, 0.55f, 0.62f, 0.34f);
                CrownSpike = LowPolyMesh.Prism(4, 0.14f, 0f, 0.42f);
                Jewel = LowPolyMesh.IcoSphere(0.09f, 0);
                CrownRing = LowPolyMesh.Prism(6, BoardLayout.TileRadius * 1.32f, BoardLayout.TileRadius * 1.3f, 0.08f);

                Rocks = new Mesh[4];
                Bushes = new Mesh[3];
                Cones = new Mesh[3];
                Blobs = new Mesh[3];
                Trunks = new Mesh[2];
                for (int i = 0; i < Rocks.Length; i++) Rocks[i] = LowPolyMesh.IcoSphere(0.5f, 0, 0.28f, 100 + i);
                for (int i = 0; i < Bushes.Length; i++) Bushes[i] = LowPolyMesh.IcoSphere(0.5f, 1, 0.12f, 200 + i);
                for (int i = 0; i < Cones.Length; i++) Cones[i] = LowPolyMesh.Prism(7, 0.85f, 0f, 1.5f, 0.12f, 300 + i);
                for (int i = 0; i < Blobs.Length; i++) Blobs[i] = LowPolyMesh.IcoSphere(0.85f, 1, 0.15f, 400 + i);
                for (int i = 0; i < Trunks.Length; i++) Trunks[i] = LowPolyMesh.Prism(5, 0.17f, 0.12f, 0.8f, 0.1f, 500 + i);
            }

            static Mesh Centered(Mesh mesh)
            {
                var center = mesh.bounds.center;
                var verts = mesh.vertices;
                for (int i = 0; i < verts.Length; i++) verts[i] -= center;
                mesh.vertices = verts;
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        void Awake()
        {
            Instance = this;
            Meshes = new MeshLibrary();
            BuildEnvironment();
            BuildTiles();
            BuildDecoration();
        }

        void OnEnable()
        {
            BoardGameManager.DiceRolled += OnDiceRolled;
            BoardGameManager.MoveStarted += OnMoveStarted;
        }

        void OnDisable()
        {
            BoardGameManager.DiceRolled -= OnDiceRolled;
            BoardGameManager.MoveStarted -= OnMoveStarted;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool TryGetPawn(ulong clientId, out PawnView pawn) => m_Pawns.TryGetValue(clientId, out pawn);

        // ------------------------------------------------------------------ building blocks

        public Material Mat(Color color, float smoothness = 0.12f)
        {
            if (m_Materials.TryGetValue(color, out var material)) return material;
            material = new Material(m_BaseMaterial) { name = $"Bombaclat {ColorUtility.ToHtmlStringRGB(color)}" };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            m_Materials[color] = material;
            return material;
        }

        public Transform Part(string partName, Transform parent, Mesh mesh, Color color, Vector3 localPosition,
            float smoothness = 0.12f, bool castShadows = true)
        {
            var go = new GameObject(partName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Mat(color, smoothness);
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        Transform Group(string groupName)
        {
            var group = new GameObject(groupName).transform;
            group.SetParent(transform, false);
            return group;
        }

        // ------------------------------------------------------------------ environment

        void BuildEnvironment()
        {
            var env = Group("Environment");

            var water = Part("Water", env, LowPolyMesh.Prism(48, 1f, 1f, 1f), Water, new Vector3(0f, -1.5f, 0f), 0.75f, false);
            water.localScale = new Vector3(160f, 1f, 160f);

            var sand = Part("Sand", env, LowPolyMesh.Prism(36, 1f, 0.97f, 1f, 0.03f, 3), Sand, new Vector3(0f, -1.1f, 0f));
            sand.localScale = new Vector3(27.5f, 0.8f, 20f);

            var grass = Part("Grass", env, LowPolyMesh.Prism(36, 1f, 0.985f, 1f, 0.035f, 7), Grass, new Vector3(0f, -0.8f, 0f));
            grass.localScale = new Vector3(25f, 0.8f, 17.8f);

            // The landmark in the middle of the island: a giant cartoon bomb on a hill.
            var hill = Part("Hill", env, LowPolyMesh.Prism(10, 4.2f, 2.6f, 0.9f, 0.08f, 11), GrassDark, Vector3.zero);
            var bomb = Part("Bomb", hill, LowPolyMesh.IcoSphere(2.1f, 1), new Color(0.13f, 0.13f, 0.17f), new Vector3(0f, 2.75f, 0f), 0.5f);
            Part("Shine", bomb, LowPolyMesh.IcoSphere(0.35f, 1), new Color(0.75f, 0.78f, 0.9f), new Vector3(-0.85f, 1.05f, -1.3f), 0.8f, false);
            var cap = Part("Cap", bomb, LowPolyMesh.Prism(8, 0.55f, 0.5f, 0.45f), new Color(0.45f, 0.45f, 0.5f), new Vector3(0f, 1.9f, 0f));
            var fuse = Part("Fuse", cap, LowPolyMesh.Prism(5, 0.1f, 0.09f, 1.1f), Bark, new Vector3(0f, 0.4f, 0f));
            fuse.localRotation = Quaternion.Euler(0f, 0f, -24f);
            m_Spark = Part("Spark", fuse, LowPolyMesh.IcoSphere(0.26f, 0), new Color(1f, 0.6f, 0.1f), new Vector3(0f, 1.15f, 0f), 0.9f, false);
        }

        void BuildTiles()
        {
            var tiles = Group("Tiles");
            for (int i = 0; i < BoardLayout.TileCount; i++)
            {
                var type = BoardLayout.Type(i);
                var tile = new GameObject($"Tile {i:00} {type}").transform;
                tile.SetParent(tiles, false);
                tile.position = BoardLayout.Position(i);
                tile.rotation = Quaternion.LookRotation(BoardLayout.Forward(i));

                var baseColor = Palette.Tile(type) * 0.55f; baseColor.a = 1f;
                Part("Base", tile, Meshes.TileBase, baseColor, new Vector3(0f, -0.04f, 0f));
                Part("Top", tile, type == TileType.Start ? Meshes.StartTop : Meshes.TileTop, Palette.Tile(type), Vector3.zero, 0.3f);

                if (type == TileType.Chaos)
                {
                    var gem = Part("ChaosGem", tile, Meshes.ChaosGem, new Color(0.85f, 0.55f, 1f), new Vector3(0f, 0.85f, 0f), 0.8f);
                    m_Spinners.Add(gem);
                }

                // A stepping stone in the gap between this tile and the next.
                var midpoint = Vector3.Lerp(BoardLayout.Position(i), BoardLayout.Position(i + 1), 0.5f);
                var stone = Part("Stone", tiles, Meshes.SteppingStone, new Color(0.82f, 0.78f, 0.7f), midpoint, 0.1f, false);
                stone.localRotation = Quaternion.Euler(0f, (i * 37) % 360, 0f);
            }

            BuildCrown();
        }

        void BuildCrown()
        {
            m_Crown = Group("Crown");
            var gold = Palette.Gold;
            var ruby = new Color(0.95f, 0.15f, 0.3f);
            Part("Band", m_Crown, Meshes.CrownBand, gold, Vector3.zero, 0.85f);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                var rim = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Part("Spike", m_Crown, Meshes.CrownSpike, gold, rim * 0.5f + Vector3.up * 0.32f, 0.85f);
                Part("TipJewel", m_Crown, Meshes.Jewel, ruby, rim * 0.5f + Vector3.up * 0.76f, 0.9f, false);
                Part("BandJewel", m_Crown, Meshes.Jewel, ruby, rim * 0.6f + Vector3.up * 0.16f, 0.9f, false);
            }
            m_Crown.localScale = Vector3.one * 1.15f;
            m_Crown.gameObject.SetActive(false);

            m_CrownRing = Part("CrownRing", transform, Meshes.CrownRing, gold, Vector3.zero, 0.6f, false);
            m_CrownRing.gameObject.SetActive(false);
        }

        void BuildDecoration()
        {
            var deco = Group("Decoration");
            var rng = new System.Random(4242);
            float Rand(float min, float max) => min + (float)rng.NextDouble() * (max - min);

            int placed = 0, attempts = 0;
            while (placed < 120 && attempts++ < 4000)
            {
                var p = new Vector3(Rand(-24f, 24f), 0f, Rand(-17f, 17f));
                float ellipse = (p.x * p.x) / (23.5f * 23.5f) + (p.z * p.z) / (16.3f * 16.3f);
                if (ellipse > 1f) continue;
                if (p.magnitude < 5f) continue; // keep the bomb hill clear
                if (NearPath(p, 2.0f)) continue;

                var item = new GameObject("Deco").transform;
                item.SetParent(deco, false);
                item.position = p;
                item.rotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
                float size = Rand(0.75f, 1.3f);
                item.localScale = Vector3.one * size;

                double kind = rng.NextDouble();
                var leaf = Color.Lerp(new Color(0.2f, 0.55f, 0.25f), new Color(0.45f, 0.75f, 0.3f), Rand(0f, 1f));
                if (kind < 0.35)
                {
                    Part("Trunk", item, Meshes.Trunks[rng.Next(Meshes.Trunks.Length)], Bark, Vector3.zero);
                    Part("Leaves", item, Meshes.Cones[rng.Next(Meshes.Cones.Length)], leaf, new Vector3(0f, 0.55f, 0f));
                    var top = Part("Leaves2", item, Meshes.Cones[rng.Next(Meshes.Cones.Length)], leaf * 1.08f, new Vector3(0f, 1.25f, 0f));
                    top.localScale = Vector3.one * 0.7f;
                }
                else if (kind < 0.6)
                {
                    Part("Trunk", item, Meshes.Trunks[rng.Next(Meshes.Trunks.Length)], Bark, Vector3.zero);
                    Part("Crown", item, Meshes.Blobs[rng.Next(Meshes.Blobs.Length)], leaf, new Vector3(0f, 1.35f, 0f));
                }
                else if (kind < 0.82)
                {
                    var bush = Part("Bush", item, Meshes.Bushes[rng.Next(Meshes.Bushes.Length)], leaf * 0.9f, new Vector3(0f, 0.15f, 0f));
                    bush.localScale = new Vector3(1.2f, 0.8f, 1.2f);
                }
                else
                {
                    var rock = Part("Rock", item, Meshes.Rocks[rng.Next(Meshes.Rocks.Length)], Stone * Rand(0.85f, 1.1f), new Vector3(0f, 0.1f, 0f));
                    rock.localScale = new Vector3(Rand(0.8f, 1.4f), Rand(0.5f, 0.9f), Rand(0.8f, 1.3f));
                }
                placed++;
            }
        }

        static bool NearPath(Vector3 p, float distance)
        {
            for (int i = 0; i < BoardLayout.TileCount; i++)
            {
                var a = BoardLayout.Position(i);
                var b = BoardLayout.Position(i + 1);
                var ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                if ((a + ab * t - p).sqrMagnitude < distance * distance) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ live state

        void Update()
        {
            float t = Time.time;
            foreach (var spinner in m_Spinners)
            {
                spinner.Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);
                spinner.localPosition = new Vector3(0f, 0.85f + Mathf.Sin(t * 2f + spinner.parent.GetSiblingIndex()) * 0.08f, 0f);
            }
            if (m_Spark) m_Spark.localScale = Vector3.one * (1f + Mathf.Sin(t * 18f) * 0.25f + Mathf.Sin(t * 7f) * 0.1f);

            var gm = BoardGameManager.Instance;
            bool live = gm != null && gm.IsSpawned;
            SyncPawns(gm, live);
            SyncCrown(live && gm.Phase.Value != GamePhase.Lobby ? gm.CrownTile.Value : -1);
        }

        void SyncPawns(BoardGameManager gm, bool live)
        {
            m_Stale.Clear();
            m_Stale.AddRange(m_Pawns.Keys);
            m_TileCounts.Clear();
            m_TileSlots.Clear();

            if (live)
            {
                var players = gm.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    m_TileCounts.TryGetValue(players[i].Tile, out int count);
                    m_TileCounts[players[i].Tile] = count + 1;
                }

                ulong current = gm.CurrentPlayerId;
                for (int i = 0; i < players.Count; i++)
                {
                    var data = players[i];
                    m_Stale.Remove(data.ClientId);
                    if (!m_Pawns.TryGetValue(data.ClientId, out var pawn))
                    {
                        pawn = new GameObject().AddComponent<PawnView>();
                        pawn.transform.SetParent(transform, false);
                        pawn.Init(this, data.ClientId, Palette.Player(data.ColorIndex));
                        m_Pawns[data.ClientId] = pawn;
                    }

                    m_TileSlots.TryGetValue(data.Tile, out int slot);
                    m_TileSlots[data.Tile] = slot + 1;
                    pawn.SetRest(SlotPosition(data.Tile, slot, m_TileCounts[data.Tile]));
                    pawn.SetTurn(gm.Phase.Value == GamePhase.Playing && data.ClientId == current);
                }
            }

            foreach (var id in m_Stale)
            {
                Destroy(m_Pawns[id].gameObject);
                m_Pawns.Remove(id);
            }
        }

        /// <summary>Spread several pawns standing on the same tile in a small circle.</summary>
        public static Vector3 SlotPosition(int tile, int slot, int count)
        {
            var top = BoardLayout.Top(tile);
            if (count <= 1) return top;
            float radius = count <= 2 ? 0.38f : 0.5f;
            float angle = slot * Mathf.PI * 2f / count + Mathf.PI * 0.25f;
            return top + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        }

        void SyncCrown(int tile)
        {
            if (tile < 0)
            {
                m_Crown.gameObject.SetActive(false);
                m_CrownRing.gameObject.SetActive(false);
                m_CrownTile = -1;
                return;
            }

            var target = BoardLayout.Top(tile) + Vector3.up * (1.45f + Mathf.Sin(Time.time * 2.2f) * 0.12f);
            if (!m_Crown.gameObject.activeSelf || m_CrownTile < 0)
            {
                m_Crown.gameObject.SetActive(true);
                m_Crown.position = target + Vector3.up * 6f; // drop in from the sky
            }
            m_CrownTile = tile;
            m_Crown.position = Vector3.Lerp(m_Crown.position, target, 1f - Mathf.Exp(-3f * Time.deltaTime));
            m_Crown.Rotate(Vector3.up, 70f * Time.deltaTime, Space.World);

            // Gold rim around the crown's tile so you can spot it from across the board.
            m_CrownRing.gameObject.SetActive(true);
            m_CrownRing.position = BoardLayout.Position(tile) + Vector3.down * 0.05f;
            m_CrownRing.rotation = Quaternion.LookRotation(BoardLayout.Forward(tile));
            m_CrownRing.localScale = new Vector3(1f, 1f, 1f) * (1f + Mathf.Sin(Time.time * 4f) * 0.03f);
        }

        void OnDiceRolled(ulong clientId, int value, float duration)
        {
            if (m_Pawns.TryGetValue(clientId, out var pawn)) pawn.RollDie(value, duration);
        }

        void OnMoveStarted(ulong clientId, int fromTile, int steps)
        {
            if (!m_Pawns.TryGetValue(clientId, out var pawn)) return;
            var path = new List<Vector3>(steps);
            for (int i = 1; i <= steps; i++) path.Add(BoardLayout.Top(fromTile + i));
            pawn.PlayMove(path, BoardGameManager.HopTime);
        }
    }
}

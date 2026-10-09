using UnityEngine;

namespace Bombaclat
{
    /// <summary>
    /// Deterministic board definition: tile positions and types. Every client builds the exact
    /// same board from this, so only tile indices ever need to go over the network.
    /// </summary>
    public static class BoardLayout
    {
        public const int TileCount = 36;
        public const float TileRadius = 0.88f;
        public const float TileHeight = 0.22f;

        static Vector3[] s_Positions;
        static Vector3[] s_Forward;
        static TileType[] s_Types;

        public static int Wrap(int index) => ((index % TileCount) + TileCount) % TileCount;

        public static Vector3 Position(int index) { Ensure(); return s_Positions[Wrap(index)]; }

        /// <summary>World position of the walkable top surface of a tile.</summary>
        public static Vector3 Top(int index) => Position(index) + Vector3.up * TileHeight;

        /// <summary>Direction of travel along the path at this tile.</summary>
        public static Vector3 Forward(int index) { Ensure(); return s_Forward[Wrap(index)]; }

        public static TileType Type(int index) { Ensure(); return s_Types[Wrap(index)]; }

        static void Ensure()
        {
            if (s_Positions != null) return;

            // Sample a wobbly closed loop densely, then place tiles at equal arc-length spacing
            // so they never overlap no matter how the curve bends.
            const int samples = 1440;
            var curve = new Vector3[samples + 1];
            var length = new float[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples * Mathf.PI * 2f;
                float r = 1f + 0.10f * Mathf.Sin(3f * t + 0.6f) + 0.06f * Mathf.Sin(5f * t + 1.3f);
                curve[i] = new Vector3(Mathf.Cos(t) * 17f * r, 0f, Mathf.Sin(t) * 11f * r);
                if (i > 0) length[i] = length[i - 1] + Vector3.Distance(curve[i - 1], curve[i]);
            }

            float total = length[samples];
            s_Positions = new Vector3[TileCount];
            s_Forward = new Vector3[TileCount];
            int seg = 0;
            for (int tile = 0; tile < TileCount; tile++)
            {
                float target = tile * total / TileCount;
                while (seg < samples - 1 && length[seg + 1] < target) seg++;
                float k = Mathf.InverseLerp(length[seg], length[seg + 1], target);
                s_Positions[tile] = Vector3.Lerp(curve[seg], curve[seg + 1], k);
            }
            for (int tile = 0; tile < TileCount; tile++)
                s_Forward[tile] = (s_Positions[Wrap(tile + 1)] - s_Positions[Wrap(tile - 1)]).normalized;

            // Tile types: mostly coins, some pain, some chaos. Never two non-blue tiles in a row
            // right after the start, so the opening turns feel fair.
            var rng = new System.Random(1337);
            s_Types = new TileType[TileCount];
            s_Types[0] = TileType.Start;
            for (int i = 1; i < TileCount; i++)
            {
                double roll = rng.NextDouble();
                var type = roll < 0.58 ? TileType.Blue : roll < 0.80 ? TileType.Red : TileType.Chaos;
                if (type != TileType.Blue && (i <= 2 || s_Types[i - 1] == type)) type = TileType.Blue;
                s_Types[i] = type;
            }
        }
    }
}

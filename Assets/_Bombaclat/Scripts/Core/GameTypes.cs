using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Bombaclat
{
    public enum GamePhase : byte { Lobby, Playing, GameOver }

    public enum TurnStage : byte { WaitingForRoll, Rolling, Moving, Resolving }

    public enum TileType : byte { Start, Blue, Red, Chaos }

    /// <summary>Everything the board needs to know about one player. Synced via a NetworkList.</summary>
    public struct PlayerData : INetworkSerializable, IEquatable<PlayerData>
    {
        public ulong ClientId;
        public FixedString32Bytes Name;
        public int ColorIndex;
        public int Tile;
        public int Coins;
        public int Crowns;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref ColorIndex);
            serializer.SerializeValue(ref Tile);
            serializer.SerializeValue(ref Coins);
            serializer.SerializeValue(ref Crowns);
        }

        public bool Equals(PlayerData other) =>
            ClientId == other.ClientId && Name.Equals(other.Name) && ColorIndex == other.ColorIndex &&
            Tile == other.Tile && Coins == other.Coins && Crowns == other.Crowns;
    }

    public static class Palette
    {
        public static readonly Color[] Players =
        {
            new(0.95f, 0.26f, 0.30f), // red
            new(0.20f, 0.55f, 0.98f), // blue
            new(0.30f, 0.85f, 0.35f), // green
            new(1.00f, 0.80f, 0.15f), // yellow
            new(0.70f, 0.35f, 0.95f), // purple
            new(1.00f, 0.55f, 0.15f), // orange
            new(0.15f, 0.85f, 0.85f), // cyan
            new(1.00f, 0.45f, 0.75f), // pink
        };

        public static Color Player(int index) => Players[Mathf.Abs(index) % Players.Length];

        public static Color Tile(TileType type) => type switch
        {
            TileType.Start => new Color(1.00f, 0.86f, 0.35f),
            TileType.Blue => new Color(0.25f, 0.55f, 0.95f),
            TileType.Red => new Color(0.92f, 0.25f, 0.25f),
            TileType.Chaos => new Color(0.62f, 0.30f, 0.90f),
            _ => Color.white,
        };

        public static readonly Color Gold = new(1.00f, 0.78f, 0.15f);
    }

    /// <summary>The local player's display name, remembered between sessions.</summary>
    public static class LocalProfile
    {
        const string NameKey = "bombaclat.name";
        public const int MaxNameLength = 16;

        public static string Name
        {
            get
            {
                var name = PlayerPrefs.GetString(NameKey, "");
                return string.IsNullOrWhiteSpace(name) ? $"Spieler{UnityEngine.Random.Range(100, 999)}" : name;
            }
            set => PlayerPrefs.SetString(NameKey, Sanitize(value));
        }

        public static string Sanitize(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
            // FixedString32Bytes holds 29 UTF-8 bytes; trim until it fits.
            while (System.Text.Encoding.UTF8.GetByteCount(name) > 29) name = name.Substring(0, name.Length - 1);
            return name;
        }
    }
}

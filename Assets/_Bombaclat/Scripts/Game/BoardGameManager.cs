using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Bombaclat
{
    /// <summary>
    /// Server-authoritative board game rules. Clients only ever ask ("roll my dice"); the server
    /// decides everything and syncs the result through NetworkVariables plus a few cosmetic RPCs.
    /// </summary>
    public class BoardGameManager : NetworkBehaviour
    {
        public static BoardGameManager Instance { get; private set; }

        public const int MaxPlayers = 8;
        public const int StartCoins = 10;
        public const int CrownCost = 20;
        public const int BlueCoins = 3;
        public const int RedCoins = 3;
        public const int LapBonus = 5;
        public const float HopTime = 0.26f;
        public const float DiceTime = 1.3f;

        // Cosmetic events for views (raised on every client, including the host).
        public static event Action<ulong, int, float> DiceRolled;   // clientId, value, spin duration
        public static event Action<ulong, int, int> MoveStarted;    // clientId, from tile, steps
        public static event Action<ulong, int> CoinsChanged;        // clientId, delta
        public static event Action<ulong, int> CrownsChanged;       // clientId, delta
        public static event Action<string> FeedPosted;
        public static event Action<string, string> BannerShown;     // title, subtitle

        public NetworkList<PlayerData> Players;
        public readonly NetworkVariable<GamePhase> Phase = new(GamePhase.Lobby);
        public readonly NetworkVariable<TurnStage> Stage = new(TurnStage.WaitingForRoll);
        public readonly NetworkVariable<int> CurrentTurn = new(0);
        public readonly NetworkVariable<int> Round = new(1);
        public readonly NetworkVariable<int> MaxRounds = new(10);
        public readonly NetworkVariable<int> CrownTile = new(-1);

        Coroutine m_Turn;

        public ulong CurrentPlayerId =>
            CurrentTurn.Value >= 0 && CurrentTurn.Value < Players.Count ? Players[CurrentTurn.Value].ClientId : ulong.MaxValue;

        public bool IsMyTurn =>
            IsSpawned && Phase.Value == GamePhase.Playing && Stage.Value == TurnStage.WaitingForRoll &&
            CurrentPlayerId == NetworkManager.LocalClientId;

        public int IndexOf(ulong clientId)
        {
            for (int i = 0; i < Players.Count; i++)
                if (Players[i].ClientId == clientId) return i;
            return -1;
        }

        void Awake()
        {
            Instance = this;
            Players = new NetworkList<PlayerData>();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // In-scene objects survive a shutdown, so wipe anything left from the last session.
                Players.Clear();
                Phase.Value = GamePhase.Lobby;
                Stage.Value = TurnStage.WaitingForRoll;
                CurrentTurn.Value = 0;
                Round.Value = 1;
                CrownTile.Value = -1;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
            if (IsClient) RegisterRpc(LocalProfile.Name);
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            if (m_Turn != null) { StopCoroutine(m_Turn); m_Turn = null; }
        }

        // ------------------------------------------------------------------ requests from clients

        [Rpc(SendTo.Server)]
        void RegisterRpc(string playerName, RpcParams rpcParams = default)
        {
            ulong id = rpcParams.Receive.SenderClientId;
            if (IndexOf(id) >= 0) return;
            if (Phase.Value != GamePhase.Lobby || Players.Count >= MaxPlayers)
            {
                FeedRpc($"{LocalProfile.Sanitize(playerName)} schaut zu.");
                return;
            }

            playerName = LocalProfile.Sanitize(playerName);
            if (string.IsNullOrEmpty(playerName)) playerName = $"Spieler {Players.Count + 1}";
            Players.Add(new PlayerData
            {
                ClientId = id,
                Name = playerName,
                ColorIndex = FreeColor(),
                Tile = 0,
                Coins = StartCoins,
            });
            FeedRpc($"{playerName} ist dabei!");
        }

        [Rpc(SendTo.Server)]
        public void StartGameRpc(int rounds, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            if (Phase.Value != GamePhase.Lobby || Players.Count == 0) return;

            // Fresh stats and a random turn order.
            var order = new List<PlayerData>();
            foreach (var p in Players) order.Add(p);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            Players.Clear();
            foreach (var p in order)
            {
                var fresh = p;
                fresh.Tile = 0;
                fresh.Coins = StartCoins;
                fresh.Crowns = 0;
                Players.Add(fresh);
            }

            MaxRounds.Value = Mathf.Clamp(rounds, 1, 30);
            Round.Value = 1;
            CurrentTurn.Value = 0;
            CrownTile.Value = PickCrownTile(0);
            Stage.Value = TurnStage.WaitingForRoll;
            Phase.Value = GamePhase.Playing;

            BannerRpc("LOS GEHT'S!", $"{Players[0].Name} fängt an");
            FeedRpc($"Die Krone kostet {CrownCost} Coins. Lauf drüber und schnapp sie dir!");
        }

        [Rpc(SendTo.Server)]
        public void RollDiceRpc(RpcParams rpcParams = default)
        {
            if (Phase.Value != GamePhase.Playing || Stage.Value != TurnStage.WaitingForRoll) return;
            if (CurrentPlayerId != rpcParams.Receive.SenderClientId) return;
            Stage.Value = TurnStage.Rolling;
            m_Turn = StartCoroutine(TurnRoutine(CurrentPlayerId));
        }

        [Rpc(SendTo.Server)]
        public void BackToLobbyRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            if (m_Turn != null) { StopCoroutine(m_Turn); m_Turn = null; }
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                p.Tile = 0;
                p.Coins = StartCoins;
                p.Crowns = 0;
                Players[i] = p;
            }
            CrownTile.Value = -1;
            Stage.Value = TurnStage.WaitingForRoll;
            Phase.Value = GamePhase.Lobby;
        }

        // ------------------------------------------------------------------ the turn

        IEnumerator TurnRoutine(ulong clientId)
        {
            int roll = Random.Range(1, 11);
            DiceRpc(clientId, roll);
            yield return new WaitForSeconds(DiceTime + 0.5f);

            int index = IndexOf(clientId);
            if (index < 0) yield break; // left mid-turn; disconnect handler moved the turn on
            var player = Players[index];

            Stage.Value = TurnStage.Moving;
            MoveRpc(clientId, player.Tile, roll);
            yield return new WaitForSeconds(roll * HopTime + 0.35f);

            index = IndexOf(clientId);
            if (index < 0) yield break;
            player = Players[index];

            // Things that happen when you walk *past* a tile: lap bonus and the crown.
            bool boughtCrown = false;
            for (int step = 1; step <= roll; step++)
            {
                int tile = BoardLayout.Wrap(player.Tile + step);
                if (tile == 0)
                {
                    player.Coins += LapBonus;
                    CoinsRpc(clientId, LapBonus);
                    FeedRpc($"{player.Name} dreht eine Runde: +{LapBonus} Coins");
                }
                if (tile == CrownTile.Value && !boughtCrown)
                {
                    if (player.Coins >= CrownCost)
                    {
                        player.Coins -= CrownCost;
                        player.Crowns++;
                        boughtCrown = true;
                        CoinsRpc(clientId, -CrownCost);
                        CrownRpc(clientId, 1);
                        BannerRpc("KRONE!", $"{player.Name} kauft eine Krone für {CrownCost} Coins");
                    }
                    else
                    {
                        FeedRpc($"{player.Name} ist zu pleite für die Krone ({player.Coins}/{CrownCost})");
                    }
                }
            }
            player.Tile = BoardLayout.Wrap(player.Tile + roll);
            Players[index] = player;
            if (boughtCrown) CrownTile.Value = PickCrownTile(player.Tile);

            Stage.Value = TurnStage.Resolving;
            yield return new WaitForSeconds(boughtCrown ? 1.2f : 0.25f);
            yield return ResolveTile(clientId);
            yield return new WaitForSeconds(0.9f);

            m_Turn = null;
            if (IndexOf(clientId) >= 0) AdvanceTurn();
        }

        IEnumerator ResolveTile(ulong clientId)
        {
            int index = IndexOf(clientId);
            if (index < 0) yield break;
            var player = Players[index];

            switch (BoardLayout.Type(player.Tile))
            {
                case TileType.Blue:
                    AddCoins(clientId, BlueCoins);
                    FeedRpc($"{player.Name} landet auf Blau: +{BlueCoins} Coins");
                    break;
                case TileType.Red:
                    int lost = -AddCoins(clientId, -RedCoins);
                    FeedRpc($"{player.Name} landet auf Rot: -{lost} Coins");
                    break;
                case TileType.Chaos:
                    BannerRpc("CHAOS!", "...");
                    yield return new WaitForSeconds(1.1f);
                    ChaosEvent(clientId);
                    yield return new WaitForSeconds(1.0f);
                    break;
            }
        }

        void ChaosEvent(ulong clientId)
        {
            var player = Players[IndexOf(clientId)];
            var others = new List<PlayerData>();
            foreach (var p in Players) if (p.ClientId != clientId) others.Add(p);

            var victimsWithCoins = others.FindAll(p => p.Coins > 0);
            var victimsWithCrowns = others.FindAll(p => p.Crowns > 0);

            // Weighted pick; events that need victims only count if there are any.
            var options = new List<(int weight, Action run)>
            {
                (20, () =>
                {
                    int gain = AddCoins(clientId, 10);
                    BannerRpc("JACKPOT!", $"{player.Name} findet {gain} Coins");
                }),
                (20, () =>
                {
                    int loss = -AddCoins(clientId, -Mathf.Max(1, player.Coins / 2));
                    BannerRpc("BOMBACLAT!", $"{player.Name} wird gesprengt: -{loss} Coins");
                }),
            };
            if (victimsWithCoins.Count > 0)
            {
                options.Add((25, () =>
                {
                    var victim = victimsWithCoins[Random.Range(0, victimsWithCoins.Count)];
                    int stolen = -AddCoins(victim.ClientId, -5);
                    AddCoins(clientId, stolen);
                    BannerRpc("TASCHENDIEB!", $"{player.Name} klaut {victim.Name} {stolen} Coins");
                }));
                options.Add((10, () =>
                {
                    int total = 0;
                    foreach (var victim in victimsWithCoins) total -= AddCoins(victim.ClientId, -2);
                    AddCoins(clientId, total);
                    BannerRpc("CHAOS-STEUER!", $"Alle zahlen {player.Name} 2 Coins (+{total})");
                }));
            }
            if (others.Count > 0)
            {
                options.Add((20, () =>
                {
                    var target = others[Random.Range(0, others.Count)];
                    int a = IndexOf(clientId), b = IndexOf(target.ClientId);
                    var pa = Players[a]; var pb = Players[b];
                    (pa.Tile, pb.Tile) = (pb.Tile, pa.Tile);
                    Players[a] = pa; Players[b] = pb;
                    BannerRpc("PLATZTAUSCH!", $"{player.Name} tauscht mit {target.Name}");
                }));
            }
            if (victimsWithCrowns.Count > 0)
            {
                options.Add((5, () =>
                {
                    var victim = victimsWithCrowns[Random.Range(0, victimsWithCrowns.Count)];
                    AddCrowns(victim.ClientId, -1);
                    AddCrowns(clientId, 1);
                    BannerRpc("KRONENRAUB!", $"{player.Name} klaut {victim.Name} eine Krone!");
                }));
            }

            int totalWeight = 0;
            foreach (var o in options) totalWeight += o.weight;
            int pick = Random.Range(0, totalWeight);
            foreach (var o in options)
            {
                if (pick < o.weight) { o.run(); return; }
                pick -= o.weight;
            }
        }

        void AdvanceTurn()
        {
            int next = CurrentTurn.Value + 1;
            if (next >= Players.Count)
            {
                if (Round.Value >= MaxRounds.Value)
                {
                    EndGame();
                    return;
                }
                Round.Value++;
                next = 0;
                BannerRpc($"RUNDE {Round.Value}", Round.Value == MaxRounds.Value ? "Letzte Runde!" : $"von {MaxRounds.Value}");
            }
            CurrentTurn.Value = next;
            Stage.Value = TurnStage.WaitingForRoll;
        }

        void EndGame()
        {
            Phase.Value = GamePhase.GameOver;
            Stage.Value = TurnStage.WaitingForRoll;
            CrownTile.Value = -1;

            PlayerData best = default;
            bool any = false;
            foreach (var p in Players)
            {
                if (!any || p.Crowns > best.Crowns || (p.Crowns == best.Crowns && p.Coins > best.Coins)) best = p;
                any = true;
            }
            if (any) BannerRpc($"{best.Name} GEWINNT!", $"{best.Crowns} Kronen, {best.Coins} Coins");
        }

        void OnClientDisconnected(ulong clientId)
        {
            int index = IndexOf(clientId);
            if (index < 0) return;
            string playerName = Players[index].Name.ToString();
            bool wasCurrent = index == CurrentTurn.Value;
            Players.RemoveAt(index);
            FeedRpc($"{playerName} hat das Spiel verlassen.");

            if (Phase.Value != GamePhase.Playing) return;
            if (Players.Count == 0)
            {
                Phase.Value = GamePhase.Lobby;
                return;
            }
            if (wasCurrent)
            {
                if (m_Turn != null) { StopCoroutine(m_Turn); m_Turn = null; }
                // The next player slid into this index; let AdvanceTurn handle round wrap-around.
                CurrentTurn.Value = index - 1;
                AdvanceTurn();
            }
            else if (index < CurrentTurn.Value)
            {
                CurrentTurn.Value--;
            }
        }

        // ------------------------------------------------------------------ helpers (server)

        /// <summary>Adds (or removes) coins, never going below zero. Returns the real change.</summary>
        int AddCoins(ulong clientId, int delta)
        {
            int index = IndexOf(clientId);
            if (index < 0) return 0;
            var p = Players[index];
            int before = p.Coins;
            p.Coins = Mathf.Max(0, p.Coins + delta);
            Players[index] = p;
            int change = p.Coins - before;
            if (change != 0) CoinsRpc(clientId, change);
            return change;
        }

        void AddCrowns(ulong clientId, int delta)
        {
            int index = IndexOf(clientId);
            if (index < 0) return;
            var p = Players[index];
            p.Crowns = Mathf.Max(0, p.Crowns + delta);
            Players[index] = p;
            CrownRpc(clientId, delta);
        }

        int FreeColor()
        {
            for (int c = 0; c < Palette.Players.Length; c++)
            {
                bool used = false;
                foreach (var p in Players) used |= p.ColorIndex == c;
                if (!used) return c;
            }
            return 0;
        }

        /// <summary>A blue tile reasonably far away from where the crown was just bought.</summary>
        static int PickCrownTile(int awayFrom)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                int tile = Random.Range(1, BoardLayout.TileCount);
                int distance = BoardLayout.Wrap(tile - awayFrom);
                if (BoardLayout.Type(tile) == TileType.Blue && distance >= 8 && distance <= BoardLayout.TileCount - 4) return tile;
            }
            return BoardLayout.Wrap(awayFrom + BoardLayout.TileCount / 2);
        }

        // ------------------------------------------------------------------ cosmetic broadcasts

        [Rpc(SendTo.ClientsAndHost)]
        void DiceRpc(ulong clientId, int value) => DiceRolled?.Invoke(clientId, value, DiceTime);

        [Rpc(SendTo.ClientsAndHost)]
        void MoveRpc(ulong clientId, int fromTile, int steps) => MoveStarted?.Invoke(clientId, fromTile, steps);

        [Rpc(SendTo.ClientsAndHost)]
        void CoinsRpc(ulong clientId, int delta) => CoinsChanged?.Invoke(clientId, delta);

        [Rpc(SendTo.ClientsAndHost)]
        void CrownRpc(ulong clientId, int delta) => CrownsChanged?.Invoke(clientId, delta);

        [Rpc(SendTo.ClientsAndHost)]
        void FeedRpc(string text) => FeedPosted?.Invoke(text);

        [Rpc(SendTo.ClientsAndHost)]
        void BannerRpc(string title, string subtitle) => BannerShown?.Invoke(title, subtitle);
    }
}

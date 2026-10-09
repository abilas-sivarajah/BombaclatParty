using System;
using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Bombaclat.Tests
{
    public class BoardLayoutTests
    {
        [Test]
        public void TilesNeverOverlap()
        {
            float minGap = BoardLayout.TileRadius * 1.12f * 2f; // tile base diameter
            for (int a = 0; a < BoardLayout.TileCount; a++)
            for (int b = a + 1; b < BoardLayout.TileCount; b++)
            {
                float d = Vector3.Distance(BoardLayout.Position(a), BoardLayout.Position(b));
                Assert.Greater(d, minGap, $"Tiles {a} and {b} overlap");
            }
        }

        [Test]
        public void StartTileIsFirstAndBoardHasEveryType()
        {
            Assert.AreEqual(TileType.Start, BoardLayout.Type(0));
            foreach (TileType type in Enum.GetValues(typeof(TileType)))
            {
                int count = 0;
                for (int i = 0; i < BoardLayout.TileCount; i++) if (BoardLayout.Type(i) == type) count++;
                Assert.Greater(count, 0, $"No {type} tiles on the board");
            }
        }
    }

    public class BoardGameFlowTests
    {
        [UnityTest]
        public IEnumerator SoloGame_HostStartRollAndFinish()
        {
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            ConnectionManager.Instance.HostLocal();
            yield return WaitUntil(() => BoardGameManager.Instance != null && BoardGameManager.Instance.IsSpawned &&
                                         BoardGameManager.Instance.Players.Count == 1, 5f, "host registered as player");

            var gm = BoardGameManager.Instance;
            Assert.AreEqual(GamePhase.Lobby, gm.Phase.Value);
            Assert.AreEqual(BoardGameManager.StartCoins, gm.Players[0].Coins);

            gm.StartGameRpc(1);
            yield return WaitUntil(() => gm.Phase.Value == GamePhase.Playing, 2f, "game started");
            Assert.IsTrue(gm.IsMyTurn, "solo player should be on turn");
            Assert.GreaterOrEqual(gm.CrownTile.Value, 1, "crown placed on the board");

            gm.RollDiceRpc();
            yield return WaitUntil(() => gm.Stage.Value != TurnStage.WaitingForRoll, 2f, "roll accepted");
            yield return WaitUntil(() => gm.Phase.Value == GamePhase.GameOver, 20f, "one-round game finished");

            var player = gm.Players[0];
            Assert.That(player.Tile, Is.InRange(1, 10), "pawn moved by the die roll");
            Assert.GreaterOrEqual(player.Coins, 0);

            gm.BackToLobbyRpc();
            yield return WaitUntil(() => gm.Phase.Value == GamePhase.Lobby, 2f, "back to lobby");
            Assert.AreEqual(0, gm.Players[0].Tile);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.Shutdown();
                yield return null;
                UnityEngine.Object.Destroy(nm.gameObject);
            }
            yield return null;
        }

        static IEnumerator WaitUntil(Func<bool> condition, float timeout, string what)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(condition(), $"Timed out waiting for: {what}");
        }
    }
}

using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Bombaclat
{
    /// <summary>
    /// Gets players into the same game: online through a Multiplayer Services session with a
    /// join code (no port forwarding needed), or locally by IP for testing and LAN parties.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class ConnectionManager : MonoBehaviour
    {
        public static ConnectionManager Instance { get; private set; }

        public const ushort LocalPort = 7777;

        public string Status { get; private set; } = "";
        public bool Busy { get; private set; }
        public string JoinCode => m_Session?.Code;
        public bool CloudLinked => !string.IsNullOrEmpty(Application.cloudProjectId);

        NetworkManager m_Network;
        ISession m_Session;
        bool m_Leaving;

        void Awake()
        {
            Instance = this;
            m_Network = GetComponent<NetworkManager>();
        }

        void Start() => m_Network.OnClientStopped += OnClientStopped;

        void OnDestroy()
        {
            if (m_Network != null) m_Network.OnClientStopped -= OnClientStopped;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ online

        public async void HostOnline()
        {
            if (Busy) return;
            Busy = true;
            Status = "Erstelle Lobby...";
            try
            {
                await EnsureSignedIn();
                var options = new SessionOptions { MaxPlayers = BoardGameManager.MaxPlayers, IsPrivate = true }.WithRelayNetwork();
                m_Session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Watch(m_Session);
                Status = "";
            }
            catch (Exception e)
            {
                m_Session = null;
                Status = Friendly(e);
                Debug.LogException(e);
            }
            finally { Busy = false; }
        }

        public async void JoinOnline(string code)
        {
            if (Busy) return;
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0) { Status = "Gib erst einen Join-Code ein."; return; }

            Busy = true;
            Status = "Trete bei...";
            try
            {
                await EnsureSignedIn();
                m_Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                Watch(m_Session);
                Status = "";
            }
            catch (Exception e)
            {
                m_Session = null;
                Status = Friendly(e);
                Debug.LogException(e);
            }
            finally { Busy = false; }
        }

        async Task EnsureSignedIn()
        {
            if (!CloudLinked)
                throw new InvalidOperationException("Projekt ist noch nicht mit Unity Cloud verknüpft.");

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
#if UNITY_EDITOR
                // Each Multiplayer Play Mode instance runs from its own folder: give each one its
                // own anonymous identity, otherwise they'd all be the same player.
                options.SetProfile($"editor{StableHash(Application.dataPath):x8}");
#endif
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        void Watch(ISession session)
        {
            session.RemovedFromSession += OnSessionGone;
            session.Deleted += OnSessionGone;
        }

        void OnSessionGone()
        {
            if (m_Session == null) return;
            m_Session = null;
            if (!m_Leaving) Status = "Die Lobby wurde geschlossen.";
        }

        // ------------------------------------------------------------------ local / LAN

        public void HostLocal()
        {
            if (Busy || m_Network.IsListening) return;
            Transport.SetConnectionData("127.0.0.1", LocalPort, "0.0.0.0");
            Status = m_Network.StartHost() ? "" : $"Konnte nicht hosten (Port {LocalPort} belegt?)";
        }

        public void JoinLocal(string address)
        {
            if (Busy || m_Network.IsListening) return;
            address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            Transport.SetConnectionData(address, LocalPort);
            Status = m_Network.StartClient() ? "" : "Konnte nicht verbinden.";
        }

        UnityTransport Transport => (UnityTransport)m_Network.NetworkConfig.NetworkTransport;

        // ------------------------------------------------------------------ leaving

        public async void Leave()
        {
            m_Leaving = true;
            try
            {
                var session = m_Session;
                m_Session = null;
                if (session != null)
                {
                    try { await session.LeaveAsync(); }
                    catch (Exception e) { Debug.LogWarning($"Leaving session failed: {e.Message}"); }
                }
                if (m_Network.IsListening) m_Network.Shutdown();
                Status = "";
            }
            finally { m_Leaving = false; }
        }

        void OnClientStopped(bool wasHost)
        {
            if (m_Leaving) return;
            var reason = m_Network.DisconnectReason;
            Status = wasHost ? "" : string.IsNullOrEmpty(reason) ? "Verbindung zum Host beendet." : reason;
            if (m_Session != null)
            {
                var session = m_Session;
                m_Session = null;
                _ = session.LeaveAsync();
            }
        }

        // ------------------------------------------------------------------ helpers

        string Friendly(Exception e)
        {
            if (!CloudLinked)
                return "Online braucht ein verknüpftes Unity-Cloud-Projekt. Zum Testen: 'Lokal hosten'.";
            if (e is SessionException se)
                return $"Online-Fehler ({se.Error}): {se.Message}";
            return $"Online-Fehler: {e.Message}";
        }

        static uint StableHash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text) hash = (hash ^ c) * 16777619;
            return hash;
        }
    }
}

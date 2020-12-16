﻿using NativeWebSocket;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class Game : MonoBehaviour {
    public static Game i { private set; get; }

    public List<Player> Players { get; set; } = new List<Player>();
    public List<Bird> Birds { get; set; } = new List<Bird>();
    public Player LocalPlayer { get; private set; }

    public List<int> deadBirds = new List<int>();

    public GameObject birdPrefab;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject bowlPrefab;

    WebSocket websocket;
    NetControllers controllers = new NetControllers();
    E_ConnectionState connectionState;
    Dictionary<int, int> scores = new Dictionary<int, int>();
    Dictionary<int, Bowl> usedBowls = new Dictionary<int, Bowl>();
    List<Bowl> pooledBowls = new List<Bowl>();
    Vector3 bowlSpawnPoint = new Vector3(12.23f, 0.005f, 12.13f);

    enum E_ConnectionState {CONNECTING, ERROR, OK};

    List<string> names = new List<string>();

    void Awake() {
        i = this;

        ConnectSocket();
    }

    private void Start() {

        names.Add("Pantoufle");
        names.Add("Luna");
        names.Add("Oscar");
        names.Add("Felix");
        names.Add("Frisbee");
        names.Add("Princess");
        names.Add("Chaussette");
        names.Add("Tina");
        names.Add("Mystique");
        names.Add("Maki");
        names.Add("Sushi");
        names.Add("Glycine");
        names.Add("Kheops");
        names.Add("Mimine");
        names.Add("Nala");
        names.Add("Gnougnou");
        names.Add("Jambon");
        names.Add("Gigot");
        names.Add("Mittens");
        names.Add("Baguera");
        names.Add("Cleopatre");
        names.Add("Frimousse");
        names.Add("Patata");
        names.Add("Mimi");
        names.Add("Elysion");
        names.Add("Diva");

#if UNITY_EDITOR
        int i = 0;
        Debug.Log(
            Newtonsoft.Json.JsonConvert.SerializeObject(BirdSpot.spots.Select(o => new NetControllers.DumpBirdSpot() {
                id = ++i,
                position = new NetControllers.Position() {
                    x = o.transform.position.x,
                    y = o.transform.position.y,
                    z = o.transform.position.z
                },
                isSafe = o.isRestingSpot
            }).ToList())
        );
#endif
    }

    async void ConnectSocket() {
        while (true) {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying) {
                break;
            }
#endif

            if (connectionState == E_ConnectionState.OK) {
                await Task.Delay(1000);
                continue;
            }

            await InitWebSock();

        }
    }

    async Task InitWebSock() {
        connectionState = E_ConnectionState.CONNECTING;
        websocket = new WebSocket("ws://racksberry:1234");

        websocket.OnOpen += () => {
            connectionState = E_ConnectionState.OK;
        };

        websocket.OnError += (e) => {
            connectionState = E_ConnectionState.ERROR;
            Debug.Log("Error! " + e);
        };

        websocket.OnClose += (e) => {
            connectionState = E_ConnectionState.ERROR;
            Debug.Log("Connection closed! " + e);
        };

        websocket.OnMessage += (bytes) => {
            try {
                string message = System.Text.Encoding.UTF8.GetString(bytes);
                string controller = message.Substring(0, 3);
                message = message.Substring(3);

                Debug.Log(controller + " => " + message);

                if (controllers.ContainsKey(controller)) {
                    controllers[controller].Invoke(websocket, message);
                }
                else {
                    Debug.LogWarning("Received junk controller " + controller + " with message " + message);
                }
            }
            catch (System.Exception e) {
                Debug.LogError(e);
            }


            // Debug.Log("OnMessage! " + message);
        };

        await websocket.Connect();
    }

    public Player SpawnPlayer(int id, Vector3 position, Quaternion rotation, bool isLocal = false) {
        var player = Object.Instantiate(playerPrefab).GetComponent<Player>();
        player.id = id;
        player.transform.position = position;
        player.transform.rotation = rotation;
        player.IsLocal = isLocal;

        if (player.IsLocal) {
            LocalPlayer = player;
        }

        Players.Add(player);

        Bowl bowl;
        if (pooledBowls.Count > 0) {
            bowl = pooledBowls[0];
            pooledBowls.RemoveAt(0);
        }
        else {
            bowl = Object.Instantiate(bowlPrefab).GetComponent<Bowl>();
        }

        bowl.playerId = player.id;
        bowl.UpdateBowl();
        bowl.UpdateSize();
        bowl.transform.position = bowlSpawnPoint - Vector3.right * usedBowls.Count;

        usedBowls[player.id] = bowl;

        return player;
    }

    public void DestroyAllPlayers() {
        Debug.Log("Destroying all players");
        foreach (var player in Players) {
            Destroy(player.gameObject);
        }

        Players.Clear();
    }

    public Player GetPlayerById(int id) {
        return Players.Find(o => o.id == id);
    }

    public void SendMyPosition(Vector3 position, Quaternion rotation, PlayerMovement infos) {
        websocket.SendText(NetControllers.PROTOCOL_MOVESTATE + Newtonsoft.Json.JsonConvert.SerializeObject(new NetControllers.PlayerMove() {
            id = LocalPlayer.id,
            isJumping = infos.IsJumping,
            isRunning = infos.IsSprinting,
            isSneaking = infos.IsCrouching,
            position = new NetControllers.Position() {
                x = position.x,
                y = position.y,
                z = position.z
            },
            rotation = rotation.x + " " + rotation.y + " " + rotation.z + " " + rotation.w
        }));
    }

    public void SendCaughtBird(int birdId) {
        websocket.SendText(NetControllers.PROTOCOL_CATCH + birdId);
    }

    public void SetScore(int clientId, int score) {
        if (GetPlayerById(clientId) != null) {
            if (!scores.ContainsKey(clientId) || scores[clientId] < score) {
                if (usedBowls.ContainsKey(clientId)) {
                    usedBowls[clientId].UpdateSize();
                }
            }

            scores[clientId] = score;
        }
    }

    public int GetScore(int clientId) {
        return scores.ContainsKey(clientId) ? scores[clientId] : 0;
    }

    public string GetNameForId(int clientId) {
        return names[clientId % names.Count];
    }

    void Update() {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket.DispatchMessageQueue();
#endif
        UpdateBowls();
    }

    public void KillPlayer(int id) {
        var player = Players.Find(o => o.id == id);
        if (player) {
            Destroy(player.gameObject);

            KillBowl(id);
        }
    }

    public void KillBowls() {
        foreach(var bowl in usedBowls.Values) {
            KillBowl(bowl.playerId);
        }

        usedBowls.Clear();
    }

    void KillBowl(int playerId) {
        if (usedBowls.ContainsKey(playerId)){
            var bowl = usedBowls[playerId];
            usedBowls.Remove(playerId);
            bowl.gameObject.SetActive(false);
            pooledBowls.Add(bowl);
        }
    }

    void UpdateBowls() {
        foreach(var player in Game.i.Players) {

        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameManger : MonoBehaviour
{
    public static GameManger instance;

    public bool InGame = false;
    public int playerId = -1;
    public int OpponentId = -1;
    public bool IsOnline;
    public int PlayerType;
    public bool MyReadyStatus;
    public bool OpponentReadyStatus;

    private TcpClient client = new TcpClient();
    private NetworkStream stream;
    private string serverIP = "47.108.170.108";
    private int serverPort = 5555;


    [SerializeField] private GameObject InvitePopupPre;    //邀请弹窗 ,弹窗type1：2按钮，1文本（按钮功能不一定一样）
    private GameObject currentinvitePopup;
    [SerializeField] private GameObject RejectPopupPre;  //拒绝弹窗 ,弹窗type2：1按钮，1文本（按钮功能只负责销毁，弹窗类似于通知）
    private GameObject currentRejectPopup;

    // 玩家状态缓存
    private Dictionary<int, int> PlayersIdToStatus = new Dictionary<int, int>();
    private string recvCache = "";//收到的消息缓存

    // 事件：展示弹窗1，展示弹窗2，加入房间，交换执子类型，刷新玩家状态
    public Action<int> JoinRoomSuccess;
    public Action ExchangeSuccess;
    public Action<Dictionary<int, int>> OnPlayersStatusUpdated;
    public Action OnReadyStatusChanged;
    public Action<int,Vector3,Vector3> OnLuoziRecived;
    public Action OnHuiQiSuccess;


    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #region 连接服务器
    public void Logic()
    {
        if (InGame) return;
        Debug.Log("正在连接服务器...");
        StartCoroutine(ConnectToServer());
    }

    private IEnumerator ConnectToServer()
    {
        var result = client.BeginConnect(serverIP, serverPort, null, null);
        yield return new WaitUntil(() => result.IsCompleted);

        if (!client.Connected)
        {
            Debug.LogError("服务器连接失败");
            yield break;
        }

        stream = client.GetStream();
        byte[] buffer = new byte[256];
        int length = stream.Read(buffer, 0, buffer.Length);
        string response = Encoding.UTF8.GetString(buffer, 0, length).Trim();

        if (response.StartsWith("ID="))
        {
            string idStr = response.Substring(3);
            if (int.TryParse(idStr, out playerId))
            {
                Debug.Log($"连接成功，玩家ID：{playerId}");
                IsOnline = true;
                SceneManger.instance.SwitchUI("Room");

                // 连接成功后启动监听
                StartListeningServer();
            }
            else
            {
                Debug.LogError("ID解析失败：" + response);
            }
        }
        else if (response == "SERVER_FULL")
        {
            Debug.LogWarning("服务器已满，无法进入");
        }
        else
        {
            Debug.LogError("未知服务器响应：" + response);
        }
    }
    #endregion

    #region 游戏开始/离线模式
    private void StartGame()
    {
        if (InGame) return;
        InGame = true;

        SceneManger.instance.SwitchUI("Game");
    }

    public void OfflineStart()
    {
        if (InGame) return;

        InGame = true;
        IsOnline = false;
        PlayerType = 0;

        SceneManger.instance.SwitchUI("Game");
    }
    #endregion

    #region 获取玩家状态
    public void RequestPlayersStatus()
    {
        if (stream == null || !client.Connected) return;

        byte[] msg = Encoding.UTF8.GetBytes("STATUS_ALL?\n");
        stream.Write(msg, 0, msg.Length);
    }

    #endregion

    #region 监听
    public void StartListeningServer()
    {
        if (IsOnline && client != null && client.Connected)
            StartCoroutine(ListenServerCoroutine());
    }

    private IEnumerator ListenServerCoroutine()
    {
        while (IsOnline && client != null && client.Connected)
        {
            if (stream.DataAvailable)
            {
                byte[] buffer = new byte[1024];
                int len = stream.Read(buffer, 0, buffer.Length);

                string chunk = Encoding.UTF8.GetString(buffer, 0, len);
                recvCache += chunk;

                //  按 \n 拆消息
                while (recvCache.Contains("\n"))
                {
                    int index = recvCache.IndexOf('\n');
                    string oneMsg = recvCache.Substring(0, index).Trim();
                    recvCache = recvCache.Substring(index + 1);

                    HandleServerMessage(oneMsg);
                }
            }

            yield return null; 
        }
    }

    private void HandleServerMessage(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;

        // STATUS_ALL
        if (msg.StartsWith("STATUS_ALL="))
        {
            Dictionary<int, int> result = new Dictionary<int, int>();

            string content = msg.Substring("STATUS_ALL=".Length);
            string[] pairs = content.Split(',');

            foreach (string p in pairs)
            {
                if (!p.Contains(":")) continue;

                string[] kv = p.Split(':');
                int id = int.Parse(kv[0]);
                int status = int.Parse(kv[1]);

                result[id] = status;
            }

            PlayersIdToStatus = result;
            OnPlayersStatusUpdated?.Invoke(result);
            return;
        }

        //邀请
        if (msg.StartsWith("INVITED:"))
        {
            if (int.TryParse(msg.Substring(8), out int inviterId))
                ShowPopupType1(inviterId, "invite you", 1);
            return;
        }
        
        //邀请被拒绝
        if (msg.StartsWith("INVITE_REJECTED:"))
        {
            if (int.TryParse(msg.Substring(16), out int rejectId))
                ShowPopupType2(rejectId, "rejected your Invitation");
            return;
        }

        //房间满
        if (msg.StartsWith("ROOM_FULL:"))
        {
            if (int.TryParse(msg.Substring(10), out int otherId))
                ShowPopupType2(otherId, "'s room is full");
            return;
        }

        //进入房间
        if (msg.StartsWith("JoinRoom:"))
        {
            string[] parts = msg.Substring(9).Split(',');
            int opponentId = int.Parse(parts[0]);

            bool isBlack = false;
            foreach (string p in parts)
                if (p.StartsWith("isBlack:"))
                    isBlack = p.Substring(8) == "1";

            OpponentId = opponentId;
            PlayerType = isBlack ? 1 : 2;
            JoinRoomSuccess?.Invoke(opponentId);
            return;
        }

        //交换执子请求
        if (msg.StartsWith("EXCHANGE_REQUEST:"))
        {
            if (int.TryParse(msg.Substring(17), out int opponentId))
                ShowPopupType1(opponentId, "want to exchange stone type", 2);
            return;
        }

        //交换执子成功
        if (msg == "Exchange_Success")
        {
            PlayerType = PlayerType == 1 ? 2 : 1;
            ExchangeSuccess?.Invoke();
            return;
        }

        //准备状态
        if (msg.StartsWith("READY_STATUS:"))
        {
            // READY_STATUS:1:1,3:0
            string content = msg.Substring("READY_STATUS:".Length);
            string[] pairs = content.Split(',');

            foreach (string p in pairs)
            {
                string[] kv = p.Split(':');
                int id = int.Parse(kv[0]);
                bool ready = kv[1] == "1";

                if (id == playerId)
                {
                    MyReadyStatus = ready;
                }
                else
                {
                    OpponentReadyStatus = ready;
                }
            }

            Debug.Log($"准备状态更新：我={MyReadyStatus}, 对手={OpponentReadyStatus}");

            OnReadyStatusChanged?.Invoke();
            return;
        }

        //游戏开始请求
        if (msg.StartsWith("GAME_START_RESULT:"))
        {
            string resultStr = msg.Substring("GAME_START_RESULT:".Length).Trim();

            if (resultStr == "1")
            {
                Debug.Log("服务端确认：可以开始游戏");
                StartGame();
            }
            else
            {
                Debug.Log("服务端拒绝：还有玩家未准备");
                ShowPopupType2(OpponentId, " not ready yet");
            }

            return;
        }

        //对方落子信息
        if (msg.StartsWith("LUOZI:"))
        {
            // 格式: LUOZI:playerId,stoneType,px,py,pz,nx,ny,nz
            try
            {
                string content = msg.Substring("LUOZI:".Length);
                string[] parts = content.Split(',');

                if (parts.Length >= 8)
                {
                    int playerId = int.Parse(parts[0]);
                    int stoneType = int.Parse(parts[1]); // 可选，如果需要
                    Vector3 point = new Vector3(
                        float.Parse(parts[2]),
                        float.Parse(parts[3]),
                        float.Parse(parts[4])
                    );
                    Vector3 normal = new Vector3(
                        float.Parse(parts[5]),
                        float.Parse(parts[6]),
                        float.Parse(parts[7])
                    );

                    // 回调通知
                    OnLuoziRecived?.Invoke(stoneType, point, normal);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("解析LUOZI消息失败: " + e);
            }
        }

        // 收到悔棋请求
        if (msg.StartsWith("HUIQI_REQUEST:"))
        {
            if (int.TryParse(msg.Substring("HUIQI_REQUEST:".Length), out int opponentId))
            {
                // 弹窗提示对方请求悔棋，type=3表示悔棋请求类型
                ShowPopupType1(opponentId, "requests undo move", 3);
            }
            return;
        }

        // 接收悔棋被拒绝
        if (msg == "HUIQI_REJECTED")
        {
            ShowPopupType2(OpponentId, "your undo request was rejected"); 
            return;
        }

        // 悔棋成功
        if (msg == "HUIQI_SUCCESS")
        {
            OnHuiQiSuccess?.Invoke(); 
            return;
        }
    }
    #endregion

    #region 邀请相关
    public void SendInvite(int targetPlayerId)
    {
        if (!IsOnline) return;

        string msg = $"INVITE:{playerId}->{targetPlayerId}\n";
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);

        Debug.Log($"向玩家 {targetPlayerId} 发送邀请（自己ID={playerId}）");
    }


    public void AcceptInvite(int inviterId)
    {
        OpponentId = inviterId;
        Debug.Log($"接受玩家 {inviterId} 的邀请，准备开始游戏");

        string msg = $"ACCEPT_INVITE:{playerId}->{inviterId}\n";
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);
    }

    public void RejectInvite(int inviterId)
    {
        Debug.Log($"拒绝玩家 {inviterId} 的邀请");

        string msg = $"REJECT_INVITE:{playerId}->{inviterId}\n";
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);
    }

    #endregion

    #region 交换执子相关
    public void SendExchange()
    {
        if (!IsOnline) return;

        string msg = $"EXCHANGE:{playerId}\n";
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);

        Debug.Log($"请求交换棋子类型，通知房间对手");
    }

    public void AcceptExchange(int Id)
    {
        OpponentId = Id;
        Debug.Log($"接受玩家 {Id} 的交换棋子类型要求");

        string msg = $"ACCEPT_Exchange:{Id}\n";
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);
    }
    #endregion

    #region 准备相关
    public void SendReady()
    {
        if (!IsOnline) return;

        string msg = $"ChangeReadyStatus:{playerId}\n";
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);

        Debug.Log($"切换准备状态");
    }

    #endregion

    public void GameStart()
    {
        if (!IsOnline) return;

        string msg = $"RequestSatrt:{playerId}\n";
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);

        Debug.Log($"请求开始游戏");
    }

    public void SendLuoziInfo(int stoneType, RaycastHit hit)
    {
        Vector3 p = hit.point;
        Vector3 n = hit.normal;

        // 格式: LUOZI:playerId,stoneType,px,py,pz,nx,ny,nz
        string msg = $"LUOZI:{playerId},{stoneType},{p.x},{p.y},{p.z},{n.x},{n.y},{n.z}\n";
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);

        Debug.Log("发送落子信息: " + msg);
    }

    #region  悔棋
    public void SendHuiQi()
    {
        if (!IsOnline) return;

        string msg = $"RequestHuiQi:{playerId}\n";
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);

        Debug.Log($"请求悔棋");
    }

    public void AcceptHuiQi(int Id)
    {
        if (!IsOnline) return;

        string msg = $"ACCEPT_HUIQI:{Id}\n";  
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);

        Debug.Log($"同意玩家 {Id} 的悔棋请求");
    }

    public void RejectHuiQi(int Id)
    {
        if (!IsOnline) return;

        string msg = $"REJECT_HUIQI:{Id}\n"; 
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);

        Debug.Log($"拒绝玩家 {Id} 的悔棋请求");
    }
    #endregion


    #region  弹窗相关

    //id，显示文本，弹窗的功能（弹窗中按钮需要绑定不同函数）
    private void ShowPopupType1(int Id, string info, int type)
    {
        if (currentinvitePopup != null) return; // 避免重复弹窗

        // 1. 实例化预制体
        currentinvitePopup = Instantiate(InvitePopupPre, transform); // 可以指定父物体为 Canvas

        // 2. 获取按钮（使用 GetComponentInChildren）
        Button[] buttons = currentinvitePopup.GetComponentsInChildren<Button>();
        Button acceptBtn = null;
        Button rejectBtn = null;
        TMP_Text[] texts = currentinvitePopup.GetComponentsInChildren<TMP_Text>();

        foreach (Button btn in buttons)
        {
            if (btn.name == "AcceptButton") acceptBtn = btn;
            else if (btn.name == "RejectButton") rejectBtn = btn;
        }

        //按钮1，按钮2各有一个，纯文本在第三个
        texts[2].text = Id + info.ToString();

        // 3. 动态绑定函数
        if (type == 1)  //邀请加入房间，
        {
            acceptBtn.onClick.AddListener(() => AcceptInvite(Id));
            rejectBtn.onClick.AddListener(() => RejectInvite(Id));
            acceptBtn.onClick.AddListener(() => DestroyPopup());
            rejectBtn.onClick.AddListener(() => DestroyPopup());
        }
        else if (type == 2)   //申请换位
        {
            acceptBtn.onClick.AddListener(() => AcceptExchange(Id));
            acceptBtn.onClick.AddListener(() => DestroyPopup());
            rejectBtn.onClick.AddListener(() => DestroyPopup());
        }
        else if (type == 3) //申请悔棋
        {
            acceptBtn.onClick.AddListener(() => AcceptHuiQi(Id));
            rejectBtn.onClick.AddListener(() => RejectHuiQi(Id));
            acceptBtn.onClick.AddListener(() => DestroyPopup());
            rejectBtn.onClick.AddListener(() => DestroyPopup());
        }
    }


    //id，显示文本（虽然有一个按钮，但是绑定同一个销毁函数，弹窗功能类似于通知）
    private void ShowPopupType2(int Id, string info)
    {
        if (currentRejectPopup != null) return;

        currentinvitePopup = Instantiate(RejectPopupPre, transform);

        Button[] buttons = currentinvitePopup.GetComponentsInChildren<Button>();
        TMP_Text[] texts = currentinvitePopup.GetComponentsInChildren<TMP_Text>();

        buttons[0].onClick.AddListener(() => DestroyPopup());
        texts[1].text = Id + info.ToString();
    }

    public void DestroyPopup()
    {
        if (currentinvitePopup != null)
        {
            Destroy(currentinvitePopup);
            currentinvitePopup = null;
        }
        if (currentRejectPopup != null)
        {
            Destroy(currentRejectPopup);
            currentRejectPopup = null;
        }
    }
    #endregion
}

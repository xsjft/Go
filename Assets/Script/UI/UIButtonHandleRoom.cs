using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class UIButtonHandleRoom : MonoBehaviour
{
    [SerializeField] private GameObject List;  //列表
    [SerializeField] private GameObject PlayerInviteItem; //列表中邀请样式
    [SerializeField] private GameObject PlayerStatusItem; //列表中状态样式
    [SerializeField] private TMP_Text MyIdText;        //id文本
    [SerializeField] private TMP_Text OpponentIdText;
    [SerializeField] private TMP_Text MyTypeText;      //执子文本
    [SerializeField] private TMP_Text OpponentTypeText; 
    [SerializeField] private Button StartButton;  //开始，交换，准备,退出房间按钮
    [SerializeField] private Button ExchangeButton;
    [SerializeField] private Button ReadyButton;
    [SerializeField] private Button ExitRoomButton;
    [SerializeField] private TMP_Text MyReadyText;     //准备文本
    [SerializeField] private TMP_Text OpponentReadyText;
    //玩家id与对应的状态，从服务器获取
    private Dictionary<int,int> PlayersIdToStatus = new Dictionary<int,int>(); //0表示可邀请，1表示房间中，2,表示游戏中
    
    //与玩家对应的列表组件
    private Dictionary<int,GameObject> PlayerIdToItem = new Dictionary<int,GameObject>();


    private void Awake()
    {

        StartCoroutine(AutoRefreshStatus());

        //为gamemanger的监听事件初始化
        GameManger.instance.JoinRoomSuccess += JoinRoom;
        GameManger.instance.ExchangeSuccess += ExchangeStoneType;
        GameManger.instance.OnPlayersStatusUpdated += HandleStatusUpdate;
        GameManger.instance.OnReadyStatusChanged += ChangeReadyStatus;

        //初始化页面信息，自己的id以及几个按钮失效
        MyIdText.text = GameManger.instance.playerId.ToString();
        StartButton.interactable = false;
        ExchangeButton.interactable = false;
        ReadyButton.interactable= false;
        ExitRoomButton.interactable = false;
    }

    private void Start()
    {
        GameManger.instance.SendCheckRoomState();
    }

    #region 玩家状态列表相关

    #region 请求最新玩家信息及处理
    IEnumerator AutoRefreshStatus()
    {
        while (true)
        {
            GameManger.instance.RequestPlayersStatus();
            yield return new WaitForSeconds(3f);
        }
    }

    private void HandleStatusUpdate(Dictionary<int, int> newStatus)
    {
        if (newStatus == null) return;

        if (!IsSame(PlayersIdToStatus, newStatus))
        {
            RenovateListItems(newStatus);
            PlayersIdToStatus = new Dictionary<int, int>(newStatus);
        }
    }
    #endregion

    //得到最新的数据之后，根据最新数据对现有的进行增删改
    private void RenovateListItems(Dictionary<int,int> newest)
    {
        // 需要删除的ID
        List<int> toRemove = new List<int>();

        foreach (var kv in PlayersIdToStatus)
        {
            int id = kv.Key;

            if (!newest.ContainsKey(id) &&id != GameManger.instance.playerId)
            {
                toRemove.Add(id);
            }
        }

        // 删除
        foreach (int id in toRemove)
        {
            RemoveItem(id);
        }

        // 新增或修改
        foreach (var kv in newest)
        {
            int id = kv.Key;
            int type = kv.Value;

            // 没有,新增
            if (!PlayersIdToStatus.ContainsKey(id))
            {
                if(id!=GameManger.instance.playerId)
                {
                    AddItem(id, type);
                }
            }
            // 变化，修改
            else if (PlayersIdToStatus[id] != type)
            {
                if (id != GameManger.instance.playerId)
                {
                    ReviseItem(id, type);
                }
            }
        }
    }
    bool IsSame(Dictionary<int, int> a, Dictionary<int, int> b)
    {
        if (a.Count != b.Count) return false;

        foreach (var kv in a)
        {
            if (!b.ContainsKey(kv.Key)) return false;
            if (b[kv.Key] != kv.Value) return false;
        }

        return true;
    }

    //改
    private void ReviseItem(int PlayerId,int PlayerType)
    {
        //先判断是否和当前类型相同，不同就删除然后新建
        if (PlayersIdToStatus[PlayerId] != PlayerType)
        {
            PlayersIdToStatus[PlayerId]=PlayerType;

            Destroy(PlayerIdToItem[PlayerId]);
            PlayerIdToItem.Remove(PlayerId);

            CreateNewItem(PlayerId, PlayerType);
        }
    }
    //增
    private void AddItem(int PlayerId,int PlayerType)
    {
        if (PlayersIdToStatus.ContainsKey(PlayerId)) { return; }
        //记录id与对应类型，创建item 
        PlayersIdToStatus.Add(PlayerId, PlayerType);
        CreateNewItem(PlayerId, PlayerType);
    }
    //删
    private void RemoveItem(int PlayerId)
    {
        if (!PlayersIdToStatus.ContainsKey(PlayerId)) { return; }
        //移除哈希表对应项，并销毁对应item
        PlayersIdToStatus.Remove(PlayerId);
        Destroy(PlayerIdToItem[PlayerId]);
        PlayerIdToItem.Remove(PlayerId);
    }
    //创建
    private void CreateNewItem(int playerId, int type)
    {
        GameObject prefab = (type == 0) ? PlayerInviteItem : PlayerStatusItem;
        GameObject item = Instantiate(prefab, List.transform);

        // Update text
        TMP_Text[] texts = item.GetComponentsInChildren<TMP_Text>();
        if (texts.Length > 0)
        {
            texts[0].text = playerId.ToString();
        }

        // If invite type, bind button event
        if (type == 0)
        {
            Button inviteBtn = item.GetComponentInChildren<Button>();
            if (inviteBtn != null)
            {
                inviteBtn.onClick.AddListener(() => OnInviteButtonClicked(inviteBtn, playerId));
            }
        }

        if(type == 1)
        {
            texts[1].text = "InRoom";
        }

        if(type == 2)
        {
            texts[1].text = "Gaming";
        }

        // Save reference
        PlayerIdToItem[playerId] = item;
    }

    #region 玩家状态列表：邀请按钮失效与恢复
    private void OnInviteButtonClicked(Button btn, int playerId)
    {
        if (!btn.interactable) return;

        Debug.Log($"邀请玩家 {playerId}");

        GameManger.instance.SendInvite(playerId);

        StartCoroutine(GameManger.instance.ResetButtonInteractable(btn));
    }
    #endregion


    #endregion

    #region  交换执子类型相关
    public void RequestExchange() //请求交换执子类型
    {
        GameManger.instance.SendExchange();
    }
    private void ExchangeStoneType()
    {
        if (GameManger.instance.PlayerType == 1) //黑
        {
            MyTypeText.text = "black";
            OpponentTypeText.text = "white";
        }
        else
        {
            MyTypeText.text = "white";
            OpponentTypeText.text = "black";
        }
    } //实际交换代码
    #endregion  

    #region 准备相关
    public void RequestChangeReadyStatus()
    {
        GameManger.instance.SendReady();
    }  //请求交换状态

    private void ChangeReadyStatus() //具体UI操作，当自己准备的时候，开始按钮才可以使用
    {
        if (GameManger.instance.MyReadyStatus)
        {
            MyReadyText.text = "Ready";
            StartButton.interactable = true;
        }
        else
        {
            MyReadyText.text = "NotReady";
            StartButton.interactable= false;
        }
        if (GameManger.instance.OpponentReadyStatus)
        {
            OpponentReadyText.text = "Ready";
        }
        else
        {
            OpponentReadyText.text = "NotReady";
        }
    }
    #endregion

    //退出房间
    public void ExitRoom()
    {
        GameManger.instance.SendExitRoom();
        SceneManger.instance.SwitchScene("Room");
    }

    //退出到主界面
    public void ExitToLogic()
    {
        GameManger.instance.SendExitRoom();
        GameManger.instance.Finally();
        GameManger.instance.Destroy();
        SceneManger.instance.SwitchScene("Logic");
    }

    private void JoinRoom(int Id)    //加入房间后具体ui操作代码
    {
        OpponentIdText.text = Id.ToString();
        ExchangeButton.interactable= true;
        ReadyButton.interactable= true;
        ExitRoomButton.interactable= true;

        if (GameManger.instance.PlayerType == 1) //黑
        {
            MyTypeText.text = "black";
            OpponentTypeText.text= "white";
        }
        else
        {
            MyTypeText.text = "white";
            OpponentTypeText.text = "black";
        }

        ChangeReadyStatus();
    }

    public void GameStart()
    {
        GameManger.instance.SendGameStart();
    }

    public void ResetButtonInteractableFor(Button btn)
    {
        if (btn == null) return;
        StartCoroutine(GameManger.instance.ResetButtonInteractable(btn));
    }


    private void OnDisable()
    {
        if (GameManger.instance == null) return;

        GameManger.instance.JoinRoomSuccess -= JoinRoom;
        GameManger.instance.ExchangeSuccess -= ExchangeStoneType;
        GameManger.instance.OnPlayersStatusUpdated -= HandleStatusUpdate;
        GameManger.instance.OnReadyStatusChanged -= ChangeReadyStatus;

        Debug.Log("Room UI 事件解绑完成");
    }

}

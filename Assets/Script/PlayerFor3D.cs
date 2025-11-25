using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;


public class PlayerFor3D : MonoBehaviour
{

    [Header("旋转棋盘")]
    [SerializeField] private float xInput;
    [SerializeField] private float xSpeed;     //旋转速度
    [SerializeField] private float yInput;
    [SerializeField] private float ySpeed;

    [Header("判断点击位置")]
    [SerializeField] private LayerMask whatIsBoard;

    [Header("棋子信息")]
    [SerializeField] private GameObject BlackStone;
    [SerializeField] private GameObject WhiteStone;
    [SerializeField] private GameObject TempStone;
    [SerializeField] private int StoneNum=0;
    private List<GameObject> allStone = new List<GameObject>();       //棋子对象列表

    [Header("轮次信息")]
    [SerializeField] int turns;       //多少轮
    [SerializeField] bool BlackTurn;    //是不是黑棋回合


    private struct BoardPoint
    {
        public Vector3 pointPosition;       //棋盘交叉点坐标
        public int stoneType;                //交叉点上棋子状态，0，1,2
        /*
         * 上下左右点坐标是否要记录
         * 
        */
    };

    private BoardPoint[,] boardPoints;        //二维数组，从某个点展开，上下左右遍历一遍，得到二维数组，落子改状态，之后dfs判断气

    private struct StoneGroup
    {
        public BoardPoint[] points;
        public int count_Qi;
    }

    private List<StoneGroup> WhiteStoneGroups = new List<StoneGroup>();
    private List<StoneGroup> BlackStoneGroups = new List<StoneGroup>();
    /*
     * 每次落子修改相邻黑棋白棋块气的数量
     * 每次落子，理论上只影响上下左右四个地方
     * 提子遍历所有块？
     * 悔棋或其他功能需要考虑
     */

    void Start()
    {

    }

    void Update()
    {
        xInput = Input.GetAxis("Horizontal");
        yInput = Input.GetAxis ("Vertical");
        //Debug.Log(Input.mousePosition);

        if (Input.GetKeyDown(KeyCode.Q)&&Check_LuoZi("Black"))
        {
            LuoZi("Black");
        }

        if (Input.GetKeyDown(KeyCode.E) && Check_LuoZi("White"))
        {
            LuoZi("White");

        }

        if (Input.GetMouseButtonDown(1) && Check_HuiQi())
        {
            HuiQi();
        }
    }
    private void FixedUpdate()
    {
        transform.Rotate(yInput*xSpeed,xInput*ySpeed,0,Space.World);     //球面旋转
    }
    
    private void LuoZi(string s)
    {
        RaycastHit hit = GetPosition();

        if (hit.collider != null)          //鼠标在没在球面上
        {
            Vector3 normal = hit.normal;
            Quaternion targetRotation = Quaternion.LookRotation(normal, Vector3.forward);
            

            if (s == "Black") 
            { 
              GameObject qizi = Instantiate(BlackStone, hit.point, targetRotation, transform);   //创建棋子
              allStone.Add(qizi);
            }
            else 
            {
                GameObject qizi = Instantiate(WhiteStone, hit.point, targetRotation, transform);
                allStone.Add(qizi);
            }

            StoneNum++;
            turns++;
            BlackTurn = !BlackTurn;
        }
    }

    private void HuiQi()                /*悔棋
                                         * 如果上轮没有提子之类，直接销毁最后一个棋子
                                         * 如果上轮触发了提子之类，还需要恢复被提的子（额外的容器记录）
                                        */
    { 
        Destroy(allStone[StoneNum - 1]);
        allStone.RemoveAt(StoneNum - 1);
        StoneNum--;
        turns--;
        BlackTurn = !BlackTurn;
    }


    public RaycastHit GetPosition()              //获取鼠标在球面上碰撞点信息
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        RaycastHit hit;

        Physics.Raycast(ray, out hit, Mathf.Infinity, whatIsBoard);

        return hit;
        
    }

    private bool Check_LuoZi(string s)     
    {
        if(  ((s=="Black"&&BlackTurn)   || (s == "White" && !BlackTurn))  &&  !IsForbiddenPoint())
        {
            return true;
        }
        else
        {
            Debug.Log("不是你的回合");
            return false;
        }
    }

    private bool Check_HuiQi()
    {
        if (StoneNum > 0)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    private bool IsForbiddenPoint() //落子地方是不是已经有子；落子之后是不是直接死且不提子；是不是劫
    {
        return false;
    }

    private void TiZi()             //落子之后循环判断对方棋子的气
    {

    }

    private void check_Qi()         //判断气
    {
        
    }

}

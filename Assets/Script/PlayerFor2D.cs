using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;


public class PlayerFor2D : MonoBehaviour
{
    [Header("判断点击位置")]
    [SerializeField] private LayerMask whatIsBoard;
    RaycastHit hit ;

    [Header("棋子信息")]
    [SerializeField] private GameObject BlackStone;
    [SerializeField] private GameObject WhiteStone;
    [SerializeField] private GameObject TempStone;
    [SerializeField] private int StoneNum = 0;
    private List<GameObject> allStone = new List<GameObject>();       //棋子对象列表
    [SerializeField] private Vector3 StoneSize;

    [Header("轮次信息")]
    [SerializeField] int turns;       //多少轮
    [SerializeField] bool BlackTurn;    //是不是黑棋回合


    private class BoardPoint
    {
        public int stoneType;          // 0,1,2 空，黑，白
        public int up;                //所有棋子存储在list中，记录id即可
        public int down;
        public int left;
        public int right;

        public BoardPoint(int stoneType, int up, int down, int left, int right)
        {
            this.stoneType = stoneType;
            this.up = up;
            this.down = down;
            this.left = left;
            this.right = right;
        }
    }

    private List<BoardPoint> boardPoints = new List<BoardPoint>();

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

    void Awake()
    {
        CreateBoardPoint();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q) && Check_LuoZi("Black"))
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

        if (Input.GetMouseButtonDown(0))
        {
            CheckStone(); 
        }
    }

    private void LuoZi(string s)
    {
        if (hit.collider != null)          //鼠标在没在球面上
        {
            Vector3 normal = hit.normal;
            Quaternion targetRotation = Quaternion.LookRotation(normal, Vector3.forward);

            Vector3 point = GetPoint(hit.point);

            Debug.Log(point);

            if (s == "Black")
            {
                GameObject qizi = Instantiate(BlackStone, point, targetRotation, transform);   //创建棋子
                qizi.transform.localScale = StoneSize;
                allStone.Add(qizi);
                Debug.Log(PointToNum(point));
                boardPoints[PointToNum(point)].stoneType = 1;
            }
            else
            {
                GameObject qizi = Instantiate(WhiteStone, point, targetRotation, transform);
                qizi.transform.localScale = StoneSize;
                allStone.Add(qizi);
                Debug.Log(PointToNum(point));
                boardPoints[PointToNum(point)].stoneType = 2;
            }

            StoneNum++;
            turns++;
            BlackTurn = !BlackTurn;
        }
    }



    public RaycastHit GetPosition()              //获取鼠标在球面上碰撞点信息
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        RaycastHit hit;

        Physics.Raycast(ray, out hit, Mathf.Infinity, whatIsBoard);

        return hit;

    }
    private Vector3 GetPoint(Vector3 point)
    {
        point.x = Mathf.Round(point.x);
        point.z = Mathf.Round(point.z);
        return point;
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
    private bool Check_LuoZi(string s)
    {
        hit = GetPosition();
        if (((s == "Black" && BlackTurn) || (s == "White" && !BlackTurn)))
        {
            if (IsForbiddenPoint())
            {
                Debug.Log("此处禁止落子");
                return false;
            }
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
            Debug.Log("当前禁止悔棋");
            return false;
        }
    }

    private bool IsForbiddenPoint() //落子地方是不是已经有子；落子之后是不是直接死且不提子；是不是劫
    {
        if (boardPoints[PointToNum(hit.point)].stoneType == 0)       //是不是有子
        {
            return false;
        }
        else
        {
            return true;
        }
    }

    private void TiZi()             //落子之后循环判断对方棋子的气
    {

    }

    private void Check_Qi()         //判断气
    {

    }


    private void CreateBoardPoint()
    {
        for(int i = -9; i < 10; i++)
        {
            for(int j = -9; j < 10; j++)
            {
                boardPoints.Add(new BoardPoint(0,PointToNum(i,j+1),PointToNum(i,j-1),PointToNum(i-1,j),PointToNum(i+1,j)));
            }
        }
    }

    private int PointToNum(int i,int j)       
    {

        #region Note
        /*
         * 游戏中，棋盘的坐标格式为（x,0,z）
         * x,z均为-9~9
         * 所以最左下角如下,省略y轴
         *           (-9,-8)
         *  (-10,-9) (-9,-9) (-8,-9)
         *           (-9,-10)
         *  从最左下角的-9,-9开始，加到boardpoints中，对于下标为0
         *  因为-10,出界，所以返回-1，访问时只需查看是否出界即可判断
         *  所以，i也就是x,一个对应1，j也就是z一个对应19
         */

        #endregion

        int num = 0;

        if(i<-9||i>9||j < -9 || j > 9)
        {
            num = -1;
            return num;
        }
        else
        {
            num += i + 9 + (j+9)*19;
        }

        return num;
    }

    private int PointToNum(Vector3 point)
    {
        int i = (int)point.x;  
        int j = (int)point.z;
        return PointToNum(i, j);
    }

    private void CheckStone()                  //输出当前棋盘上，不为空子的对应位置在boardpoints的下标
    {
        for(int i=0;i<boardPoints.Count;i++)
        {
            if (boardPoints[i].stoneType!=0)
            {
                Debug.Log(i);
            }
        }
    }
}

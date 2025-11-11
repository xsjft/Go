using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;


public class player : MonoBehaviour
{

    [Header("旋转棋盘")]
    [SerializeField] private float xInput;
    [SerializeField] private float xSpeed;     //旋转速度
    [SerializeField] private float yInput;
    [SerializeField] private float ySpeed;

    [Header("判断点击位置")]
    [SerializeField] private LayerMask whatIsBall;       

    [Header("棋子信息")]
    [SerializeField] private GameObject Qizi_Black;
    [SerializeField] private GameObject Qizi_White;
    [SerializeField] private int QiziNum=0;
    private List<GameObject> allQizi = new List<GameObject>();       //棋子对象列表

    [Header("轮次信息")]
    [SerializeField] int turns;       //多少轮
    [SerializeField] bool BlackTurn;    //是不是黑棋回合


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
            if (s == "Black") 
            { 
              GameObject qizi = Instantiate(Qizi_Black, hit.point, Quaternion.identity, transform);   //创建棋子
              allQizi.Add(qizi);
            }
            else 
            {
                GameObject qizi = Instantiate(Qizi_White, hit.point, Quaternion.identity, transform);
                allQizi.Add(qizi);
            }

            QiziNum++;
            turns++;
            BlackTurn = !BlackTurn;
        }
    }

    private void HuiQi()                //悔棋，棋子列表最后一个直接销毁就行       后面判断可能会用到数组去统计棋子在棋盘上的信息，这里也要补上相应的修改   
    {
        Destroy(allQizi[QiziNum - 1]);
        allQizi.RemoveAt(QiziNum - 1);
        QiziNum--;
        turns--;
        BlackTurn = !BlackTurn;
    }


    public RaycastHit GetPosition()              //获取鼠标在球面上碰撞点信息
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        RaycastHit hit;

        Physics.Raycast(ray, out hit, Mathf.Infinity, whatIsBall);

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
        if (QiziNum > 0)
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

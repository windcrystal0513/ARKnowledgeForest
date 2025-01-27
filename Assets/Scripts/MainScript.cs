﻿using System.Collections.Generic;
using UnityEngine;
using Vuforia;
using Baidu.Aip.Ocr;
using System.Threading;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.TrackingModule;
using LitJson;
using UnityEngine.UI;
using System.Net;
using System.IO;
using System.Text;
using System;
using System.Collections;
using UnityEngine.EventSystems;

public class MainScript : MonoBehaviour
{
    /* 
     * 参数部分
     * */

    /*
     * 全局部分
     * */
    public Text tipsText;    // 主屏幕提示符
    public GameObject startButton;  // 确定按钮
    public GameObject restartButton;    // 返回按钮

    /*
     * 百度OCR接口部分
     * */

    // 设置百度接口数据
    private static string APP_ID = "15948497";
    private static string API_KEY = "KspjD5vYehAk7iXRENII2CLF";
    private static string SECRET_KEY = "cVw8bWeDyDwBih8MZSFxxf9au55ckYqc";

    private Ocr ocrAPI = new Ocr(API_KEY, SECRET_KEY);  // 初始化百度接口
    private Texture2D frameOCR; // 用于OCR分析的相机帧数据
    private bool ocrThreadFlag = false;    // OCR线程标志位，为true表示线程结束
    private string OCR_RESULT = "";  // 存储OCR结果的全局变量

    const int OCR_IMAGE_QUALITY = 40;


    /*
     * Vuforia部分
     * */

    public GameObject groundPlaneFinder;    // Ground Plane Finder
    public GameObject arCamera; // ARCamera

    private Texture tempFrame;  // Vuforia相机取帧暂存


    /*
     * OpenCV追踪部分
     * */
    
    private int TrackingFlag = 0;    // 定义追踪状态的flag，0表示未开始追踪，1表示初始化，2表示更新追踪，3保留
    private Texture2D trackingFrameOld; // 处理前的帧数据
    private Texture2D trackingFrameNew; // 处理后的帧数据
    private Mat trackingFrame;  // 处理追踪的Mat帧
    private Mat trackingFrameGray;  // 计算跟踪的灰度图

    private TrackerMOSSE trackers;  // 定义追踪器

    private Rect2d trackingWindow;  // 定义追踪框
    private Scalar scalar = new Scalar(64, 157, 248);  // 追踪框的好 颜色值
    private Point xyLow;    // 定义追踪框的左上角坐标
    private Point xyHigh;   // 定义追踪框的右下角坐标

    private int TimeFlag;
    private int TIMEFLAG = 3;

    /*
     * Collider按钮部分
     * */
    public GameObject cubeButtonPrefab; // 按钮Prefab

    private double[] points;    // 文字区域及关键词位置信息
    private string[] items; // 关键词内容
    private Point xyKeywordsLow = new Point();  // 关键词的坐标
    private Point xyKeywordsHigh = new Point(); // 关键词的坐标
    private GameObject[] cubeButton;    // 实例化后的按钮数组


    /*
     * 生成树部分
     * */

    public GameObject groundPlaneStage; // ground plane stage
    public GameObject trunk;    // 树干Prefab
    public GameObject b1_1; // 一级存放球的分支
    public GameObject b2_1; // 二级分枝，带球
    public GameObject b2_2;
    public GameObject b2_3;
    public GameObject b2_4;
    public GameObject b2_5;
    public GameObject b3_1; // 三级分支，带球
    public GameObject b3_2;
    public GameObject b3_3;
    public GameObject b3_4;
    public GameObject b3_5;
    public GameObject b3_6;
    
    private Dictionary<int, Dictionary<int, string[]>> leaves;    // 存储数据结构的字典

    const float ORIGIN_TREE_SCALE_FACTER=20f;

    /*
     * 面板展开部分
     * */

    public ScrollRect scrollView;   // 滚动视图
    public Button textMessagePrefab;    // 可供点击的message button
    public GameObject content;

    private GameObject clickedSphere;   // 点击物体
    private bool clickedFlag = false;   // 标志位，为true表示有球被点击
    private Vector2 screenPos;  // 转换坐标
    private Vector2 screenScale;    // 转换坐标
    private Transform platformPoint;    // 球上的面板点
    private Button[] textButton;    // 可点击的button集合

    /*
     * 手势控制部分
     * */

    const int ROTATE_FECTOR = 8;
    const int SCALE_FECTOR = 10000;
    const float SCALE_MIN_FECTOR = 0.02f;
    const float SCALE_MAX_FECTOR = 0.5f;
    private Touch oldTouch1;  //上次触摸点1(手指1)  
    private Touch oldTouch2;  //上次触摸点2(手指2)  
    private Transform treeTransform;    // 生成树的Transform


    /*
     * 函数部分
     * */

    // 第一帧开始渲染时调用
    private void Start()
    {
        ocrAPI.Timeout = 60000; // 修改OCR超时时间
        groundPlaneFinder.SetActive(false);  // 取消掉Ground Plane Finder
        scrollView.gameObject.SetActive(false); // 取消掉scroll view
        TimeFlag = TIMEFLAG - 2;    // 设置刷新帧控制值
        restartButton.SetActive(false); // 将返回按钮设置成不可用
    }

    // 帧刷新时调用
    private void Update()
    {
        // 对应初始化阶段
        if (TrackingFlag==0)
        {
            if (VuforiaRenderer.Instance != null && VuforiaRenderer.Instance.VideoBackgroundTexture != null)
            {
                tempFrame = VuforiaRenderer.Instance.VideoBackgroundTexture;    // 取到一帧
                BackgroundPlaneBehaviour vuforiaBackgroundPlane = FindObjectOfType<BackgroundPlaneBehaviour>(); //获取屏幕
                if (vuforiaBackgroundPlane != null)
                {
                    vuforiaBackgroundPlane.GetComponent<Renderer>().material.mainTexture = tempFrame;
                }
            }
        }

        // 在OCR与API1返回正确的结果后进入
        if (ocrThreadFlag)
        {
            ocrThreadFlag = false;  // 线程结束收到，将线程标志位复原

            tipsText.text = "";

            // 获取关键词数量
            JsonData resultJson = JsonMapper.ToObject(OCR_RESULT);
            JsonData resultsNum = resultJson["result_num"];
            int wordsNum = int.Parse(resultsNum.ToString());

            if (wordsNum == 0)
            {
                tipsText.text = "对不起，您拍摄的页面没有识别到关键词\n请尝试重新拍摄";
            }
            else
            {
                GetResult(wordsNum);   // 获取关键词位置

                cubeButton = new GameObject[wordsNum];  // 生成Collider Button的数组

                for (int c = 0; c < wordsNum; c++)  // 实例化Collider Button
                {
                    cubeButton[c] = Instantiate(cubeButtonPrefab);
                    cubeButton[c].name = c.ToString();
                    cubeButton[c].tag = "CubeButton";
                }

                // 初始跟踪框坐标与框
                xyLow = new Point(points[0], points[1]);
                xyHigh = new Point(points[2], points[3]);
                trackingWindow = new Rect2d(xyLow, xyHigh);

                // 创建各种图像资源的引用
                if (VuforiaRenderer.Instance != null && VuforiaRenderer.Instance.VideoBackgroundTexture != null)
                {
                    tempFrame = VuforiaRenderer.Instance.VideoBackgroundTexture;    // 取帧Texture
                    trackingFrameOld = new Texture2D(tempFrame.width, tempFrame.height, TextureFormat.RGB24, false);    // 取帧Texture2D
                    Utils.textureToTexture2D(tempFrame, trackingFrameOld);
                    trackingFrame = new Mat(trackingFrameOld.height, trackingFrameOld.width, CvType.CV_8UC3);   // 构造Mat
                    trackingFrameGray = new Mat(trackingFrameOld.height, trackingFrameOld.width, CvType.CV_8UC1);   // 灰度图Mat
                    trackingFrameNew = new Texture2D(trackingFrame.cols(), trackingFrame.rows(), TextureFormat.RGB24, false);  // 初始化新的Vuforia帧
                }

                TrackingFlag = 1;   // 将TrackingFlag置1，开启识别模式
            }

        }

        // OCR确定关键词信息和追踪区域后初始化Tracker时调用
        if (TrackingFlag == 1 && TimeFlag == TIMEFLAG)
        {
            if (VuforiaRenderer.Instance != null && VuforiaRenderer.Instance.VideoBackgroundTexture != null)
            {
                tempFrame = VuforiaRenderer.Instance.VideoBackgroundTexture;    // 取到一帧
                Utils.textureToTexture2D(tempFrame, trackingFrameOld);
                Utils.texture2DToMat(trackingFrameOld, trackingFrame, true, -1);   // 将帧转换成Mat处理
                Imgproc.cvtColor(trackingFrame, trackingFrameGray, Imgproc.COLOR_BGR2GRAY); // 转化成灰度图计算

                trackers = TrackerMOSSE.create();

                if (trackers.init(trackingFrameGray, trackingWindow))  // 初始化追踪
                {
                    DrawTrackingAndButton();    // 绘制关键词的追踪框和button

                    Utils.matToTexture2D(trackingFrame, trackingFrameNew, true, -1);   // 将Mat格式的frame转换成Texture2D

                    BackgroundPlaneBehaviour vuforiaBackgroundPlane = FindObjectOfType<BackgroundPlaneBehaviour>(); // 获取屏幕
                    if (vuforiaBackgroundPlane != null)
                    {
                        vuforiaBackgroundPlane.GetComponent<Renderer>().material.mainTexture = trackingFrameNew;    // 绘制处理后的图像
                        TrackingFlag = 2;   // 更新flag到更新格式
                        TimeFlag = 1;   // time标志位归一
                        startButton.SetActive(false);
                        restartButton.SetActive(true);
                    }
                }
            }
        }

        // Tracker初始化正常，追踪阶段（更新Tracker阶段）调用
        if (TrackingFlag == 2 && TimeFlag == TIMEFLAG)
        {
            if (VuforiaRenderer.Instance != null && VuforiaRenderer.Instance.VideoBackgroundTexture != null)
            {
                tempFrame = VuforiaRenderer.Instance.VideoBackgroundTexture;    // 取到一帧
                Utils.textureToTexture2D(tempFrame, trackingFrameOld);
                Utils.texture2DToMat(trackingFrameOld, trackingFrame, true, -1);   // 将帧转换成Mat处理
                Imgproc.cvtColor(trackingFrame, trackingFrameGray, Imgproc.COLOR_BGR2GRAY);

                if (trackers.update(trackingFrameGray, trackingWindow))    // 更新追踪器
                {
                    TimeFlag = 1;

                    // 计算新的追踪框的位置
                    xyLow.x = trackingWindow.x;
                    xyLow.y = trackingWindow.y;
                    xyHigh.x = trackingWindow.x + trackingWindow.width;
                    xyHigh.y = trackingWindow.y + trackingWindow.height;

                    DrawTrackingAndButton();

                    if (Input.GetMouseButton(0))
                    {
                        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                        RaycastHit hit;

                        if (Physics.Raycast(ray, out hit))
                        {
                            GameObject clickedGameObject = hit.collider.gameObject;
                            if (clickedGameObject.tag == "CubeButton")
                            {
                                int clickedName = int.Parse(clickedGameObject.name);
                                InitializeGroundPlane(items[clickedName]);
                            }
                        }
                    }

                    Utils.matToTexture2D(trackingFrame, trackingFrameNew, true, -1);   // 将Mat格式的frame转换成Texture2D

                    BackgroundPlaneBehaviour vuforiaBackgroundPlane = FindObjectOfType<BackgroundPlaneBehaviour>(); // 获取屏幕
                    if (vuforiaBackgroundPlane != null)
                    {
                        vuforiaBackgroundPlane.GetComponent<Renderer>().material.mainTexture = trackingFrameNew;    // 绘制处理后的图像
                    }
                }
            }
        }

        // 每次time标志位没有到达TIMEFLAG值的时候正常刷新
        if (TimeFlag != TIMEFLAG && TrackingFlag != 3 && TrackingFlag!=0)
        {
            TimeFlag++;
            if (VuforiaRenderer.Instance != null && VuforiaRenderer.Instance.VideoBackgroundTexture != null)
            {
                tempFrame = VuforiaRenderer.Instance.VideoBackgroundTexture;    // 取到一帧
                Utils.textureToTexture2D(tempFrame, trackingFrameOld);
                Utils.texture2DToMat(trackingFrameOld, trackingFrame, true, -1);   // 将帧转换成Mat处理

                DrawTrackingAndButton();

                if (Input.GetMouseButton(0))
                {
                    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    RaycastHit hit;

                    if (Physics.Raycast(ray, out hit))
                    {
                        GameObject clickedGameObjectHere = hit.collider.gameObject;
                        if (clickedGameObjectHere.tag == "CubeButton")
                        {
                            int clickedName = int.Parse(clickedGameObjectHere.name);
                            InitializeGroundPlane(items[clickedName]);
                        }
                    }
                }

                Utils.matToTexture2D(trackingFrame, trackingFrameNew, true, -1);   // 将Mat格式的frame转换成Texture2D

                BackgroundPlaneBehaviour vuforiaBackgroundPlane = FindObjectOfType<BackgroundPlaneBehaviour>(); //获取屏幕
                if (vuforiaBackgroundPlane != null)
                {
                    vuforiaBackgroundPlane.GetComponent<Renderer>().material.mainTexture = trackingFrameNew;
                }
            }
        }

        // 用户确定查看关键词，进入AR阶段
        if (TrackingFlag == 3)
        {
            if (VuforiaRenderer.Instance != null && VuforiaRenderer.Instance.VideoBackgroundTexture != null)
            {
                tempFrame = VuforiaRenderer.Instance.VideoBackgroundTexture;    // 取到一帧
                BackgroundPlaneBehaviour vuforiaBackgroundPlane = FindObjectOfType<BackgroundPlaneBehaviour>(); //获取屏幕
                if (vuforiaBackgroundPlane != null)
                {
                    vuforiaBackgroundPlane.GetComponent<Renderer>().material.mainTexture = tempFrame;
                }
            }

            // 点击球的逻辑
            if (Input.GetMouseButton(0))
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit))
                {
                    clickedSphere = hit.collider.gameObject;
                    int sKey = clickedSphere.GetInstanceID();

                    Dictionary<int, string[]> sValue;

                    if (leaves.TryGetValue(sKey, out sValue))
                    {
                        GameObject fatherGameObject = clickedSphere.transform.parent.gameObject;
                        platformPoint = fatherGameObject.GetComponentsInChildren<MyPlatformPoint>()[0].gameObject.transform;

                        scrollView.gameObject.SetActive(true);

                        // 先销毁所有content下的button
                        OnMessageButtonClicked[] buttonsInContent = content.GetComponentsInChildren<OnMessageButtonClicked>();
                        for (int b = 0; b < buttonsInContent.Length; b++)
                        {
                            Destroy(buttonsInContent[b].gameObject);
                        }

                        int num = sValue.Count;

                        content.GetComponents<RectTransform>()[0].anchoredPosition3D = new Vector3(0, 0, 0);
                        content.GetComponents<RectTransform>()[0].sizeDelta = new Vector2(0, 50 * num + 25);

                        textButton = new Button[num];
                        int h = 0;

                        foreach (int messageKey in sValue.Keys)
                        {
                            textButton[h] = Instantiate(textMessagePrefab);
                            textButton[h].transform.parent = content.transform;
                            textButton[h].GetComponentsInChildren<Text>()[0].text =sValue[messageKey][0];
                            textButton[h].GetComponents<RectTransform>()[0].anchoredPosition3D = new Vector3(0, h * (-50) - 10, 0);

                            OnMessageButtonClicked onMessageButton = textButton[h].GetComponents<OnMessageButtonClicked>()[0];
                            onMessageButton.messageValue = sValue[messageKey][1];

                            h++;
                        }
                        clickedFlag = true; // 将点击球标志位置为true

                    }

                }
            }

            // 如果点击中了一个球
            if (clickedFlag)
            {
                screenPos = Camera.main.WorldToScreenPoint(platformPoint.position);
                RectTransform rectTrans = scrollView.GetComponent<RectTransform>();
                rectTrans.position = screenPos;
            }

            // 手势控制
            if (Input.touchCount <= 0)
            {
                return;
            }

            // 手势控制
            //单点触摸控制旋转 
            if (1 == Input.touchCount)
            {
                Touch touch = Input.GetTouch(0);
                Vector2 deltaPos = touch.deltaPosition;
                treeTransform.Rotate(Vector3.down * deltaPos.x / ROTATE_FECTOR, Space.World);
            }

            //多点触摸控制防缩 
            Touch newTouch1 = Input.GetTouch(0);
            Touch newTouch2 = Input.GetTouch(1);

            if (newTouch2.phase == TouchPhase.Began)
            {
                oldTouch2 = newTouch2;
                oldTouch1 = newTouch1;
                return;
            }

            float oldDistance = Vector2.Distance(oldTouch1.position, oldTouch2.position);
            float newDistance = Vector2.Distance(newTouch1.position, newTouch2.position);
            float offset = newDistance - oldDistance;

            float scaleFactor = offset / SCALE_FECTOR;

            Vector3 localScale = treeTransform.localScale;
            Vector3 scale = new Vector3(localScale.x + scaleFactor, localScale.y + scaleFactor, localScale.z + scaleFactor);

            if (scale.x > SCALE_MIN_FECTOR && scale.y > SCALE_MIN_FECTOR && scale.z > SCALE_MIN_FECTOR && scale.x < SCALE_MAX_FECTOR && scale.y < SCALE_MAX_FECTOR && scale.z < SCALE_MAX_FECTOR)
            {
                treeTransform.localScale = scale;
            }

            oldTouch1 = newTouch1;
            oldTouch2 = newTouch2;
        }

    }


    // 绘制关键词的追踪框与button
    private void DrawTrackingAndButton()
    {
        // 绘制关键词框
        for (int n = 0; n < items.Length; n++)
        {
            xyKeywordsLow.x = xyLow.x + points[(n + 1) * 4] - 1;
            xyKeywordsLow.y = xyLow.y + points[(n + 1) * 4 + 1] - 1;
            xyKeywordsHigh.x = xyLow.x + points[(n + 1) * 4 + 2] + 1;
            xyKeywordsHigh.y = xyLow.y + points[(n + 1) * 4 + 3] + 1;

            Imgproc.rectangle(trackingFrame, xyKeywordsLow, xyKeywordsHigh, scalar, 2); // 绘制关键词矩形框

            //计算Button位置
            float targetWidth = (float)xyKeywordsHigh.x - (float)xyKeywordsLow.x;
            float targetHeight = (float)xyKeywordsHigh.y - (float)xyKeywordsLow.y;

            float targetX = (float)xyKeywordsLow.x;
            float targetY = (float)xyKeywordsLow.y;

            float imageWidth = frameOCR.width;
            float imageHeight = frameOCR.height;

            GameObject vuforiaScreen = GameObject.Find("ARCamera/BackgroundPlane");

            float K = 2 * vuforiaScreen.transform.localScale.z / imageHeight;

            float buttonPositionX;
            float buttonPositionY;

            float buttonScaleX = K * targetWidth;
            float buttonScaleY = 1;
            float buttonScaleZ = K * targetHeight;

            TextMesh contentText = cubeButton[n].GetComponentInChildren<TextMesh>();
            contentText.text = items[n];
            contentText.gameObject.transform.localPosition = new Vector3(0,0,0);

            if (Screen.orientation.ToString() == "LandscapeLeft")
            {
                buttonPositionX = (-1) * K * (targetX + targetWidth / 2 - imageWidth / 2);
                buttonPositionY = K * (targetY + targetHeight / 2 - imageHeight / 2);
                contentText.gameObject.transform.localEulerAngles=new Vector3(90,0,0);
                contentText.gameObject.transform.localScale = new Vector3(0.05f, 0.05f * buttonScaleX / buttonScaleZ, 1);
            }
            else if (Screen.orientation.ToString() == "LandscapeRight")
            {
                buttonPositionX = K * (targetX + targetWidth / 2 - imageWidth / 2);
                buttonPositionY = (-1) * K * (targetY + targetHeight / 2 - imageHeight / 2);
            } 
            else if (Screen.orientation.ToString() == "Portrait")
            {
                buttonPositionY = K * (targetX + targetWidth / 2 - imageWidth / 2);
                buttonPositionX = K * (targetY + targetHeight / 2 - imageHeight / 2);
                contentText.gameObject.transform.localEulerAngles = new Vector3(90, 0, 90);
                contentText.gameObject.transform.localScale = new Vector3(0.2f * buttonScaleX / buttonScaleZ, 0.2f, 1);
            }
            else
            {
                buttonPositionY = (-1) * K * (targetX + targetWidth / 2 - imageWidth / 2);
                buttonPositionX = (-1) * K * (targetY + targetHeight / 2 - imageHeight / 2);
            }

            Vector3 buttonPosition = new Vector3(buttonPositionX, buttonPositionY, 0);
            Vector3 buttonLocalScale = new Vector3(buttonScaleX, buttonScaleY, buttonScaleZ);

            cubeButton[n].transform.parent = arCamera.transform;
            cubeButton[n].transform.localPosition = vuforiaScreen.transform.localPosition + buttonPosition;
            cubeButton[n].transform.localRotation = vuforiaScreen.transform.localRotation;
            cubeButton[n].transform.localScale = buttonLocalScale;
        }
    }

    // 按钮点击后调用
    public void OnButtonClicked()
    {

        if (VuforiaRenderer.Instance != null && VuforiaRenderer.Instance.VideoBackgroundTexture != null)
        {
            tempFrame = VuforiaRenderer.Instance.VideoBackgroundTexture;    // 取到一帧
            frameOCR = new Texture2D(tempFrame.width, tempFrame.height, TextureFormat.RGB24, false);
            Utils.textureToTexture2D(tempFrame, frameOCR);  // texture 转换成 texture2D

            frameOCR = horizontalFlipPic(frameOCR); // 翻转图片
            byte[] frameOCRbyte = frameOCR.EncodeToJPG(OCR_IMAGE_QUALITY);   // 转换成PNG格式的图片

            object imageOCR = frameOCRbyte;    // 装箱
            Thread thread = new Thread(new ParameterizedThreadStart(this.GetOCR));  // 新建一个线程
            thread.Start(imageOCR);    // 开启线程，并传入参数image
            tipsText.text = "正在识别，请稍等……";    // 开启等待提示符
        }
    }

    // 线程函数，获取OCR结果存储到全局变量 OCR_RESULT 中
    private void GetOCR(object imageObj)
    {
        byte[] image = (byte[])imageObj;    // 拆箱

        var result = ocrAPI.General(image); // 调用通用文字识别

        var options = new Dictionary<string, object>{
            {"detect_direction", "true"}
        };  // 增加识别方向参数,识别单字结果

        result = ocrAPI.General(image, options);    // 带参数调用通用文字识别（含位置信息版）
        string ocrResult = result.ToString();   // ocrResultw为OCR结果

        // 开始请求API1获得追踪位置坐标与关键词及其位置坐标
        string api1Url = "http://yotta.xjtushilei.com:8000/crystal/get_location/?OCR_result=" + ocrResult;
        HttpWebRequest api1Request = (HttpWebRequest)WebRequest.Create(api1Url);
        api1Request.Method = "GET";
        api1Request.ContentType = "application/json";

        // 捕获请求失败的异常
        try
        {
            HttpWebResponse api1Response = (HttpWebResponse)api1Request.GetResponse();
            StreamReader reader = new StreamReader(api1Response.GetResponseStream(), Encoding.Default);
            OCR_RESULT = reader.ReadToEnd();
            ocrThreadFlag = true;  // 线程结束标志位
        }
        catch (WebException e)
        {
            WebResponse wenReq = (HttpWebResponse)e.Response;
            StreamReader reader = new StreamReader(wenReq.GetResponseStream(), Encoding.Default);
            tipsText.text = "网络请求出现异常，请检查网络链接";
        }

        api1Request.Abort();    // 结束网络请求
    }

    // 分析OCR和API1返回结果，得到关键词的数据
    private void GetResult(int wordsNum)
    {
        points = new double[(wordsNum + 1) * 4];    // 初始化数组大小存储坐标点
        items = new string[wordsNum];   // 初始化数组存储关键词

        JsonData resultJson = JsonMapper.ToObject(OCR_RESULT);
        JsonData wordsBorder = resultJson["border"];    // 获取border信息
        JsonData wordsItems = resultJson["results"];    // 获取所有关键词

        // 边界点
        points[0] = double.Parse(wordsBorder["left"].ToString());
        points[1] = double.Parse(wordsBorder["top"].ToString());
        points[2] = double.Parse(wordsBorder["right"].ToString());
        points[3] = double.Parse(wordsBorder["down"].ToString());

        // 文字区域的宽高
        double borderWidth = double.Parse(wordsBorder["right"].ToString()) - double.Parse(wordsBorder["left"].ToString());
        double borderHeight = double.Parse(wordsBorder["down"].ToString()) - double.Parse(wordsBorder["top"].ToString());

        // 关键词信息
        for (int i = 0; i < wordsNum; i++)
        {
            JsonData itemLocation = wordsItems[i]["location"];
            items[i] = wordsItems[i]["words"].ToString();   // 获取关键词
            points[4 * (i + 1)] = double.Parse(itemLocation["left"].ToString());
            points[4 * (i + 1) + 1] = double.Parse(itemLocation["top"].ToString());
            points[4 * (i + 1) + 2] = double.Parse(itemLocation["left"].ToString()) + double.Parse(itemLocation["width"].ToString());
            points[4 * (i + 1) + 3] = double.Parse(itemLocation["top"].ToString()) + double.Parse(itemLocation["height"].ToString());
        }
    }

    // 进入AR阶段
    void InitializeGroundPlane(string item)
    {
        API2Start(item);

        treeTransform = groundPlaneStage.GetComponentsInChildren<Transform>()[1];

        TrackingFlag = 3;

        groundPlaneFinder.SetActive(true);  // 初始化groundPlaneFinder

        trackers.Dispose();

        // 销毁cubeButton
        for (int c = 0; c < items.Length; c++)
        {
            Destroy(cubeButton[c]);
        }
    }

    // API2调用以及生成树
    private void API2Start(string itemName)
    {
        string api2_result;
        string api1Url = "http://yotta.xjtushilei.com:8000/crystal/get_AllInByTopicName/?topic=" + itemName;
        HttpWebRequest api2Request = (HttpWebRequest)WebRequest.Create(api1Url);
        api2Request.Method = "GET";
        api2Request.ContentType = "application/json";
        HttpWebResponse api1Response = (HttpWebResponse)api2Request.GetResponse();
        StreamReader reader = new StreamReader(api1Response.GetResponseStream(), Encoding.Default);
        api2_result = reader.ReadToEnd();
        api2Request.Abort();

        JsonData returnData = JsonMapper.ToObject(api2_result);
        string success = returnData["msg"].ToString();

        if (success == "成功")//成功则显示树，不成功则提示
        {
            //实例化一个ArrayList字典，存储叶子的信息
            leaves = new Dictionary<int, Dictionary<int, string[]>>();

            Vector3 position = new Vector3(0, 0, 0);
            Vector3 scale = new Vector3(1, 1, 1);

            //实例化树干
            GameObject mytrunk = Instantiate(trunk);
            mytrunk.GetComponentInChildren<TextMesh>().text = itemName;
            mytrunk.transform.parent = groundPlaneStage.transform;
            mytrunk.transform.localPosition = position;
            mytrunk.transform.localScale = scale / ORIGIN_TREE_SCALE_FACTER;

            //得到树干所有的字物体
            Transform[] allChildren1 = mytrunk.GetComponentsInChildren<Transform>();

            JsonData first_branch = returnData["data"]["children"];
            int first_count = first_branch.Count;

            //只生成一个球的flag
            int first_flag = 0;

            //实例化一个dictionary,存储这个小球上所有的碎片信息
            Dictionary<int, string[]> first_dic = new Dictionary<int, string[]>();

            int first_sphere = new int();//小球的id
            for (int i = 0; i < first_count; i++)
            {
                //第一层循环
                string first_type = first_branch[i]["type"].ToString();//第一层判断

                if (first_type == "leaf")
                {
                    //将小球信息加入到dictionary里面
                    string[] first_webpage = new string[2];
                    string first_url = first_branch[i]["url"].ToString();
                    int first_name = i;
                    string first_content = first_branch[i]["assembleContent"].ToString();
                    first_webpage[0] = first_content;
                    first_webpage[1] = first_url;
                    first_dic.Add(first_name,first_webpage );
                    //生成树叶
                    if (first_flag == 0)
                    {
                        first_flag = 1;
                        //实例化一个球
                        GameObject first_s = Instantiate(b1_1);
                        Transform[] allChildren_s1 = first_s.GetComponentsInChildren<Transform>();
                        first_sphere = allChildren_s1[1].gameObject.GetInstanceID();
                        first_s.transform.parent = allChildren1[i + 1];
                        first_s.transform.localPosition = position;
                        first_s.transform.localEulerAngles = position;
                        first_s.transform.localScale = scale;
                    }
                }
                else
                {
                    string first_name = first_branch[i]["facetName"].ToString();
                    //生成树枝
                    GameObject first_b = Branch2Random();
                    Transform[] allChildren2 = first_b.GetComponentsInChildren<Transform>();
                    first_b.transform.parent = allChildren1[i + 1];
                    first_b.transform.localPosition = position;
                    first_b.transform.localScale = scale;
                    first_b.transform.localEulerAngles = position;
                    //写上文字
                    TextMesh[] first_textMeshes = first_b.GetComponentsInChildren<TextMesh>();
                    for(int t = 0; t < first_textMeshes.Length; t++)
                    {
                        if (first_textMeshes[t].gameObject.tag == "FirstBranchText")
                        {
                            TextMesh first_textMesh = first_textMeshes[t];
                            first_textMesh.text = first_name;
                            break;
                        }
                    }

                    JsonData second_branch = first_branch[i]["children"];
                    //第二层的flag
                    int second_flag = 0;
                    Dictionary<int, string[]> second_dic = new Dictionary<int, string[]>();
                    int second_sphere = new int();
                    for (int j = 0; j < second_branch.Count; j++)
                    {
                        //第二层循环
                        string second_type = second_branch[j]["type"].ToString();//第二层判断
                        if (second_type == "leaf")
                        {
                            string[] second_webpage = new string[2];
                            string second_url = second_branch[j]["url"].ToString();
                            string second_content = second_branch[j]["assembleContent"].ToString();
                            int second_name = j;
                            second_webpage[0] = second_content;
                            second_webpage[1] = second_url;
                            second_dic.Add(second_name, second_webpage);
                            //生成树叶
                            if (second_flag == 0)
                            {
                                second_flag = 1;
                                second_sphere = allChildren2[1].gameObject.GetInstanceID();
                            }
                        }
                        else
                        {

                            string second_name = second_branch[j]["facetName"].ToString();
                            //生成树枝
                            GameObject second_b = Branch3Random();
                            Transform[] allChildren3 = second_b.GetComponentsInChildren<Transform>();
                            second_b.transform.parent = allChildren2[j + 2];
                            second_b.transform.localPosition = position;
                            second_b.transform.localScale = scale;
                            second_b.transform.localScale = scale;
                            second_b.transform.localEulerAngles = position;
                            //写上文字
                            TextMesh second_textMesh = second_b.GetComponentsInChildren<TextMesh>()[0];
                            second_textMesh.text = second_name;

                            JsonData third_branch = second_branch[j]["children"];
                            //第三层的flag
                            int third_flag = 0;
                            Dictionary<int, string[]> third_dic = new Dictionary<int, string[]>();
                            int third_sphere = new int();
                            int third_NUM = third_branch.Count > 3 ? 3 : third_branch.Count;
                            for (int k = 0; k < third_NUM; k++)
                            {
                                //第三层循环，第三层一定是叶子，不需要判断。
                                string[] third_webpage = new string[2];
                                string third_url = third_branch[k]["url"].ToString();
                                string third_content = third_branch[k]["assembleContent"].ToString();
                                int third_name = k;
                                third_webpage[0] = third_content;
                                third_webpage[1] = third_url;
                                third_dic.Add(third_name, third_webpage);
                                if (third_flag == 0)
                                {
                                    third_flag = 1;
                                    third_sphere = allChildren3[1].gameObject.GetInstanceID();
                                }
                            }
                            if (third_flag == 1)
                            {
                                leaves.Add(third_sphere, third_dic);
                            }

                        }
                    }
                    if (second_flag == 1)
                    {
                        leaves.Add(second_sphere, second_dic);
                    }
                }
            }
            if (first_flag == 1)
            {
                leaves.Add(first_sphere, first_dic);
            }

            // 去除生成树的所有renderer、collider、canvas
            var rendererComponents = groundPlaneStage.GetComponentsInChildren<Renderer>(true);
            var colliderComponents = groundPlaneStage.GetComponentsInChildren<Collider>(true);
            var canvasComponents = groundPlaneStage.GetComponentsInChildren<Canvas>(true);

            // Disable rendering:
            foreach (var component in rendererComponents)
                component.enabled = false;

            // Disable colliders:
            foreach (var component in colliderComponents)
                component.enabled = false;

            // Disable canvas':
            foreach (var component in canvasComponents)
                component.enabled = false;
        }

    }

    private GameObject Branch2Random()
    {
        int branchName = UnityEngine.Random.Range(1, 6);
        GameObject branch2;
        switch (branchName)
        {
            case 1:
                branch2 = Instantiate(b2_1);
                break;
            case 2:
                branch2 = Instantiate(b2_2);
                break;
            case 3:
                branch2 = Instantiate(b2_3);
                break;
            case 4:
                branch2 = Instantiate(b2_4);
                break;
            case 5:
                branch2 = Instantiate(b2_5);
                break;
            default:
                branch2 = Instantiate(b2_3);
                break;

        }
        return branch2;
    }

    private GameObject Branch3Random()
    {
        int branchName = UnityEngine.Random.Range(1, 7);
        GameObject branch3;
        switch (branchName)
        {
            case 1:
                branch3 = Instantiate(b3_1);
                break;
            case 2:
                branch3 = Instantiate(b3_2);
                break;
            case 3:
                branch3 = Instantiate(b3_3);
                break;
            case 4:
                branch3 = Instantiate(b3_4);
                break;
            case 5:
                branch3 = Instantiate(b3_5);
                break;
            case 6:
                branch3 = Instantiate(b3_6);
                break;
            default:
                branch3 = Instantiate(b3_3);
                break;

        }
        return branch3;
    }

    // 重新开始拍照
    public void OnRestartButtonClicked()
    {
        restartButton.SetActive(false);
        startButton.SetActive(true);

        if (TrackingFlag==2)
        {
            // 对应正在追踪但还没有点击关键词但情况
            // 销毁cillider button
            for (int c = 0; c < cubeButton.Length; c++)  
            {
                Destroy(cubeButton[c]);
            }
            // 关闭追踪器
            trackers.Dispose();
        }

        if (TrackingFlag==3)
        {
            // 对应点击关键词后生成了树但情况
            // 将树销毁
            Destroy(treeTransform.gameObject);
            // 将ground plane finder置为不可见
            groundPlaneFinder.SetActive(false);
            // 销毁所有content下的button
            OnMessageButtonClicked[] buttonsInContent = content.GetComponentsInChildren<OnMessageButtonClicked>();
            for (int b = 0; b < buttonsInContent.Length; b++)
            {
                Destroy(buttonsInContent[b].gameObject);
            }
            // 将scroll view置为不可见
            scrollView.gameObject.SetActive(false);
        }

        // 将tracking flag置为0
        TrackingFlag = 0;
    }


    // 反转图片
    private Texture2D horizontalFlipPic(Texture2D texture2d)
    {
        int width = texture2d.width;    // 图片宽度  
        int height = texture2d.height;  // 图片高度 

        Texture2D newTexture2d = new Texture2D(width, height);  // 创建等大小的新Texture2D 

        int i = 0;
        while (i < width)
        {
            newTexture2d.SetPixels(i, 0, 1, height, texture2d.GetPixels(width - i - 1, 0, 1, height));
            i++;
        }
        newTexture2d.Apply();

        return newTexture2d;
    }
}

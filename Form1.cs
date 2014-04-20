using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZedGraph;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;

namespace WaveBT_Server
{
    public partial class Form1 : Form
    {
        private PointPairList list_ecg = new PointPairList();   //ECG显示列表
        private LineItem myCurve_ecg;
        private const int N_buf = 20;
        private static int myProt = 4000;   //端口  
        private string ipadd = "192.168.1.100";//IP地址
        private Socket serverSocket;         //服务器Socket
        private delegate void DisplayDelegate();

        /// <summary>
        /// 窗口构造函数
        /// </summary>
        public Form1()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 窗口初始化：图表，服务器连接
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_Load(object sender, EventArgs e)
        {
            this.zedGraphControl1.GraphPane.XAxis.Type = ZedGraph.AxisType.DateAsOrdinal;
            myCurve_ecg = zedGraphControl1.GraphPane.AddCurve("心电信号",
                list_ecg, Color.DarkGreen, SymbolType.None);
            //初始化服务器
            //服务器IP地址  
            IPAddress ip = IPAddress.Parse(ipadd);
            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(new IPEndPoint(ip, myProt));  //绑定IP地址：端口  
            serverSocket.Listen(10);    //设定最多10个排队连接请求  
            //新线程，监听客户端
            Thread myThread = new Thread(ListenClientConnect);
            myThread.Start();
        }

        /// <summary>  
        /// 监听客户端线程
        /// </summary>  
        private void ListenClientConnect()
        {
            //监听客户端
            Socket clientSocket = serverSocket.Accept();
            //新线程，接收数据
            Thread receiveThread = new Thread(ReceiveMessage);
            receiveThread.Start(clientSocket);
        }

        /// <summary>
        /// 接收数据线程
        /// </summary>
        /// <param name="clientSocket"></param>
        private void ReceiveMessage(object clientSocket)
        {
            Socket myClientSocket = (Socket)clientSocket;
            Stream input = new NetworkStream(myClientSocket);
            byte[] InputBuffer_ecg = new byte[40];  //ECG输入缓存
            int count = 0;
            while (true)
            {
                while (count < N_buf*2)
                {
                    InputBuffer_ecg[count] = (byte)input.ReadByte();
                    InputBuffer_ecg[count+1] = (byte)input.ReadByte();
                    int y = (int)((InputBuffer_ecg[count] & 0xff) | ((InputBuffer_ecg[count + 1] & 0xff) << 8));
                    double x = (double)new XDate(DateTime.Now);
                    list_ecg.Add(x, y);
                    //判断显示列表的宽度
                    if (list_ecg.Count >= 800)
                        list_ecg.RemoveAt(0);
                    count += 2;
                }
                //收到N_buf点的byte数据
                count = 0;
                //DisplayDelegate dl = new DisplayDelegate(display);
                //this.BeginInvoke(dl);
            }
        }

        /// <summary>
        /// 刷新波形显示
        /// </summary>
        private void display()
        {
            this.zedGraphControl1.AxisChange();
            this.zedGraphControl1.Refresh();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            display();
        }

    }
}

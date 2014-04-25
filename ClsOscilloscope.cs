using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using ZedGraph;

namespace WaveBT_Server
{
    public class ClsOscilloscope
    {
        /// <summary>
        /// 开始
        /// </summary>
        public void Start()
        {
            RecordThread recordThread = new RecordThread();
        }

        /// <summary>
        /// 录制线程
        /// </summary>
        public class RecordThread
        {
            private const int N_buf = 20;
            private static int myProt = 4000;   //端口  
            private string ipadd = "192.168.1.100";//IP地址
            private Socket serverSocket = null;         //服务器Socket
            private Socket clientSocket = null;
            private Stream inStream = null;
            private byte[] activeBuffer = new byte[128];
            private int activeBufLen = 0;
            private int exgSeq = -1;
            private int motionSeq = -1;

            /// <summary>
            /// 构造函数，连接服务器
            /// </summary>
            public RecordThread()
            {
                //服务器IP地址  
                IPAddress ip = IPAddress.Parse(ipadd);
                serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                serverSocket.Bind(new IPEndPoint(ip, myProt));  //绑定IP地址：端口
                serverSocket.Listen(10);    //设定最多10个排队连接请求
                //新线程，监听客户端
                Thread myThread = new Thread(ListenClientConnect);
                myThread.IsBackground = true;
                myThread.Start();
            }

            /// <summary>
            /// 监听客户端
            /// </summary>
            private void ListenClientConnect()
            {
                clientSocket = serverSocket.Accept();   //获得Socket
                inStream = new NetworkStream(clientSocket); //获得输入流
                //新线程，接收数据
                Thread receiveThread = new Thread(run);
                receiveThread.IsBackground = true;
                receiveThread.Start();
            }

            /// <summary>
            /// 录制线程的run方法
            /// </summary>
            public void run()
            {
                byte[] buf = new byte[128];
                byte[] printBuf;
                int bufLen = 0;
                FileInfo exgFile = new FileInfo(@"D:\exg.data");
                FileStream exgOut = exgFile.Create();

                /* First search for magic "EXGBT" */
                while (true)
                {
                    if (inStream == null)
                    {

                        Thread.Sleep(500);
                        continue;
                    }
                    bufLen = inStream.Read(buf, 0, buf.Length);
                    if (bufLen > 0)
                    {
                        exgOut.Write(buf, 0, bufLen);
                        exgOut.Flush();
                        handle_one_pkt(buf, bufLen);
                    }
                }
            }

            /// <summary>
            /// 处理一个数据包
            /// </summary>
            /// <param name="buf"></param>
            /// <param name="bufLen"></param>
            public void handle_one_pkt(byte[] buf, int bufLen)
            {
                byte[] tmpBuf;
                int remainIdx = 0, i = 0;

                if (activeBufLen > 0)
                {
                    /* 
                     * There are leftover bytes on activeBuffer,
                     * concatenate two buffers together into
                     * activeBuffer, and then processed there
                     */
                    if ((activeBufLen + bufLen) > activeBuffer.Length)
                    {
                        tmpBuf = new byte[2 * (activeBufLen + bufLen)];
                        Array.ConstrainedCopy(activeBuffer, 0, tmpBuf, 0, activeBufLen);
                        Array.ConstrainedCopy(buf, 0, tmpBuf, activeBufLen, bufLen);
                        activeBuffer = tmpBuf;
                    }
                    else
                    {
                        Array.ConstrainedCopy(buf, 0, activeBuffer, activeBufLen, bufLen);
                    }
                    activeBufLen += bufLen;

                    /*
                     * Process activeBuffer/activeBufLen
                     */
                    remainIdx = greedy_dispatcher(activeBuffer, activeBufLen);
                    for (i = 0; i < (activeBufLen - remainIdx); i++)
                    {
                        activeBuffer[i] = activeBuffer[remainIdx + i];
                    }
                    activeBufLen = (activeBufLen - remainIdx);
                }
                else
                {
                    remainIdx = greedy_dispatcher(buf, bufLen);
                    if (activeBuffer.Length < (bufLen - remainIdx))
                    {
                        activeBuffer = new byte[2 * (bufLen - remainIdx)];
                    }
                    Array.ConstrainedCopy(buf, remainIdx, activeBuffer, 0, (bufLen - remainIdx));
                    activeBufLen = bufLen - remainIdx;
                }
            }

            public int greedy_dispatcher(byte[] buf, int bufLen)
            {
                int prevPattern = -1, prevIdx = -1, hint = -1;
                int[] searchResult;
                String[] patterns = new String[3] { "EXGBT", "<<<>>>", "MOTION" };

                do
                {
                    //Log.i("EXG Wave", "Search " + Integer.toString((prevIdx + 1)) + "/" + Integer.toString(bufLen));
                    searchResult = search_next_tag(buf, bufLen, (prevIdx + 1), patterns, hint);
                    if (searchResult[0] != -1)
                    {
                        if (prevPattern == -1)
                        {
                            if (searchResult[1] != 0)
                            {
                                // Warning: there *may* be some garbage
                                // in the front of buf?
                                //Log.i("EXG Wave", "greedy_dispatcher found " +
                                //        searchResult[1] + " bytes of garbage " +
                                //        "at the front");
                            }
                            prevPattern = searchResult[0];
                            prevIdx = searchResult[1];
                            // Skip this turn
                        }
                        else
                        {
                            byte[] tmpBuf = new byte[searchResult[1] - prevIdx];
                            Array.ConstrainedCopy(buf, prevIdx, tmpBuf, 0, searchResult[1] - prevIdx);
                            handle_message(tmpBuf, prevPattern);
                            prevPattern = searchResult[0];
                            prevIdx = searchResult[1];
                        }
                        hint = prevPattern;
                    }
                } while (searchResult[0] != -1);

                if (prevIdx == -1)
                {
                    prevIdx = 0;
                }
                return prevIdx;
            }

            /// <summary>
            /// 分拣EXGBT，MOTION
            /// </summary>
            /// <param name="buf"></param>
            /// <param name="type"></param>
            public void handle_message(byte[] buf, int type)
            {
                int length = 0;
                int seqNum = 0;

                if (type == 0)
                {
                    length = (buf[6] & 0xFF) | ((buf[7] & 0xFF) << 8);
                    seqNum = (buf[8] & 0xFF) | ((buf[9] & 0xFF) << 8);
                    if ((length + 8) != buf.Length)
                    {
                        //Log.e("EXG Wave", "Expected length: " + Integer.toString(length) +
                        //        " , but got: " + Integer.toString(buf.length));
                    }
                    //Log.i("EXG Wave", "EXGBT: " + Integer.toString(seqNum));
                    if (exgSeq == -1)
                    {
                        exgSeq = seqNum;
                    }
                    else
                    {
                        if (exgSeq + 1 != seqNum)
                        {
                            //Log.e("EXG Wave", "EXGBT expected: " + Integer.toString(exgSeq + 1) + ", but got " + Integer.toString(seqNum));
                            //display_string("EXGBT expected: " + Integer.toString(exgSeq + 1) + ", but got " + Integer.toString(seqNum));
                        }
                        exgSeq = seqNum;
                    }
                    byte[] tmpBuf = new byte[buf.Length - 5];
                    Array.ConstrainedCopy(buf, 5, tmpBuf, 0, buf.Length - 5);
                    //exgLogger.LogData(tmpBuf);
                    //display_seq(exgSeq, motionSeq);
                    AddToList(tmpBuf);
                }
                else if (type == 1)
                {
                    byte[] subBuf = new byte[buf.Length - 7];
                    Array.ConstrainedCopy(buf, 7, subBuf, 0, buf.Length - 7);
                    //Log.i("EXG Wave", "MSG: " + new String(subBuf));	
                    //display_message(subBuf);
                }
                else if (type == 2)
                {
                    length = (buf[6] & 0xFF) | ((buf[7] & 0xFF) << 8);
                    seqNum = (buf[8] & 0xFF) | ((buf[9] & 0xFF) << 8);
                    if ((length + 8) != buf.Length)
                    {
                        //Log.e("EXG Wave", "Expected length: " + Integer.toString(length) +
                        //        " , but got: " + Integer.toString(buf.length));
                    }
                    //Log.i("EXG Wave", "MOTION: " + Integer.toString(seqNum));
                    if (motionSeq == -1)
                    {
                        motionSeq = seqNum;
                    }
                    else
                    {
                        if (motionSeq + 1 != seqNum)
                        {
                            //Log.e("EXG Wave", "MOTION expected: " + Integer.toString(motionSeq + 1) + ", but got " + Integer.toString(seqNum));
                            //display_string("MOTION expected: " + Integer.toString(motionSeq + 1) + ", but got " + Integer.toString(seqNum));
                        }
                        motionSeq = seqNum;
                    }
                    byte[] tmpBuf = new byte[buf.Length - 6];
                    Array.ConstrainedCopy(buf, 6, tmpBuf, 0, buf.Length - 6);
                    //motionLogger.LogData(tmpBuf);
                    //display_seq(exgSeq, motionSeq);
                }
                else
                {
                    //Log.e("EXG Wave", "Unknown type: " + Integer.toString(type));
                }
            }

            /// <summary>
            /// 查找标签
            /// </summary>
            /// <param name="buf"></param>
            /// <param name="bufLen"></param>
            /// <param name="startIdx"></param>
            /// <param name="patterns"></param>
            /// <param name="hint"></param>
            /// <returns></returns>
            public int[] search_next_tag(byte[] buf, int bufLen, int startIdx, String[] patterns, int hint)
            {
                int i = 0, j = 0;
                int[] ret = new int[2];
                String tmpStr;
                ret[0] = -1;
                ret[1] = -1;
                char[] bufc = null;


                if (hint < 0)
                {
                    for (i = startIdx; i < bufLen; i++)
                    {
                        for (j = 0; j < patterns.Length; j++)
                        {
                            if ((i + patterns[j].Length) <= bufLen)
                            {
                                bufc = new ASCIIEncoding().GetChars(buf);
                                tmpStr = new String(bufc, i, patterns[j].Length);
                                if (tmpStr.Equals(patterns[j]) == true)
                                {
                                    ret[0] = j;
                                    ret[1] = i;
                                    return ret;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (hint == 0)
                    {
                        if ((bufLen - startIdx) < (4 + 3))
                        {
                            return search_next_tag(buf, bufLen, startIdx, patterns, -1);
                        }
                        else
                        {
                            int tmpLen = buf[startIdx + 5] + (buf[startIdx + 6] << 8);
                            if ((bufLen - startIdx) < (tmpLen + 3 + 4 + 6))
                            {
                                return ret;
                            }
                            else
                            {
                                for (i = 0; i < patterns.Length; i++)
                                {
                                    bufc = new ASCIIEncoding().GetChars(buf);
                                    tmpStr = new String(bufc, (startIdx + 4 + 3 + tmpLen), patterns[i].Length);
                                    if (tmpStr.Equals(patterns[i]) == true)
                                    {
                                        ret[0] = i;
                                        ret[1] = (startIdx + 4 + 3 + tmpLen);
                                        return ret;
                                    }
                                }
                                return search_next_tag(buf, bufLen, startIdx, patterns, -1);
                            }
                        }
                    }
                    else if (hint == 1)
                    {
                        if ((bufLen - startIdx) < (5 + 1))
                        {
                            return search_next_tag(buf, bufLen, startIdx, patterns, -1);
                        }
                        else
                        {
                            int tmpLen = buf[startIdx + 5];
                            if ((bufLen - startIdx) < (tmpLen + 5 + 1 + 6))
                            {
                                return ret;
                            }
                            else
                            {
                                for (i = 0; i < patterns.Length; i++)
                                {
                                    bufc = new ASCIIEncoding().GetChars(buf);
                                    tmpStr = new String(bufc, (startIdx + 5 + 1 + tmpLen), patterns[i].Length);
                                    if (tmpStr.Equals(patterns[i]) == true)
                                    {
                                        ret[0] = i;
                                        ret[1] = (startIdx + 5 + 1 + tmpLen);
                                        return ret;
                                    }
                                }
                                return search_next_tag(buf, bufLen, startIdx, patterns, -1);
                            }
                        }
                    }
                    else if (hint == 2)
                    {
                        if ((bufLen - startIdx) < (5 + 2))
                        {
                            return search_next_tag(buf, bufLen, startIdx, patterns, -1);
                        }
                        else
                        {
                            int tmpLen = buf[startIdx + 5] + (buf[startIdx + 6] << 8);
                            if ((bufLen - startIdx) < (tmpLen + 5 + 2 + 6))
                            {
                                return ret;
                            }
                            else
                            {
                                for (i = 0; i < patterns.Length; i++)
                                {
                                    bufc = new ASCIIEncoding().GetChars(buf);
                                    tmpStr = new String(bufc, (startIdx + 5 + 2 + tmpLen), patterns[i].Length);
                                    if (tmpStr.Equals(patterns[i]) == true)
                                    {
                                        ret[0] = i;
                                        ret[1] = (startIdx + 5 + 2 + tmpLen);
                                        return ret;
                                    }
                                }
                                return search_next_tag(buf, bufLen, startIdx, patterns, -1);
                            }
                        }
                    }

                }
                return ret;
            }

            /// <summary>
            /// 加入显示列表
            /// </summary>
            /// <param name="buf"></param>
            public void AddToList(byte[] buf)
            {
                for (int i = 9; i < buf.Length; i += 2)
                {
                    short y = (short)((buf[i] & 0xff) | ((buf[i + 1] & 0xff) << 8));
                    double x = (double)new XDate(DateTime.Now);
                    Form1.list_ecg.Add(x, y);
                    if (Form1.list_ecg.Count > 800)
                        Form1.list_ecg.RemoveAt(0);
                }
            }
        }


    }
}

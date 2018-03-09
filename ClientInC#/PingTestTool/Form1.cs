using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Timer = System.Windows.Forms.Timer;

namespace PingTestTool
{
    public delegate void UpdateDelegate(string info);
    public partial class Form1 : Form
    {
        private string path = AppDomain.CurrentDomain.BaseDirectory;
        private UpdateDelegate updateUI;

        public class Device
        {
            public string Ip;
            public string Name;
            public PingResult pingResult;
            public Device(string ip, string name)
            {
                Ip = ip;
                Name = name;
                pingResult = new PingResult();
            }
        }

        private List<Device> deviceList = new List<Device>();
        private Thread[] thread;
        private Thread wait;
        private int time_out;
        private int Package_size;
        private int try_times;
        private int interval;
        public enum PingResultEntryStatus
        {
            Success,
            GenericFailureSeeReplyStatus,
            PingAbortedForHighNetworkUsage,
            PingAbortedUnableToGetNetworkUsage,
            ExceptionRaisedDuringPing
        }

        public class PingResultEntry
        {
            public double? Rtt { get; private set; }
            public IPStatus? IpStatus { get; private set; }
            public PingResultEntryStatus Status { get; private set; }
            public DateTime Time { get; private set; }

            public PingResultEntry(double? rtt, IPStatus? ipStatus, PingResultEntryStatus status, DateTime time)
            {
                IpStatus = ipStatus;
                Rtt = rtt;
                Status = status;
                Time = time;
            }
        }

        public class PingResult
        {
            public List<PingResultEntry> results;
            public int? err_num;
            public int? total_num;
            private double? avg;
            private double? speed;
            public PingResult()
            {
                results = new List<PingResultEntry>();
                err_num = 0;
                total_num = 0;
            }

            public void addPingResultEntry(PingResultEntry newEntry)
            {
                // Adding a new entry
                results.Add(newEntry);
                // Reset any previously calculated stats (they where probably already null but who knows...)
                avg = null;
                speed = null;
            }

            public double? getSpeed(int length)
            {
                // Calculating speed if not yet calculated
                if (avg <= 0) return 1024.0F;
                return length / avg * 1000 / 1024;
            }

            public double? getAvg()
            {
                // Calculating avg if not yet calculated
                if (avg == null && results.Count(res => res.Status == PingResultEntryStatus.Success) > 0)
                {
                    avg = 0;
                    int cont = 0;
                    foreach (var e in results.Where(x => x.Status == PingResultEntryStatus.Success))
                    {
                        cont++;
                        avg += e.Rtt;
                    }
                    if (cont > 0)
                    {
                        avg = avg / cont;
                    }
                }
                return avg;
            }
        }

        public Form1()
        {
            InitializeComponent();
            textBox1.Text = path + "ConfigData.xml";
            textBox_timeout.Text = "300";
            textBox_trytimes.Text = "200";
            textBox_packetsize.Text = "4096";
            textBox_port.Text = "62626";
            checkBox1.Checked = false;
            textBox_interval.Text = "100";
            LoadXmlFile(textBox1.Text);
            updateUI = UpdateList;
            timerdraw.Enabled = true;
            timerdraw.Interval = 1000;
            timerdraw.Tick += SetTextBox;
            tcp_sw.AutoFlush = true;
            udp_sw.AutoFlush = true;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (DialogResult.OK == openFileDialog1.ShowDialog())
            {
                textBox1.Text = openFileDialog1.FileName;
                LoadXmlFile(textBox1.Text);
            }
        }

        private void LoadXmlFile(string fileName)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(fileName);
                deviceList.Clear();
                XmlNodeList list = doc.SelectNodes("/Root/Project/DeviceList/Device");
                if (list != null)
                    foreach (XmlNode n in list)
                    {
                        deviceList.Add(new Device(n.Attributes["ip"].Value, n.Attributes["name"].Value));
                    }
                foreach (var dev in deviceList)
                {
                    int i = dataGridView1.Rows.Add();
                    dataGridView1.Rows[i].Cells[0].Value = dev.Name;
                    dataGridView1.Rows[i].Cells[1].Value = dev.Ip;
                    comboBox1.Items.Add(dev.Ip);
                }
                dataGridView1.ClearSelection();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void button_begin_Click(object sender, EventArgs e)
        {
            if (deviceList.Count == 0)
            {
                MessageBox.Show("There is no device！");
                return;
            }

            if (button_begin.Text == "Stop")
            {
                wait?.Abort();
                wait = null;
                for (var i = 0; i < thread.Length; i++)
                {
                    if (thread[i] == null || !thread[i].IsAlive) continue;
                    thread[i].Abort();
                    thread[i] = null;
                }
                button_begin.Text = "Begin";
                textBox_packetsize.Enabled = true;
                textBox_timeout.Enabled = true;
                textBox_trytimes.Enabled = true;
                checkBox1.Enabled = true;
                button_clear.Enabled = true;
                textBox_interval.Enabled = true;
            }
            else
            {
                button_begin.Text = "Stop";
                textBox_packetsize.Enabled = false;
                textBox_timeout.Enabled = false;
                textBox_trytimes.Enabled = false;
                textBox_interval.Enabled = false;
                checkBox1.Enabled = false;
                button_clear.Enabled = false;

                Package_size = Convert.ToInt32(textBox_packetsize.Text);
                time_out = Convert.ToInt32(textBox_timeout.Text);
                try_times = Convert.ToInt32(textBox_trytimes.Text);
                interval = Convert.ToInt32(textBox_interval.Text);
                thread = new Thread[deviceList.Count];
                for (var i = 0; i < deviceList.Count; i++)
                {
                    thread[i] = new Thread(Threadhandler) { IsBackground = true };
                    thread[i].Start(i);
                }

                wait = new Thread(waitforallthread) { IsBackground = true };
                wait.Start();
            }
        }

        private void waitforallthread()
        {
            bool flag = true;
            Thread.Sleep(200);
            while (flag)
            {
                flag = false;
                foreach (var thr in thread.Where(thr => thr.IsAlive))
                {
                    Thread.Sleep(2000);
                    flag = true;
                }
            }

            BeginInvoke(updateUI, "button");
        }

        private void Threadhandler(object index)
        {
            Ping pingSender = new Ping();
            PingOptions options = new PingOptions();
            byte[] buffer = new byte[Package_size];
            options.DontFragment = checkBox1.Checked;
            for (var i = 0; i < try_times; i++)
            {
                Device dev = deviceList[(int)index];
                try
                {
                    PingReply reply = pingSender.Send(dev.Ip, time_out, buffer, options);
                    if (reply.Status == IPStatus.Success)
                    {
                        // All has gone well
                        dev.pingResult.addPingResultEntry(new PingResultEntry(
                            reply.RoundtripTime, reply.Status, PingResultEntryStatus.Success, DateTime.Now));

                    }
                    else
                    {
                        // Something went wrong, wrong but "expected"
                        dev.pingResult.addPingResultEntry(new PingResultEntry(
                            reply.RoundtripTime, reply.Status, PingResultEntryStatus.GenericFailureSeeReplyStatus,
                            DateTime.Now));
                        dev.pingResult.err_num++;
                    }
                    dev.pingResult.total_num++;
                    dev.pingResult.getAvg();
                    dev.pingResult.getSpeed(Package_size);
                }
                catch
                {
                    dev.pingResult.addPingResultEntry(new PingResultEntry(
                                    null, null, PingResultEntryStatus.ExceptionRaisedDuringPing, DateTime.Now));
                }

                BeginInvoke(updateUI, "list" + index);

                Thread.Sleep(interval);
            }
        }

        private void UpdateList(string info)
        {
            if (info.Contains("list"))
            {
                int i = Convert.ToInt32(info.Remove(0, 4));
                Device dev = deviceList[i];
                dataGridView1.Rows[i].Cells[0].Value = dev.Name;
                dataGridView1.Rows[i].Cells[1].Value = dev.Ip;
                var ipStatus = dev.pingResult.results.Last().IpStatus;
                if (ipStatus != null)
                    dataGridView1.Rows[i].Cells[2].Value = ipStatus.ToString();

                var avg = dev.pingResult.getAvg();
                if (avg != null)
                    dataGridView1.Rows[i].Cells[3].Value = Math.Round((double)avg, 2).ToString();
                var speed = dev.pingResult.getSpeed(Package_size);
                if (speed != null)
                    dataGridView1.Rows[i].Cells[4].Value = Math.Round((double)speed, 2).ToString();
                dataGridView1.Rows[i].Cells[5].Value = dev.pingResult.err_num.ToString();
                dataGridView1.Rows[i].Cells[6].Value = dev.pingResult.total_num.ToString();

                dataGridView1.Refresh();
                dataGridView1.ClearSelection();
            }
            else if (info == "button")
            {
                button_begin.Text = "Begin";
                textBox_packetsize.Enabled = true;
                textBox_timeout.Enabled = true;
                textBox_trytimes.Enabled = true;
                checkBox1.Enabled = true;
                button_clear.Enabled = true;
                textBox_interval.Enabled = true;
            }
        }

        private void button_clear_Click(object sender, EventArgs e)
        {
            foreach (var dev in deviceList)
            {
                dev.pingResult.results.Clear();
                dev.pingResult.err_num = 0;
                dev.pingResult.total_num = 0;
            }
            dataGridView1.ResetText();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            dataGridView1.ClearSelection();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            wait?.Abort();
            wait = null;
            if (thread != null)
            {
                for (var i = 0; i < thread.Length; i++)
                {
                    if (thread[i] == null || !thread[i].IsAlive) continue;
                    thread[i].Abort();
                    thread[i] = null;
                }
            }
            tcpthread?.Abort();
            udpthread?.Abort();
            tcp_sw.Close();
            fTcpLog.Close();
            udp_sw.Close();
            fUdpLog.Close();
        }

        enum RCVSTATUS
        {
            RCV_OK = 0,
            RCV_DATAERROR,
            RCV_TIMEOUT,
            RCV_CONNERROR,
        }

        public static string server_IP = "10.16.1.16";
        public static ushort server_Port = 62626;

        public static bool mode = true;    // 0 rising mode 1 constant mode
        public static ushort constLen = 0xc582;

        public bool udpRunning;
        public bool tcpRunning;

        public static bool tcp_cond;
        public static bool udp_cond;

        public static FileStream fUdpLog = new FileStream("ClientUdpLog.txt", FileMode.Create);
        public static FileStream fTcpLog = new FileStream("ClientTcpLog.txt", FileMode.Create);
        public static StreamWriter tcp_sw = new StreamWriter(fTcpLog);
        public static StreamWriter udp_sw = new StreamWriter(fUdpLog);

        public Thread tcpthread;
        public Thread udpthread;

        public static List<string> Strinfo = new List<string>();

        public const bool ModeRising = false;
        public const bool ModeConstant = true;

        public Timer timerdraw = new Timer();

        public void SetTextBox(object sender, EventArgs eventArgs)
        {
            richTextBox1.Text = string.Join(Environment.NewLine, Strinfo);
        }

        //调用API函数
        [DllImport("kernel32.dll")]
        extern static short QueryPerformanceCounter(ref long x);

        public class CTcpNet
        {
            Socket TCPsocket;
            int eData;
            int eTimeout;
            int iFrame;

            public bool NetInit()
            {
                TCPsocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                //----------------------
                // The sockaddr_in structure specifies the address family,
                // IP address, and port for the socket that is being bound.
                try
                {
                    TCPsocket.Connect(new IPEndPoint(IPAddress.Parse(server_IP), server_Port)); //配置服务器IP与端口
                }
                catch (SocketException)
                {
                    Strinfo.Add(@"[TCP]--Error at Connect()");
                    return false;
                }

                Strinfo.Add(@"[TCP] -- Start TCP communication...");
                int optval_buflen = 0x200000;
                TCPsocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, optval_buflen);
                TCPsocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 5000);
                iFrame = 0;
                return true;
            }

            public void NetEcho()
            {
                byte[] rcvbuf = new byte[65524];
                byte[] sndbuf = new byte[65524];

                ushort sndlength;
                ushort rcvlength = 0;

                sndbuf[0] = 0x7;
                sndbuf[1] = 0;

                RCVSTATUS m_rcvStatus = RCVSTATUS.RCV_OK;

                int curRcvLen;
                int tmpSndLen;
                int tmpRcvLen;
                tmpRcvLen = 0;

                long nStart = 0;
                long nEnd = 0;
                int tDelt;
                int bWide;

                DateTime tNow;
                tNow = DateTime.Now;

                tcp_sw.WriteLine(tNow + "[TCP]--Communication Start");

                while ((RCVSTATUS.RCV_CONNERROR != m_rcvStatus) && tcp_cond)
                {
                    if (ModeRising == mode)
                        sndlength = (ushort)(iFrame % 0xfe00 + 9);
                    else
                        sndlength = constLen;

                    iFrame++;
                    m_rcvStatus = RCVSTATUS.RCV_OK;

                    QueryPerformanceCounter(ref nStart);

                    try
                    {
                        tmpSndLen = TCPsocket.Send(sndbuf, sndlength, 0);
                        curRcvLen = 0;
                        while (curRcvLen < tmpSndLen)
                        {
                            try
                            {
                                tmpRcvLen = TCPsocket.Receive(rcvbuf, curRcvLen, tmpSndLen - curRcvLen, 0);
                                if (tmpRcvLen > 0)
                                {
                                    curRcvLen += tmpRcvLen;
                                }
                                else if (tmpRcvLen == 0 || -1 == tmpRcvLen)
                                {
                                    tNow = DateTime.Now;
                                    tcp_sw.WriteLine(tNow + "[TCP]--recv tmpRcvLen error");
                                    m_rcvStatus = RCVSTATUS.RCV_CONNERROR;
                                    break;
                                }
                            }
                            catch (SocketException ex)
                            {
                                if (ex.SocketErrorCode == SocketError.TimedOut)
                                {
                                    tNow = DateTime.Now;

                                    tcp_sw.WriteLine(tNow + $"[TCP]--select {iFrame:X} timeout");
                                    tcp_sw.WriteLine("curRcvLen = {0:X}, tmpSndLen = {1:X} sndAllLength= {2:X}, sndBuf= {3:X}", curRcvLen, tmpSndLen, sndlength, sndbuf[2]);
                                    m_rcvStatus = RCVSTATUS.RCV_TIMEOUT;
                                    break;
                                }
                                tNow = DateTime.Now;
                                tcp_sw.WriteLine(tNow + $"[TCP]--select {iFrame:X}" + ex.SocketErrorCode);
                                m_rcvStatus = RCVSTATUS.RCV_CONNERROR;
                                break;
                            }
                        }
                        QueryPerformanceCounter(ref nEnd);
                        bool res = true;
                        for (var i = 0; i < tmpSndLen; i++)
                        {
                            if (sndbuf[i] == rcvbuf[i]) continue;
                            res = false;
                            break;
                        }

                        if (m_rcvStatus == RCVSTATUS.RCV_OK && (curRcvLen != tmpSndLen || res))
                        {
                            tNow = DateTime.Now;
                            tcp_sw.WriteLine(tNow + @"[TCP]-- {0:x} data error\r\n", iFrame);
                            m_rcvStatus = RCVSTATUS.RCV_DATAERROR;
                        }
                    }
                    catch (SocketException ex)
                    {
                        tNow = DateTime.Now;
                        tcp_sw.WriteLine(tNow + $@"[TCP]--send {iFrame:x} error " + ex.SocketErrorCode + @"\r\n");
                        m_rcvStatus = RCVSTATUS.RCV_CONNERROR;
                    }

                    

                    switch (m_rcvStatus)
                    {
                        case RCVSTATUS.RCV_OK:
                            tDelt = (int)(nEnd - nStart);
                            bWide = (int)(sndlength * 2 * 8 * 1000 / (double)tDelt);    //kbps
                            Strinfo.Add($@"[TCP] -- length: {sndlength:X6}, data_error={eData:d}, time_out={eTimeout:d}, {tDelt:d3}us /{bWide:d}kbps");
                            break;
                        case RCVSTATUS.RCV_DATAERROR:
                            eData++;
                            Strinfo.Add($"[TCP] -- length:{sndlength:X6}, data_error={eData:d}, time_out={eTimeout:d}");
                            break;
                        case RCVSTATUS.RCV_TIMEOUT:
                            eTimeout++;
                            Strinfo.Add($"[TCP] -- length:{sndlength:X6}, data_error={eData:d}, time_out={eTimeout:d}");
                            break;
                        case RCVSTATUS.RCV_CONNERROR:
                            Strinfo.Add("[TCP] -- connection error.");
                            goto ret;
                    }
                }
                ret:
                tNow = DateTime.Now;
                tcp_sw.WriteLine(tNow + "[TCP]--Communication Stop");
                tcp_sw.WriteLine("data_error = {0:d}, time_out = {1:d}", eData, eTimeout);
            }

            public void NetClose()
            {
                TCPsocket.Close();
            }
        }

        public class CUdpNet
        {
            Socket UDPsocket;
            int eData;
            int eTimeout;
            int iFrame;

            public bool NetInit()
            {
                UDPsocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                //----------------------
                // The sockaddr_in structure specifies the address family,
                // IP address, and port for the socket that is being bound.
                int optval_buflen = 0x200000;
                Strinfo.Add(@"[UDP] -- Start Udp communication...");
                UDPsocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 5000);
                UDPsocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, optval_buflen);
                UDPsocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, optval_buflen);
                iFrame = 0;
                return true;
            }

            public void NetEcho()
            {
                byte[] rcvbuf = new byte[65524];
                byte[] sndbuf = new byte[65524];

                ushort sndlength;
                ushort rcvlength = 0;

                sndbuf[0] = 0x7;
                sndbuf[1] = 0;

                RCVSTATUS m_rcvStatus = RCVSTATUS.RCV_OK;

                for (int cnt = 2; cnt < 65524; cnt++)
                    sndbuf[cnt] = 0x7;

                EndPoint sendpoint = new IPEndPoint(IPAddress.Parse(server_IP), server_Port);
                EndPoint recvpoint = new IPEndPoint(IPAddress.Any, 0);
                int curRcvLen;
                int tmpSndLen;
                int tmpRcvLen;

                long nStart = 0;
                long nEnd = 0;
                int tDelt;
                int bWide;

                DateTime tNow;
                tNow = DateTime.Now;

                udp_sw.WriteLine(tNow + "[UDP]--Communication Start");

                while ((RCVSTATUS.RCV_CONNERROR != m_rcvStatus) && udp_cond)
                {
                    if (ModeRising == mode)
                        sndlength = (ushort)(iFrame % 0xfe00 + 9);
                    else
                        sndlength = constLen;

                    iFrame++;
                    m_rcvStatus = RCVSTATUS.RCV_OK;

                    QueryPerformanceCounter(ref nStart);

                    try
                    {
                        tmpSndLen = UDPsocket.SendTo(sndbuf, sndlength, 0, sendpoint);

                        curRcvLen = 0;

                        while (curRcvLen < tmpSndLen)
                        {
                            try
                            {
                                tmpRcvLen = UDPsocket.ReceiveFrom(rcvbuf, curRcvLen, tmpSndLen - curRcvLen, 0, ref recvpoint);
                                if (tmpRcvLen > 0)
                                {
                                    curRcvLen += tmpRcvLen;
                                }
                                else if (tmpRcvLen == 0 || -1 == tmpRcvLen)
                                {
                                    tNow = DateTime.Now;
                                    udp_sw.WriteLine(tNow + "[UDP]--recv tmpRcvLen error");
                                    m_rcvStatus = RCVSTATUS.RCV_CONNERROR;
                                    break;
                                }
                            }
                            catch (SocketException ex)
                            {
                                if (ex.SocketErrorCode == SocketError.TimedOut)
                                {
                                    tNow = DateTime.Now;

                                    udp_sw.WriteLine(tNow + $"[UDP]--select {iFrame:X} timeout");
                                    udp_sw.WriteLine("curRcvLen = {0:X}, tmpSndLen = {1:X} sndAllLength= {2:X}, sndBuf= {3:X}", curRcvLen, tmpSndLen, sndlength, sndbuf[2]);
                                    m_rcvStatus = RCVSTATUS.RCV_TIMEOUT;
                                    break;
                                }
                                tNow = DateTime.Now;
                                udp_sw.WriteLine(tNow + $"[UDP]--select {iFrame:X}" + ex.SocketErrorCode);
                                m_rcvStatus = RCVSTATUS.RCV_CONNERROR;
                                break;
                            }
                        }

                        QueryPerformanceCounter(ref nEnd);
                        bool res = true;
                        for (var i = 0; i < tmpSndLen; i++)
                        {
                            if (sndbuf[i] == rcvbuf[i]) continue;
                            res = false;
                            break;
                        }

                        if (m_rcvStatus == RCVSTATUS.RCV_OK && (curRcvLen != tmpSndLen || res))
                        {
                            tNow = DateTime.Now;
                            udp_sw.WriteLine(tNow + "[UDP]-- {0:x} data error", iFrame);
                            m_rcvStatus = RCVSTATUS.RCV_DATAERROR;
                        }
                    }
                    catch (SocketException ex)
                    {
                        tNow = DateTime.Now;
                        udp_sw.WriteLine(tNow + $"[UDP]--send {iFrame:x} error " + ex.SocketErrorCode);
                        m_rcvStatus = RCVSTATUS.RCV_CONNERROR;
                    }

                    

                    switch (m_rcvStatus)
                    {
                        case RCVSTATUS.RCV_OK:
                            tDelt = (int)(nEnd - nStart);
                            bWide = (int)(sndlength * 2 * 8 * 1000 / (double)tDelt);    //kbps
                            Strinfo.Add($@"[UDP] -- length: {sndlength:X6}, data_error={eData:d}, time_out={eTimeout:d}, {tDelt:d3}us /{bWide:d}kbps");
                            break;
                        case RCVSTATUS.RCV_DATAERROR:
                            eData++;
                            Strinfo.Add($"[UDP] -- length:{sndlength:X6}, data_error={eData:d}, time_out={eTimeout:d}");
                            break;
                        case RCVSTATUS.RCV_TIMEOUT:
                            eTimeout++;
                            Strinfo.Add($"[UDP] -- length:{sndlength:X6}, data_error={eData:d}, time_out={eTimeout:d}");
                            break;
                        case RCVSTATUS.RCV_CONNERROR:
                            Strinfo.Add("[UDP] -- connection error.");
                            goto ret;
                    }
                }
                ret:
                tNow = DateTime.Now;
                udp_sw.WriteLine(tNow + "[UDP]--Communication Stop");
                udp_sw.WriteLine("data_error = {0:d}, time_out = {1:d}", eData, eTimeout);
            }

            public void NetClose()
            {
                UDPsocket.Close();
            }
        }

        public void ThreadTcpEcho()
        {
            CTcpNet mTcpClient = new CTcpNet();
            while (tcp_cond)
            {
                if (mTcpClient.NetInit())
                    mTcpClient.NetEcho();
                mTcpClient.NetClose();
            }
        }

        public void ThreadUdpEcho()
        {
            CUdpNet mUdpClient = new CUdpNet();

            while (udp_cond)
            {
                if (mUdpClient.NetInit())
                    mUdpClient.NetEcho();
                mUdpClient.NetClose();
            }
        }

        public void buttonStart_Click(object sender, EventArgs e)
        {

            if (buttonStart.Text == "Start")
            {
                buttonStart.Text = "Stop";

                mode = !checkBox2.Checked;
                tcp_cond = rdb_tcp.Checked;
                udp_cond = rdb_udp.Checked;
                server_Port = Convert.ToUInt16(textBox_port.Text);
                if (comboBox1.SelectedItem != null) server_IP = comboBox1.SelectedItem.ToString();
                timerdraw.Start();
                tcpthread = new Thread(ThreadTcpEcho);
                tcpthread.Start();
                udpthread = new Thread(ThreadUdpEcho);
                udpthread.Start();
            }
            else
            {
                buttonStart.Text = "Start";
                tcp_cond = false;
                udp_cond = false;

                tcpthread?.Abort();
                tcpthread = null;
                udpthread?.Abort();
                udpthread = null;
                timerdraw.Stop();
            }
        }

        private void richTextBox1_TextChanged(object sender, EventArgs e)
        {
            richTextBox1.SelectionStart = richTextBox1.Text.Length;
            richTextBox1.ScrollToCaret();
        }
    }
}
